﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Helpers;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class InnerCircleVotingController
    {
        private readonly DialogManager _dialogManager;
        private readonly ITelegramClient _client;
        private readonly BroadcastController _broadcastController;
        private readonly IRepository _repository;
        private readonly RandomTaskRepository _randomTaskRepository;
        private readonly LocStrings _loc;
        private readonly IBotConfiguration _configuration;
        private static readonly ILog logger = Log.Get(typeof(InnerCircleVotingController));

        public InnerCircleVotingController(DialogManager dialogManager,
            ITelegramClient client,
            BroadcastController broadcastController,
            IRepository repository,
            RandomTaskRepository randomTaskRepository,
            LocStrings loc, IBotConfiguration configuration)
        {
            _dialogManager = dialogManager;
            _client = client;
            _broadcastController = broadcastController;
            _repository = repository;
            _randomTaskRepository = randomTaskRepository;
            _loc = loc;
            _configuration = configuration;
        }

        public async Task<bool> PremoderateTaskForNewRound(bool isReactivation)
        {
            var state = _repository.GetOrCreateCurrentState();

            var winner = _repository.GetExistingUserWithTgId(state.CurrentWinnerId ?? 0);

            if (winner?.ChatId == null)
            {
                logger.Error($"User with Id = {state.CurrentWinnerId} not found / his ChatId = null, falling to ModerateWithoutWinner mode");

                var result = await PremoderateTaskForNewRoundInternal(state, isReactivation, true);

                logger.Info($"Finally, voting resulted in following task: {state.CurrentTaskTemplate}");

                // I don't really have a clue how to set CurrentTaskType in this case (or whether I even should)
                // In any case, it doesn't seem to matter at this point
                _repository.UpdateState(s => s.CurrentTaskTemplate, result.Item2);

                return true;
            }

            using (var dialog = _dialogManager.StartNewDialogExclusive(winner.ChatId.Value, winner.Id, "PremoderateTaskForNewRound"))
            {
                dialog.Tag = "PremoderateTaskForNewRound";
                try
                {
                    var result = await PremoderateTaskForNewRoundInternal(state, isReactivation, false);

                    if (result.Item1 == false)
                    {
                        await _client.SendTextMessageAsync(dialog.ChatId,
                            LocTokens.SubstituteTokens(_loc.InnerCircleDeclinedMessage,
                                Tuple.Create(LocTokens.Details,
                                    result.Item2)),
                            ParseMode.Html);

                        return false;
                    }

                    if (result.Item1 == true)
                        await _client.SendTextMessageAsync(dialog.ChatId, _loc.InnerCircleApprovedTaskMessage,
                            ParseMode.Html);

                    // Sets a task kind to manual. Makes sense when overriding a random task
                    if (result.Item1 == null)
                        _repository.UpdateState(s => s.CurrentTaskKind, SelectedTaskKind.Manual);

                    //(Say nothing to CurrentWinnerId on override)

                    logger.Info($"Finally, voting resulted in following task: {state.CurrentTaskTemplate}");

                    _repository.UpdateState(s => s.CurrentTaskTemplate, result.Item2);
                }
                finally
                {
                    _dialogManager.RecycleDialog(dialog);
                }
            }

            return true;
        }

        private enum VotingResult
        {
            Override,
            Deny,
            Approve,
            Skipped
        }

        private async Task<Tuple<bool?, string>> PremoderateTaskForNewRoundInternal(SystemState state,
            bool isReactivation, bool isVotingWithoutWinner)
        {
            var allAdmins = _repository.GetAllActiveUsersWithCredentials(UserCredentials.Admin);

            if (allAdmins.Length == 0)
            {
                logger.Warn($"Lol, no active administrators found, lol. Auto-approve...");
                return Tuple.Create<bool?, string>(true, state.CurrentTaskTemplate);
            }

            var taskInfo = state.CurrentTaskInfo;

            bool isOptedForRandomTask = taskInfo.Item1 == SelectedTaskKind.Random;

            if (isOptedForRandomTask)
            {
                var randomTask = _randomTaskRepository.GetRandomTaskDescription();
                taskInfo = Tuple.Create(SelectedTaskKind.Random, randomTask);
            }

            //start administrative voting cycle

            var preliminaryVotingCompletionSource = new TaskCompletionSource<VotingResult>();
            var cts = new CancellationTokenSource();

            var completedTasks = await Task.WhenAll(allAdmins.Select(admin => VoteAsync(admin,
                taskInfo.Item2,
                isOptedForRandomTask,
                preliminaryVotingCompletionSource,
                isReactivation, isVotingWithoutWinner,
                    cts.Token).
                ContinueWith(task => NotifyOthersReturnResult(task.Result, allAdmins, cts, preliminaryVotingCompletionSource), CancellationToken.None)));

            //here we can use Result because all tasks are guaranteed to complete
            var results = completedTasks.Select(t => t.Result).ToArray();

            cts.Cancel();

            var ovrResult = results.FirstOrDefault(r => r.Item2 == VotingResult.Override);
            //we got override
            if (ovrResult != null)
                return Tuple.Create<bool?, string>(null, ovrResult.Item3);

            var denyResult = results.FirstOrDefault(r => r.Item2 == VotingResult.Deny);
            //we got denial
            if (denyResult != null)
                return Tuple.Create<bool?, string>(false, denyResult.Item3);

            //approved
            return Tuple.Create<bool?, string>(true, taskInfo.Item2);
        }

        private async Task<Tuple<User, VotingResult, string>> NotifyOthersReturnResult(
            Tuple<User, VotingResult, string> taskResult, User[] allAdmins, CancellationTokenSource cts,
            TaskCompletionSource<VotingResult> preliminaryVotingCompletionSource)
        {
            var issuedBy = taskResult.Item1;

            if (taskResult.Item2 == VotingResult.Skipped)
            {
                logger.Info($"Admin {issuedBy.GetUsernameOrNameWithCircumflex()} voting result : skipped");

                //fire timeout timer anyway (for case when admin terminated voting dialog)

                if (!cts.IsCancellationRequested)
                    cts.CancelAfter(TimeSpan.FromHours(_configuration.MaxAdminVotingTimeHoursSinceFirstVote));

                return taskResult;
            }

            //Cancel other's selection process
            if (taskResult.Item2 != VotingResult.Approve)
                preliminaryVotingCompletionSource.TrySetResult(taskResult.Item2);
            else if (!cts.IsCancellationRequested)
                cts.CancelAfter(TimeSpan.FromHours(_configuration.MaxAdminVotingTimeHoursSinceFirstVote));

            foreach (var admin in allAdmins)
            {
                if (admin.Id == issuedBy.Id)
                    continue;

                if (admin.ChatId == null)
                {
                    logger.Warn($"Admin {admin.GetUsernameOrNameWithCircumflex()} has no ChatId set");
                    continue;
                }

                string details = string.Empty;

                switch (taskResult.Item2)
                {
                    case VotingResult.Override:

                        details = LocTokens.SubstituteTokens(_loc.AdminVotingDetailsOverridden,
                            Tuple.Create(LocTokens.Details, taskResult.Item3));

                        break;
                    case VotingResult.Deny:

                        details = LocTokens.SubstituteTokens(_loc.AdminVotingDetailsDenied,
                            Tuple.Create(LocTokens.Details, taskResult.Item3));

                        break;
                    case VotingResult.Approve:

                        details = _loc.AdminVotingDetailsApproved;

                        break;
                    default:
                        break;
                }

                await _client.SendTextMessageAsync(admin.ChatId,
                    LocTokens.SubstituteTokens(_loc.AdminVotingSomeoneVotedNotification,
                        Tuple.Create(LocTokens.Details, details),
                        Tuple.Create(LocTokens.User, issuedBy.GetHtmlUserLink())),
                    ParseMode.Html);
            }

            return taskResult;
        }

        private async Task<Tuple<User, VotingResult, string>> VoteAsync(User admin,
            string taskTemplate,
            bool isOptedForRandomTask,
            TaskCompletionSource<VotingResult> preliminaryVotingCompletionSource, bool isReactivation,
            bool isVotingWithoutWinner,
            CancellationToken token)
        {
            if (admin.ChatId == null)
            {
                logger.Info($"Warning : no chat for admin {admin.GetUsernameOrNameWithCircumflex()}, issued auto-approve action but that should not happen");
                return Tuple.Create(admin, VotingResult.Approve, String.Empty);
            }

            var callback = new
            {
                approve = "ok",
                decline = "no",
                overrideId = "ovr"
            };
            using (var dialog = _dialogManager.StartNewDialogExclusive(admin.ChatId.Value, admin.Id, "VoteAsync"))
            {
                try
                {

                    var inlineKeyboardButtons = new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData(_loc.AdminApproveLabel, callback.approve),
                    };

                    if (!isVotingWithoutWinner)
                        inlineKeyboardButtons.Add(
                            InlineKeyboardButton.WithCallbackData(_loc.AdminDeclineLabel, callback.decline));

                    if (admin.Credentials.HasFlag(UserCredentials.Supervisor))
                        inlineKeyboardButtons.Add(
                            InlineKeyboardButton.WithCallbackData(_loc.AdminOverrideLabel, callback.overrideId));

                    if (isReactivation)
                        await _client.SendTextMessageAsync(dialog.ChatId, _loc.GeneralReactivationDueToErrorsMessage,
                            ParseMode.Html, cancellationToken: token);
                    if (isVotingWithoutWinner)
                        await _client.SendTextMessageAsync(dialog.ChatId, _loc.VotingWithoutWinnerSituation,
                            ParseMode.Html, cancellationToken: token);

                    var message = await _client.SendTextMessageAsync(dialog.ChatId, LocTokens.SubstituteTokens(
                            _loc.AdminVotingPrivateStarted,
                            Tuple.Create(LocTokens.Details, taskTemplate)), ParseMode.Html,
                        replyMarkup: new InlineKeyboardMarkup(inlineKeyboardButtons), cancellationToken: token);

                    if (null == message)
                    {
                        logger.Warn(
                            $"Couldnt send voting initiation to {admin.GetUsernameOrNameWithCircumflex()}, skipping admin in voting sequence");
                        return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
                    }

                    if (isOptedForRandomTask)
                        await _client.SendTextMessageAsync(dialog.ChatId, _loc.AdminVotingTaskFromRandomTaskRepository,
                            parseMode: ParseMode.Html,
                            cancellationToken: token);

                    //this either gets us callback, or perliminary voting result ('deny' or 'override' from some admin)

                    var result = await Task.WhenAny(
                        TaskEx.TaskToObject(dialog.GetCallbackQueryAsync(token)),
                        TaskEx.TaskToObject(preliminaryVotingCompletionSource.Task)).Unwrap();

                    //Remove action buttons
                    await _client.EditMessageReplyMarkupAsync(message.Chat.Id, message.MessageId,
                        replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0]),
                        cancellationToken: token); //remove

                    switch (result)
                    {
                        //this admin pressed button
                        case CallbackQuery query:

                            await _client.AnswerCallbackQueryAsync(query.Id, "got ya", cancellationToken: token);

                            if (query.Data == callback.approve)
                            {
                                logger.Info($"Admin {admin.GetUsernameOrNameWithCircumflex()} approves task template");
                                return Tuple.Create(admin, VotingResult.Approve, String.Empty);
                            }
                            else if (query.Data == callback.decline)
                            {
                                logger.Info($"Admin {admin.GetUsernameOrNameWithCircumflex()} DECLINES task template");

                                var reason = await dialog.AskForMessageWithConfirmation(token,
                                    _loc.AdminVotingTypeReasonForDeclineMessage);

                                logger.Info($"Admin {admin.GetUsernameOrNameWithCircumflex()} reason: `{reason}`");

                                return Tuple.Create(admin, VotingResult.Deny, reason);
                            }
                            else if (query.Data == callback.overrideId)
                            {
                                logger.Info($"Admin {admin.GetUsernameOrNameWithCircumflex()} OVERRIDES task template");

                                var template = await dialog.AskForMessageWithConfirmation(token,
                                    _loc.AdminVotingTypeOverridingTaskMessage);

                                logger.Info(
                                    $"Admin {admin.GetUsernameOrNameWithCircumflex()} override result `{template}`");

                                return Tuple.Create(admin, VotingResult.Override, template);
                            }

                            break;
                        //other stopped voting
                        case VotingResult votingResult:

                            return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
                    }



                }
                catch (TaskCanceledException)
                {
                    return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
                }
                catch (BadRequestException e)
                {
                    logger.Error(
                        $"One of admin workers voting ({admin.GetUsernameOrNameWithCircumflex()}) resulted in BadRequestException: {e.Message}");
                    return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
                }
                catch (Exception e)
                {
                    logger.Error(
                        $"One of admin workers voting ({admin.GetUsernameOrNameWithCircumflex()}) resulted in exception",
                        e);
                    return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
                }
                finally
                {
                    _dialogManager.RecycleDialog(dialog);
                }
            }

            logger.Warn($"Somehow fallen through all admin voting options : {admin.GetUsernameOrNameWithCircumflex()}");

            //Should not normally get here
            return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
        }


    }
}
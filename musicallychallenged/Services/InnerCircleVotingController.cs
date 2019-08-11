using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using Telegram.Bot;
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
        private readonly BotConfiguration _configuration;
        private static readonly ILog logger = Log.Get(typeof(InnerCircleVotingController));

        public InnerCircleVotingController(DialogManager dialogManager,
            ITelegramClient client,
            BroadcastController broadcastController,
            IRepository repository,
            RandomTaskRepository randomTaskRepository,
            LocStrings loc, BotConfiguration configuration)
        {
            _dialogManager = dialogManager;
            _client = client;
            _broadcastController = broadcastController;
            _repository = repository;
            _randomTaskRepository = randomTaskRepository;
            _loc = loc;
            _configuration = configuration;
        }

        public async Task<bool> PremoderateTaskForNewRound()
        {
            var state = _repository.GetOrCreateCurrentState();

            var user = _repository.GetExistingUserWithTgId(state.CurrentWinnerId ?? 0);

            if (null == user)
            {
                logger.Error($"User with Id = {state.CurrentWinnerId} not found");
                return true;
            }
            
            if (null == user.ChatId)
            {
                logger.Error($"Chat for user with Id = {state.CurrentWinnerId} not found");
                return true;
            }

            using (var dialog = _dialogManager.StartNewDialogExclusive(user.ChatId.Value, user.Id))
            {
                try
                {
                    var result = await PremoderateTaskForNewRoundInternal(state);

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
            
                    //(Say nothing to CurrentWinnerId on override)

                    logger.Info($"Finally, voting resulted in following task: {state.CurrentTaskTemplate}");
                
                    _repository.UpdateState(_=>_.CurrentTaskTemplate, result.Item2);
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
      
        private async Task<Tuple<bool?, string>> PremoderateTaskForNewRoundInternal(SystemState state)
        {
            var allAdmins = _repository.GetAllActiveUsersWithCredentials(UserCredentials.Admin);

            if (allAdmins.Length == 0)
            {
                logger.Warn($"Lol, no active administrators found, lol. Auto-approve...");
                return Tuple.Create<bool?, string>(true,null);
            }

            var taskTemplate = state.CurrentTaskTemplate;

            bool isOptedForRandomTask = string.IsNullOrWhiteSpace(taskTemplate) ||
                                        taskTemplate == NewTaskSelectorController.RandomTaskCallbackId;

            if (isOptedForRandomTask)
                taskTemplate = _randomTaskRepository.GetRandomTaskDescription();

            //start administrative voting cycle

            var preliminaryVotingCompletionSource = new TaskCompletionSource<VotingResult>();
            var cts = new CancellationTokenSource();

            var completedTasks = await Task.WhenAll(allAdmins.Select(admin => VoteAsync(admin,
                taskTemplate,
                isOptedForRandomTask,
                preliminaryVotingCompletionSource,cts.Token).
                ContinueWith(task => NotifyOthersReturnResult(task.Result, allAdmins, cts,preliminaryVotingCompletionSource), CancellationToken.None)));

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
            return Tuple.Create<bool?, string>(true, taskTemplate);
        }

        private async Task<Tuple<User, VotingResult, string>> NotifyOthersReturnResult(
            Tuple<User, VotingResult, string> taskResult, User[] allAdmins, CancellationTokenSource cts,
            TaskCompletionSource<VotingResult> preliminaryVotingCompletionSource)
        {
            var issuedBy = taskResult.Item1;

            if (taskResult.Item2 == VotingResult.Skipped)
            {
                logger.Info($"Admin {issuedBy.GetUsernameOrNameWithCircumflex()} voting result : skipped");
                return taskResult;
            }

            //Cancel other's selection process
            if (taskResult.Item2 != VotingResult.Approve)
                preliminaryVotingCompletionSource.TrySetResult(taskResult.Item2);
            else
                cts.CancelAfter(TimeSpan.FromHours(_configuration.MaxAdminVotingTimeHoursSinceFirstVote));

            foreach (var admin in allAdmins)
            {
                if(admin.Id== issuedBy.Id)
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
            TaskCompletionSource<VotingResult> preliminaryVotingCompletionSource, CancellationToken token)
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

            try
            {
                using (var dialog = _dialogManager.StartNewDialogExclusive(admin.ChatId.Value, admin.Id))
                {
                    var inlineKeyboardButtons = new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData(_loc.AdminApproveLabel, callback.approve),
                        InlineKeyboardButton.WithCallbackData(_loc.AdminDeclineLabel, callback.decline),
                    };

                    if (admin.Credentials.HasFlag(UserCredentials.Supervisor))
                        inlineKeyboardButtons.Add(
                            InlineKeyboardButton.WithCallbackData(_loc.AdminOverrideLabel, callback.overrideId));

                    var message = await _client.SendTextMessageAsync(dialog.ChatId, LocTokens.SubstituteTokens(
                            _loc.AdminVotingPrivateStarted,
                            Tuple.Create(LocTokens.Details, taskTemplate)), ParseMode.Html,
                        replyMarkup: new InlineKeyboardMarkup(inlineKeyboardButtons), cancellationToken: token);

                    if (null == message)
                    {
                        logger.Warn($"Couldnt send voting initiation to {admin.GetUsernameOrNameWithCircumflex()}, skipping admin in voting sequence");
                        return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
                    }

                    if (isOptedForRandomTask)
                        await _client.SendTextMessageAsync(dialog.ChatId, _loc.AdminVotingTaskFromRandomTaskRepository,parseMode:ParseMode.Html,
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

                            await _client.AnswerCallbackQueryAsync(query.Id, "got ya",cancellationToken:token);

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

                                logger.Info($"Admin {admin.GetUsernameOrNameWithCircumflex()} reson: `{reason}`");

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
            }
            catch (TaskCanceledException)
            {
                return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
            }
            catch (BadRequestException e)
            {
                logger.Error($"One of admin workers voting ({admin.GetUsernameOrNameWithCircumflex()}) resulted in BadRequestException: {e.Message}");
                return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
            }
            catch (Exception e)
            {
                logger.Error($"One of admin workers voting ({admin.GetUsernameOrNameWithCircumflex()}) resulted in exception",e);
                return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
            }

            logger.Warn($"Somehow fallen through all admin voting options : {admin.GetUsernameOrNameWithCircumflex()}");

            //Should not normally get here
            return Tuple.Create(admin, VotingResult.Skipped, String.Empty);
        }


    }
}
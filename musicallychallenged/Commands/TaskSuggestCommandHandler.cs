using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Telegram;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    public class TaskSuggestCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly IBotConfiguration _configuration;
        private readonly NextRoundTaskPollController _pollController;
        private readonly ITelegramClient _client;
        private readonly LocStrings _loc;

        private static readonly ILog logger = Log.Get(typeof(TaskSuggestCommandHandler));

        public string CommandName { get; } = Schema.TaskSuggestCommandName;
        public string UserFriendlyDescription => _loc.TaskSuggestCommandHandler_Description;

        int MinimumTaskSuggestionLength { get; set; } = 10;

        public TaskSuggestCommandHandler(
            IRepository repository, 
            IBotConfiguration configuration, 
            NextRoundTaskPollController pollController, 
            LocStrings loc, 
            ITelegramClient client)
        {
            _repository = repository;
            _configuration = configuration;
            _pollController = pollController;
            _loc = loc;
            _client = client;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} is about to suggest a task");

            // reject if not in TaskSuggestionCollection state
            if (state.State != ContestState.TaskSuggestionCollection)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.TaskSuggestCommandHandler_OnlyAvailableInSuggestionCollectionState);
                return;
            }

            // send the guidelines
            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                LocTokens.SubstituteTokens(_loc.TaskSuggestCommandHandler_SubmitGuidelines,
                Tuple.Create(LocTokens.VotingChannelLink, _configuration.VotingChannelInviteLink)));

            // get a suggestion message
            var response = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(_configuration.SubmissionTimeoutMinutes)).Token);

            if (!IsValidTaskSuggestion(response))
            {
                logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} failed task suggestion validation");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                    _loc.TaskSuggestCommandHandler_SubmitionFailed);

                return;
            }

            var text = ContestController.EscapeTgHtml(response.Text);
            await _pollController.SaveTaskSuggestion(text, user);

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, 
                _loc.TaskSuggestCommandHandler_SubmitionSucceeded);

            logger.Info("Task suggestion submitted");
        }

        bool IsValidTaskSuggestion(Message message)
        {
            var text = message.Text;

            if (string.IsNullOrWhiteSpace(text) || text.Length < MinimumTaskSuggestionLength)
                return false;

            return true;
        }
    }
}

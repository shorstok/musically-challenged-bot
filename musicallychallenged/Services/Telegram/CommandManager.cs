using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Administration;
using musicallychallenged.Commands;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services.Telegram
{
    public class CommandManager
    {
        private static readonly ILog logger = Log.Get(typeof(CommandManager));

        private readonly ITelegramQueryHandler[] _queryHandlers;
        private readonly DialogManager _dialogManager;
        private readonly IRepository _repository;
        private readonly ITelegramClient _client;
        private readonly LocStrings _loc;
        private readonly ITelegramCommandHandler[] _commandHandlers;

        public CommandManager(ITelegramQueryHandler[] queryHandlers,
            DialogManager dialogManager,
            IRepository repository,
            ITelegramClient client,
            LocStrings loc,
            ITelegramCommandHandler[] commandHandlers)
        {
            _queryHandlers = queryHandlers;
            _dialogManager = dialogManager;
            _repository = repository;
            _client = client;
            _loc = loc;
            _commandHandlers = commandHandlers;
        }

        public bool TryGetMatchingCommand(Message message, out ITelegramCommandHandler handler)
        {
            handler = null;

            if (message.Type != MessageType.Text)
                return false;

            var command = message.Text.Split(' ').First().TrimStart('/');

            var matchedCommand = _commandHandlers.FirstOrDefault(ch =>
                ch.CommandName?.Equals(command, StringComparison.InvariantCultureIgnoreCase) ?? false);

            if (matchedCommand == null)
                return false;
            
            handler = matchedCommand;

            return true;
        }

        private static bool IsAllowedToExecuteCommand(ITelegramCommandHandler commandHandler,
            User user)
        {
            if (commandHandler == null) throw new ArgumentNullException(nameof(commandHandler));

            var requiredCredentials = commandHandler.GetType().GetCustomAttributes(true)
                .OfType<DemandCredentialsAttribute>()
                .ToArray();

            foreach (var attribute in requiredCredentials)
            {
                foreach (var required in attribute.Credentials)
                {
                    if (user.Credentials.HasFlag(required))
                        continue;
                    
                    return false;
                }                
            }

            return true;
        }


        public async Task RunCommandChatAsync(ITelegramCommandHandler matchedCommand, Message message, User user)
        {
            var chatId = message.Chat.Id;

            using (var dialog = _dialogManager.StartNewDialogExclusive(chatId,message.From.Id))
            {
                try
                {
                    //check is banned

                    if (await MaybeHandleBannedCase(user, chatId))
                    {
                        logger.Info($"User {user.Id} / {user.Username ?? user.Name} is banned");
                        return;
                    }

                    //check credentials

                    if (!IsAllowedToExecuteCommand(matchedCommand, user))
                    {
                        logger.Warn($"User {message.From.Id} / {message.From.Username}: " +
                                    $"credentials check failed for command {matchedCommand.CommandName}");

                        await _client.SendTextMessageAsync(chatId, _loc.MissingCredentials);

                        return;
                    }

                    _repository.UpdateUser(user, chatId);

                    //all ok - run command                    

                    await matchedCommand.ProcessCommandAsync(dialog, user);
                }
                catch (TimeoutException)
                {
                    logger.Error($"{matchedCommand.CommandName} execution resulted in timeout");
                    var _ = _client.SendTextMessageAsync(chatId, _loc.ClientTookTooLongToRespond);                    
                }
                catch (TaskCanceledException)
                {
                    logger.Error($"{matchedCommand.CommandName} execution resulted in TaskCanceledException");
                    var _ = _client.SendTextMessageAsync(chatId, _loc.ClientTookTooLongToRespond);
                }
                catch (InvalidOperationException e)
                {
                    //usually TPL-generated
                    logger.Error(
                        $"{matchedCommand.CommandName} execution resulted in InvalidOperationException {e.Message}");
                }
                catch (ApiRequestException e)
                {
                    logger.Error($"{matchedCommand.CommandName} execution resulted in Tg API exception {e.ErrorCode}:{e.Message}");
                    var _ = _client.SendTextMessageAsync(chatId, _loc.SomethingWentWrongGeneric);
                }
                catch (Exception e)
                {
                    logger.Error($"{matchedCommand.CommandName} execution resulted in exception", e);
                    var _ = _client.SendTextMessageAsync(chatId, _loc.SomethingWentWrongGeneric);
                }
                finally
                {
                    _dialogManager.RecycleDialog(dialog);
                }
            }
        }

        private async Task<bool> MaybeHandleBannedCase(User user, long chatId)
        {
            if (user.State != UserState.Banned) 
                return false;

            await _client.SendTextMessageAsync(chatId,_loc.YouAreBanned);

            return true;
        }

        public ITelegramQueryHandler GetQueryHandler(string queryData)
        {
            return _queryHandlers.FirstOrDefault(qh => queryData?.StartsWith(qh.Prefix + ":") ?? false);
        }

        public static string ConstructQueryData(ITelegramQueryHandler handler, string payload) =>
            $"{handler.Prefix}:{payload}";

        public static string ExtractQueryData(ITelegramQueryHandler handler, CallbackQuery query) =>
            query.Data.Replace(handler.Prefix + ":", String.Empty);

        public async Task DescribeUsageAndAvailableCommands(User user, Message message)
        {
            var builder = new StringBuilder();

            foreach (var telegramCommandHandler in _commandHandlers)
            {
                if(!IsAllowedToExecuteCommand(telegramCommandHandler,user))
                    continue;

                builder.AppendLine(
                    $"/{telegramCommandHandler.CommandName} - <code>{telegramCommandHandler.UserFriendlyDescription}</code>");
            }

            await _client.SendTextMessageAsync(message.Chat.Id, LocTokens.SubstituteTokens(
                _loc.UnknownCommandUsageTemplate,
                Tuple.Create(LocTokens.Details,builder.ToString())),
                ParseMode.Html);
        }
    }
}
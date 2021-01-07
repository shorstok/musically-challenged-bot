using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Commands;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace musicallychallenged.Services.Telegram
{
    public class ServiceHost : IDisposable
    {
        private static readonly ILog logger = Log.Get(typeof(ServiceHost));

        private readonly Lazy<ITelegramClient> _telegramClientProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly DialogManager _dialogManager;
        private readonly IBotConfiguration _configuration;
        private readonly IRepository _repository;
        private readonly CommandManager _commandManager;
        
        public ITelegramClient Client { get; private set; }
        
        public ServiceHost(Lazy<ITelegramClient> telegramClientProvider,                               
            IEventAggregator eventAggregator,
            DialogManager dialogManager,
            IBotConfiguration configuration,
            IRepository repository,
            CommandManager commandManager)
        {
            _telegramClientProvider = telegramClientProvider;
            _eventAggregator = eventAggregator;
            _dialogManager = dialogManager;
            _configuration = configuration;
            _repository = repository;
            _commandManager = commandManager;
            
            configuration.Save();
        }

        public async void Start()
        {
            logger.Info($"Starting telegram bot");

            try
            {
                Client = _telegramClientProvider.Value;
                
                Client.OnMessage += BotOnMessageReceived;
                Client.OnMessageEdited += BotOnMessageReceived;
                Client.OnCallbackQuery += BotOnCallbackQueryReceived;
                Client.OnInlineResultChosen += BotOnChosenInlineResultReceived;
                Client.OnReceiveError += BotOnReceiveError;
                Client.OnUpdate += BotOnUpdate;

                await Client.ConnectAsync();
                
                logger.Info($"Bot started");
            }
            catch (CryptographicException e)
            {
                logger.Fatal($"Invalid telegram bot key (or from different user)",e);
                throw;
            }
            catch (Exception e)
            {
                logger.Error($"Telegram bot initialization error",e);
                logger.Fatal("Terminating service");

                System.Environment.Exit(1);
            }

            
        }

        private void BotOnUpdate(object sender, UpdateEventArgs e)
        {
            var message = e.Update.Message;

            if (e.Update.Type == UpdateType.ChannelPost && e.Update.ChannelPost!=null)
            {
                _repository.AddOrUpdateActiveChat(e.Update.ChannelPost.Chat.Id, e.Update.ChannelPost.Chat.Title);
                return;
            }
            
            if(null == message)
                return;

            if (message.MigrateFromChatId != 0 || message.MigrateToChatId != 0 || 
                message.Type == MessageType.MigratedToSupergroup||
                message.Type == MessageType.MigratedFromGroup)
            {
                HandleChatMigrationEvent(message);
                return;
            }
            
            if (message.Type == MessageType.ChatMembersAdded && message.NewChatMembers?.Any() == true)
            {
                foreach (var member in message.NewChatMembers)
                {
                    //somebody added us to new chat
                    if (member.IsBot && member.Id == _configuration.TelegramBotId)
                    {
                        if (message.Chat == null)
                        {
                            logger.Error($"Bot was added to chat (?), malformed message: Chat = null");
                            return;
                        }

                        logger.Info($"Bot was added to chat: {message.Chat.Id} {message.Chat.Title}");
                        _repository.AddOrUpdateActiveChat(message.Chat.Id, message.Chat.Title);
                        return;
                    }
                }
            }

            if (message.Type == MessageType.ChatMemberLeft && message.LeftChatMember!=null)
            {
                //somebody removed us from chat
                if (message.LeftChatMember.IsBot && message.LeftChatMember.Id == _configuration.TelegramBotId)
                {
                    if (message.Chat == null)
                    {
                        logger.Error($"Bot was removed from chat (?), malformed message: Chat = null");
                        return;
                    }

                    logger.Info($"Bot was removed from chat: {message.Chat.Id} {message.Chat.Title}");
                    _repository.RemoveActiveChat(message.Chat.Id);
                    return;
                }
            }
            
        }

        private void HandleChatMigrationEvent(Message message)
        {
            if (message.MigrateFromChatId == 0 && message.Chat?.Id == null)
            {
                logger.Error($"Chat migration failed (can't guess source chat id), manual migration required");
                return;
            }

            if (message.MigrateToChatId == 0)
            {
                logger.Error($"Chat migration failed (can't guess target chat id), manual migration required");
                return;
            }

            var fromId = message.MigrateFromChatId != 0 ? message.MigrateFromChatId : message.Chat.Id;
            var toId = message.MigrateToChatId;

            if (!_repository.MigrateChat(fromId, toId))
            {
                logger.Error($"Chat migration failed, manual migration required");
                _eventAggregator.Publish(new ChatMigrationFailedEvent(message));
                return;
            }

            _configuration.Reload();

            foreach (var deployment in _configuration.Deployments)
            {
                if (deployment.MainChatId == fromId)
                {
                    deployment.MainChatId = toId;
                    logger.Info($"Updated deployment {deployment.Name} MainChatId");
                }

                if (deployment.VotingChatId == fromId)
                {
                    deployment.VotingChatId = toId;
                    logger.Info($"Updated deployment {deployment.Name} VotingChatId");
                }
            }

            _configuration.Save(); 
            
            logger.Info($"Chat migration complete");
        }


        /// <summary>
        /// Handle incoming message. 
        /// 1. If it is command, stop existing dialogue for <paramref name="messageEventArgs.Message.From"/> and fire new dialogue with command handler
        /// 2. If it is not command, forward it to existing dialogue
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="messageEventArgs"></param>
        private async void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;

            if (message == null) 
                return;

            if(message.Chat.Type != ChatType.Private)
                return;

            var user = _repository.CreateOrGetUserByTgIdentity(message.From);

            if (null == user)
            {
                logger.Error($"Something went wrong: couldnt get new or existing user from repository");
                return;
            }

            if (!_commandManager.TryGetMatchingCommand(message, out var commandHandler))
            {
                var dialog = _dialogManager.GetActiveDialogForUserId(message.From.Id);

                if (null == dialog)
                {
                    await _commandManager.DescribeUsageAndAvailableCommands(user,message);
                    return;
                }

                //forward message to active dialog
                await dialog.NotifyMessageArrived(message);
                return;
            }

            //message is command, so start new dialog
            await _commandManager.RunCommandChatAsync(commandHandler, message, user);
        }
        
        private async void BotOnCallbackQueryReceived(object sender, CallbackQueryEventArgs callbackQueryEventArgs)
        {
            var callbackQuery = callbackQueryEventArgs.CallbackQuery;
            
            //maybe query source message is linked to some active dialog

            Dialog dialog = null;

            if (callbackQuery.Message != null &&
                (dialog = _dialogManager.GetActiveDialogByChatId(callbackQuery.Message.Chat.Id, callbackQuery.From)) != null)
            {
                await dialog.NotifyCallbackQueryReceived(callbackQuery);
                return;
            }

            //then try to find root-level query handler for callback

            var handler = _commandManager.GetQueryHandler(callbackQuery.Data);

            try
            {
                if (null != handler)
                {
                    await handler.ExecuteQuery(callbackQuery);
                    return;
                }
            }
            catch (MessageIsNotModifiedException)
            {
                //FU
                return;
            }

            //no? that's suspicious

            logger.Warn($"Got callback query no one wants to know about: {callbackQuery.Data}");
        }
        
        private static void BotOnChosenInlineResultReceived(object sender, ChosenInlineResultEventArgs chosenInlineResultEventArgs)
        {
            logger.Info($"Received inline result: " +
                        $"{chosenInlineResultEventArgs.ChosenInlineResult.ResultId}");
        }

        private static void BotOnReceiveError(object sender, ReceiveErrorEventArgs receiveErrorEventArgs)
        {
            logger.Warn($"Received error: {receiveErrorEventArgs.ApiRequestException.ErrorCode} " +
                        $"— {receiveErrorEventArgs.ApiRequestException.Message}");
        }

        public void Dispose()
        {
            Stop();            
        }


        public void Stop()
        {
            if(null == Client)
                return;
            
            logger.Info($"Stopping telegram bot");
            Client?.StopReceiving();
            Client = null;
        }

        
    }
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Dapper.Contrib.Extensions;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using tests.DI;
using tests.Mockups.Messaging;
using User = Telegram.Bot.Types.User;

namespace tests.Mockups
{
    public class UserScenarioContext : IDisposable
    {
        private TimeSpan DefaultReadTimeout => Debugger.IsAttached ? TimeSpan.FromHours(1) : TimeSpan.FromSeconds(1);

        private readonly BufferBlock<MockMessage> _messagesToUser;
        private readonly MockTelegramClient _mockTelegramClient;

        private readonly TaskCompletionSource<object> _taskCompletion = new TaskCompletionSource<object>();
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public Task<object> ScenarioTask => _taskCompletion.Task;

        public User MockUser { get; }

        public Chat PrivateChat { get; }

        public delegate UserScenarioContext Factory();

        public UserScenarioContext(MockTelegramClient mockTelegramClient)
        {
            var mockUserId = MockConfiguration.GetNewMockUserId();

            MockUser = new User
            {
                Id = mockUserId,
                FirstName = Guid.NewGuid().ToString().Substring(0, 4),
                Username = $"User_{mockUserId:00}"
            };

            PrivateChat = new Chat
            {
                Id = MockConfiguration.CreateNewPrivateChatId(),
            };

            _mockTelegramClient = mockTelegramClient;

            _messagesToUser = new BufferBlock<MockMessage>(new DataflowBlockOptions
            {
                CancellationToken = _cancellationTokenSource.Token
            });
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            _cancellationTokenSource = null;
            _taskCompletion?.TrySetCanceled();
            _messagesToUser?.Complete();
        }

        public void SendCommand(string command)
        {
            SendMessage("/"+command,PrivateChat);
        }

        public void SendMessage(string text, Chat destinationChat)
        {
            _mockTelegramClient.InvokeOnMessage(TestCompartment.CreateMockUpdateEvent(new Update
            {
                Message = new Message
                {
                    Chat = destinationChat,
                    MessageId = MockConfiguration.CreateNewMockMessageId(),
                    Text = text,
                    From = MockUser
                }
            }));
        }


        public void SendAudioFile(Audio audio, Chat chat)
        {
            _mockTelegramClient.InvokeOnMessage(TestCompartment.CreateMockUpdateEvent(new Update
            {
                Message = new Message
                {
                    Chat = chat,
                    Audio = audio,
                    MessageId = MockConfiguration.CreateNewMockMessageId(),
                    From = MockUser
                }
            }));
        }

        public void SendQuery(string queryData, Message sourceMessage)
        {
            _mockTelegramClient.InvokeOnCallbackQuery(TestCompartment.CreateMockUpdateEvent(new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    From = MockUser,
                    Id = Guid.NewGuid().ToString(),
                    Data = queryData,
                    Message = sourceMessage,
                }
            }));

        }

        internal async Task<Message> ReadTillMessageReceived(Expression<Predicate<MessageSentMock>> filter,  TimeSpan? readTimeOut = null)
        {
            var messageSent = await ReadMockMessage(filter, 
                readTimeOut ?? DefaultReadTimeout);

            return new Message
            {
                Text = messageSent.Text,
                MessageId = messageSent.Id,
                ReplyToMessage = null, //todo: get by id from mock tg
                ReplyMarkup = messageSent.ReplyMarkup as InlineKeyboardMarkup,
                Chat = new Chat {Id = messageSent.ChatId.Identifier},
                From = MockConfiguration.MockBotUser
            };
        }


        public async Task<Message> ReadTillMessageReceived(long? channelFilter = null, TimeSpan? readTimeOut = null)
        {
            return await ReadTillMessageReceived(mock =>
                channelFilter == null || mock.ChatId.Identifier == channelFilter, readTimeOut);
        }

        public async Task<Message> ReadTillMessageEdited(long? channelFilter = null,  TimeSpan? readTimeOut = null)
        {

            var messageEdited = await ReadMockMessage<MessageEditedMock>(mock =>
                    channelFilter == null || mock.ChatId.Identifier == channelFilter, 
                readTimeOut ?? DefaultReadTimeout);

            return new Message
            {
                Text = messageEdited.Text,
                MessageId = messageEdited.MessageId,
                ReplyToMessage = null, //todo: get by id from mock tg
                ReplyMarkup = messageEdited.ReplyMarkup as InlineKeyboardMarkup,
                Chat = new Chat {Id = messageEdited.ChatId.Identifier},
                From = MockConfiguration.MockBotUser
            };
        }


        internal async Task<Message> ReadTillMessageForwardedEvent(Expression<Predicate<MessageForwardedMock>> filter, TimeSpan? readTimeOut = null)
        {
            var messageSent = await ReadMockMessage<MessageForwardedMock>(filter,
                readTimeOut ?? DefaultReadTimeout);

            return _mockTelegramClient.GetMockMessageById(messageSent.ChatId.Identifier,messageSent.MessageId);
        }

        private async Task<TMockMessage> ReadMockMessage<TMockMessage>(Expression<Predicate<TMockMessage>> filter, TimeSpan? readTimeOut)
            where TMockMessage : MockMessage
        {
            var predicate = filter==null ? (m)=>true : filter.Compile();

            var eatenMessages = new List<MockMessage>();

            do
            {
                var mockMessage = await _messagesToUser.ReceiveAsync(
                    readTimeOut ?? DefaultReadTimeout,
                    _cancellationTokenSource.Token);

                if (!(mockMessage is TMockMessage message))
                {
                    eatenMessages.Add(mockMessage);
                    continue;
                }

                if (!predicate(message))
                {
                    eatenMessages.Add(message);
                    continue;
                }

                foreach (var eatenMessage in eatenMessages) 
                    await _messagesToUser.SendAsync(eatenMessage);

                return message;
            } while (true);
        }

        internal void SetException(Exception exception)
        {
            _taskCompletion.SetException(exception);
        }

        internal void SetCompleted()
        {
            _taskCompletion.SetResult(null);
        }

        internal async Task AddMessageToUserQueue(MockMessage message, CancellationToken token)
        {
            await _messagesToUser.SendAsync(message, token);
        }


    }
}
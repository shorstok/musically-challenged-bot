using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper.Contrib.Extensions;
using log4net;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using Telegram.Bot.Types;
using tests.Mockups;
using tests.Mockups.Messaging;
using User = Telegram.Bot.Types.User;

namespace tests.DI
{
    public class UserScenarioController : IDisposable
    {
        private readonly UserScenarioContext.Factory _userScenarioFactory;
        private readonly IRepository _repository;

        private static readonly ILog Logger = Log.Get(typeof(UserScenarioController));

        private readonly ConcurrentDictionary<long, UserScenarioContext> _contexts =
            new ConcurrentDictionary<long, UserScenarioContext>();

        public UserScenarioController(UserScenarioContext.Factory userScenarioFactory, IRepository repository)
        {
            _userScenarioFactory = userScenarioFactory;
            _repository = repository;
        }


        public void Dispose()
        {
            foreach (var userScenarioContext in _contexts.Values)
                userScenarioContext.Dispose();
        }

        private void SetUserCredentials(UserCredentials credentials, User mockUser)
        {
            _repository.CreateOrGetUserByTgIdentity(mockUser);

            //This should be inaccessible from outside, but currently we have no way
            //to set user credentials (they are set externally, in DB)
            //so we have to use reflection to get connection to DB and execute
            //sql command to set it manually

            var createOpenConnectionMethodInfo = _repository.GetType().GetMethod("CreateOpenConnection",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (null == createOpenConnectionMethodInfo)
                throw new Exception(
                    $"Could not find CreateOpenConnection method in {_repository?.GetType()}, maybe it was refactored?");

            using (var connection = createOpenConnectionMethodInfo.Invoke(_repository, null) as DbConnection)
            {
                using (var tx = connection.BeginTransaction())
                {
                    var existing = connection.Get<musicallychallenged.Domain.User>(mockUser.Id);

                    if (existing == null)
                        throw new Exception($"User with id {mockUser.Id} not found in database");

                    existing.Credentials = credentials;

                    connection.Update(existing);

                    tx.Commit();
                }
            }
        }

        public UserScenarioContext StartUserScenario(Func<UserScenarioContext, Task> scenario,
            UserCredentials credentials = UserCredentials.User)
        {
            var context = _userScenarioFactory();

            SetUserCredentials(credentials, context.MockUser);

            _contexts[context.PrivateChat.Id] = context;

            Task.Run(async () =>
            {
                try
                {
                    Logger.Info($">Starting {context.MockUser.Id}/{context.MockUser.Username}({credentials}) user scenario");
                    await scenario(context).ConfigureAwait(false);

                    Logger.Info(
                        $">{context.MockUser.Id}/{context.MockUser.Username}({credentials}) user scenario finished OK");
                    context.SetCompleted();
                }
                catch (Exception e)
                {
                    Logger.Error($"User {context.MockUser.Id} / {context.MockUser.Username} scenario resulted in exception");
                    //Marshal exception from thread pool to awaiting thread (should be test runner)
                    context.SetException(e);
                }
            });

            return context;
        }

        internal async Task SendMessageToMockUsers(ChatId chatId, MockMessage message, CancellationToken token)
        {
            if (chatId.Identifier == MockConfiguration.MainChat.Id ||
                chatId.Identifier == MockConfiguration.VotingChat.Id)
            {
                //Should be sent to all mock-users as a fake broadcast

                foreach (var userScenarioContext in _contexts.Values)
                    await userScenarioContext.AddMessageToUserQueue(message, token);

                return;
            }

            //Send to private user chat

            if (!_contexts.TryGetValue(chatId.Identifier, out var context))
                throw new Exception($"MockUser with chat id {chatId} not registered with this MockTelegramClient");

            await context.AddMessageToUserQueue(message, token);
        }

        internal async Task SendMessageToMockUser(int userId, MockMessage message, CancellationToken token)
        {
            var user = _contexts.Values.FirstOrDefault(u => u.MockUser.Id == userId);
            
            //Send to private user chat

            if (user == null)
                throw new Exception($"MockUser with id {userId} not registered with this MockTelegramClient");

            await user.AddMessageToUserQueue(message, token);
        }
    }
}
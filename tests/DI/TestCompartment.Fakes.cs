using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dapper;
using Dapper.Contrib.Extensions;
using musicallychallenged.Domain;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using tests.Mockups;
using User = Telegram.Bot.Types.User;

namespace tests.DI
{
    public partial class TestCompartment : IDisposable
    {
        public async Task AddFakeTaskSuggestions(Func<User, string> suggestionGenerator)
        {
            await using var connection = GetRepositoryDbConnection(Repository);

            var users = connection.Query<User>("SELECT * FROM User").ToArray();

            if (users.Length < 1)
                throw new Exception("Can't add fake suggestions - no users in the database!");

            foreach (var user in users)
            {
                var suggestion = suggestionGenerator(user);
                
                if(null == suggestion)
                    continue;
                
                //Create placeholder message

                var msg = await TelegramClient.SendTextMessageAsync(MockConfiguration.VotingChat, "placeholder");

                //And respective suggest records
                
                Repository.CreateOrUpdateTaskSuggestion(Repository.GetExistingUserWithTgId(user.Id),
                    suggestion, msg.Chat.Id,msg.MessageId,out var _);
            }

        }

        public void AddFakeVoteForEntries(Func<ActiveContestEntry, int> voteGenerator)
        {
            using var connection = GetRepositoryDbConnection(Repository);

            var voters = connection.Query<User>("SELECT * FROM User").ToArray();

            if (voters.Length < 2)
                throw new Exception("Can't add fake votes - too few users in the database!");

            using (var tx = connection.BeginTransaction())
            {
                var entries = connection.Query<ActiveContestEntry>(
                    "SELECT * FROM ActiveContestEntry WHERE ConsolidatedVoteCount is null").ToArray();

                if (entries.Length == 0)
                    throw new Exception("Can't add fake votes - no entries in DB!");

                foreach (var entry in entries)
                {
                    for (int nvote = 0; nvote < voteGenerator(entry); nvote++)
                    {
                        connection.Insert(new Vote
                        {
                            UserId = voters[nvote % voters.Length].Id,
                            Value = 1,
                            ContestEntryId = entry.Id,
                            Timestamp = Clock.GetCurrentInstant()
                        });
                    }
                }

                tx.Commit();
            }
        }

        internal static UpdateEventArgs CreateMockUpdateEvent(Update source) => new(source);

        public void AdvanceClockToDeadline()
        {
            var state = Repository.GetOrCreateCurrentState();

            var delta = state.NextDeadlineUTC - Clock.GetCurrentInstant();

            Clock.Offset = Clock.Offset.Plus(delta);
        }
    }
}
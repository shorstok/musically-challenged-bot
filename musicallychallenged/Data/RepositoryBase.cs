using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using musicallychallenged.Domain;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dapper;
using Dapper.Contrib;
using Dapper.Contrib.Extensions;
using log4net;
using musicallychallenged.Logging;
using musicallychallenged.Services.Events;
using NodaTime;

namespace musicallychallenged.Data
{
    public abstract class RepositoryBase : IRepository
    {
        private static readonly ILog logger = Log.Get(typeof(RepositoryBase));

        private readonly IClock _clock;


        protected abstract DbConnection CreateOpenConnection();


        protected RepositoryBase(IClock clock)
        {
            _clock = clock;
            SqlMapper.AddTypeHandler(new InstantHandler());
        }


        public bool MigrateChat(long fromId, long toId)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    var state = GetOrCreateSystemStateInternal(connection, tx);

                    if (state.VotingChannelId == fromId)
                    {
                        logger.Info($"Replaced VotingChannelId in SystemState");
                        state.VotingChannelId = toId;
                    }
                    if (state.MainChannelId == fromId)
                    {
                        logger.Info($"Replaced MainChannelId in SystemState");
                        state.MainChannelId = toId;
                    }

                    connection.Update<SystemState>(state, transaction: tx);

                    //Update contest entries migration

                    var qty = connection.Execute(@"UPDATE ActiveContestEntry set ContainerChatId=@MigrateToId WHERE ContainerChatId=@FromId",
                        new
                        {
                            MigrateToId = toId,
                            FromId = fromId
                        });

                    logger.Info($"{qty} contest entries updated");

                    tx.Commit();
                }
            }

            return true;
        }

        public RandomTask[] GetLeastUsedRandomTasks()
        {
            using (var connection = CreateOpenConnection())
            {
                //Selects all least used random tasks

                var query = @"select * from RandomTask r
                inner join (select min(UsedCount - Priority) mincount from RandomTask) m
                on r.UsedCount-Priority = m.mincount";

                return connection.Query<RandomTask>(query).ToArray();
            }
        }

        public void UpdateRandomTask(RandomTask task)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    connection.Update(task, transaction: tx);
                    tx.Commit();
                }
            }
        }

        public double? GetAverageVoteForUser(User user)
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.Query<double?>(
                    @"select avg(v.Value) from Vote v where v.UserId = @Id",
                    new { Id = user.Id }).FirstOrDefault();
            }
        }

        public int GetVoteCountForActiveEntriesForUser(User user)
        {
            using (var connection = CreateOpenConnection())
            {
                var qry = @"select count(v.Value) from Vote v
                inner join ActiveContestEntry ace on ace.Id = v.ContestEntryId
                where v.UserId = @Id and ace.ConsolidatedVoteCount is null";

                return connection.Query<int?>(qry, new { Id = user.Id }).FirstOrDefault() ?? 0;
            }
        }

        public bool MaybeCreateVoteForAllActiveEntriesExcept(User user, int entryId, int defaultVoteValue)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    var qry = @"select count(v.Value) from Vote v
                    inner join ActiveContestEntry ace on ace.Id = v.ContestEntryId
                    where v.UserId = @Id and ace.ConsolidatedVoteCount is null";

                    var count = connection.Query<int?>(qry, new { Id = user.Id }, transaction: tx).
                                    FirstOrDefault() ?? 0;

                    //Votes present
                    if (0 != count)
                        return false;

                    var multiInsertQry = @"insert into Vote(UserId, ContestEntryId,Value,Timestamp)
                    select @UserID,e.Id,@VoteVal, @Timestamp from ActiveContestEntry e
                    where e.ConsolidatedVoteCount is NULL and e.Id != @EntryId";

                    connection.Execute(multiInsertQry,
                        new
                        {
                            UserID = user.Id,
                            VoteVal = defaultVoteValue,
                            Timestamp = _clock.GetCurrentInstant(),
                            EntryId = entryId,
                        }, transaction: tx);

                    tx.Commit();

                    return true;
                }
            }
        }

        public int CloseAllPostponeRequests(PostponeRequestState finalState)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    //Set finalState for all requests with Open state

                    var quantity = connection.Execute(
                        @"UPDATE PostponeRequest set State=@FinalState WHERE State=@FromState",
                        new
                        {
                            FinalState = finalState,
                            FromState = PostponeRequestState.Open
                        }, 
                        transaction:tx);

                    logger.Info($"{quantity} postpone requests set to {finalState}");

                    tx.Commit();

                    return quantity;
                }
            }
        }

        public PostponeRequest[] GetOpenPostponeRequestsForUser(int authorId)
        {
            using (var connection = CreateOpenConnection())
            {
                //Get open requests for authorId

                var query = @"select * from PostponeRequest where State = @state and UserId == @userId";

                return connection.Query<PostponeRequest>(query, new
                {
                    state = PostponeRequestState.Open,
                    userId = authorId
                }).ToArray();
            }
        }

        public PostponeRequest[] CreatePostponeRequestRetrunOpen(User author, Duration postponeDuration)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    var multiInsertQry = @"insert into 
                    PostponeRequest(UserId, ChallengeRoundNumber,AmountMinutes, State,Timestamp)
                    select @UserID,s.CurrentChallengeRoundNumber,@AmountMinutes, @State, @Timestamp from SystemState s";

                    connection.Execute(multiInsertQry,
                        new
                        {
                            UserID = author.Id,
                            AmountMinutes = (long)postponeDuration.TotalMinutes,
                            State = PostponeRequestState.Open,
                            Timestamp = _clock.GetCurrentInstant(),
                        }, transaction: tx);

                    var openRequests = connection.Query<PostponeRequest>(
                        "select * from PostponeRequest where State = @State",
                        new
                        {
                            State = PostponeRequestState.Open,
                        },
                        transaction: tx).ToArray();

                    tx.Commit();

                    return openRequests;
                }

            }
        }

        public void FinalizePostponeRequests(PostponeRequest keyRequest)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    //Set 'ClosedSatisfied' on keyRequest and 'ClosedDiscarded' on other open requests

                    connection.Execute(
                        @"UPDATE PostponeRequest set State =
                            case 
                                when Id=@keyId then @finalSatisfiedState else @finalDiscardedState
                            end
                            WHERE State = @openState",
                        new
                        {
                            finalSatisfiedState = PostponeRequestState.ClosedSatisfied,
                            finalDiscardedState = PostponeRequestState.ClosedDiscarded,
                            openState = PostponeRequestState.Open,
                            keyId = keyRequest.Id,
                        }, transaction: tx);


                    tx.Commit();
                }

            }
        }

        public long GetUsedPostponeQuotaForCurrentRoundMinutes()
        {
            using (var connection = CreateOpenConnection())
            {
                //Sum all satisfied postpone requests for current round

                var query = @"
                    select SUM(r.AmountMinutes) from PostponeRequest r
                    inner join SystemState s on s.CurrentChallengeRoundNumber = r.ChallengeRoundNumber
                    where r.State = @selectedState";

                return connection.Query<long?>(query, new
                {
                    selectedState = PostponeRequestState.ClosedSatisfied,
                }).FirstOrDefault()??0;
            }
        }



        public NextRoundTaskPoll GetOpenNextRoundTaskPoll()
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.Query<NextRoundTaskPoll>(
                    @"select * from NextRoundTaskPoll where State=@State",
                    new { State = NextRoundTaskPollState.Open })
                    .FirstOrDefault();
            }
        }

        public void CreateNextRoundTaskPoll()
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    logger.Info("Creating a NextRoundTaskPoll");

                    // checking whether a poll is already open
                    if (GetOpenNextRoundTaskPoll() != null)
                    {
                        logger.Error($"Tried creating a new NextRoundTaskPoll, when one is already open");
                        return;
                    }

                    // inserting a new poll
                    var newPoll = new NextRoundTaskPoll
                    {
                        State = NextRoundTaskPollState.Open,
                        Timestamp = _clock.GetCurrentInstant()
                    };

                    connection.Insert(newPoll, tx);

                    logger.Info("NextRoundTaskPoll created");

                    tx.Commit();
                }
            }
        }

        class TaskSuggestionConsolidated
        {
            public int Id { get; set; }
            public int Sum { get; set; }
        }

        public IEnumerable<TaskSuggestion> CloseNextRoundTaskPollAndConsolidateVotes()
        {
            var result = new List<TaskSuggestion>();

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    logger.Info("Closing NextRoundTaskPoll");

                    var existing = GetOpenNextRoundTaskPoll();

                    if (existing == null)
                    {
                        logger.Warn("No open NextRoundPoll found");
                        return result;
                    }

                    // Consolidating vote count for suggestions
                    var query = @"SELECT s.Id, COALESCE(SUM(v.sum_amount),0) AS Sum
                    FROM TaskSuggestion s
                    Left JOIN (
                        SELECT TaskSuggestionId, SUM(Value) AS sum_amount
                        FROM TaskPollVote
                        GROUP BY TaskSuggestionId
                    ) v on s.Id = v.TaskSuggestionId
                    WHERE s.ConsolidatedVoteCount is NULL
                    GROUP by s.Id";

                    var consVotes = connection.Query<TaskSuggestionConsolidated>(query, transaction: tx);

                    foreach (var consTask in consVotes)
                    {
                        var suggestion = connection.Get<TaskSuggestion>(consTask.Id, transaction: tx);
                        suggestion.ConsolidatedVoteCount = consTask.Sum;

                        result.Add(suggestion);

                        connection.Update<TaskSuggestion>(suggestion, transaction: tx);
                    }

                    logger.Info("Consolidated vote count for TaskSuggestions");

                    existing.State = NextRoundTaskPollState.Closed;
                    connection.Update<NextRoundTaskPoll>(existing, transaction: tx);

                    logger.Info("NextRoundTaskPoll closed");

                    tx.Commit();
                }
            }

            return result;
        }

        public void SetNextRoundTaskPollWinner(int? winnerId)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    var lastPoll = connection.Query<NextRoundTaskPoll>(
                        "select * from NextRoundTaskPoll", transaction: tx)
                        .OrderBy(p => p.Timestamp)
                        .LastOrDefault();

                    if (lastPoll == null)
                    {
                        logger.Warn("No last NextRoundTaskPoll found");
                        return;
                    }

                    lastPoll.WinnerId = winnerId;
                    connection.Update<NextRoundTaskPoll>(lastPoll, tx);

                    tx.Commit();
                }
            }
        }

        public int? GetLastTaskPollWinnerId()
        {
            using (var connection = CreateOpenConnection())
            {
                var lastPoll = connection.Query<NextRoundTaskPoll>(
                    "select * from NextRoundTaskPoll")
                    .OrderBy(p => p.Timestamp)
                    .LastOrDefault();

                if (lastPoll == null)
                {
                    logger.Warn("No last NextRoundTaskPoll found");
                    return null;
                }

                return lastPoll.WinnerId;
            }
        }

        public void CreateOrUpdateTaskSuggestion(
            User author, 
            string descriptionUnescaped, 
            long containerChatId, 
            int containerMessageId,
            out TaskSuggestion previous)
        {
            previous = null;

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    // get an open NextRoundTaskPoll
                    var poll = GetOpenNextRoundTaskPoll();
                    if (poll == null)
                    {
                        logger.Error("Tried creating/updating a TaskSuggestion when no NextRoundTaskPoll is open");
                        return;
                    }

                    // get the existing suggestions if exists
                    bool create = false;
                    var query = @"select * from TaskSuggestion where ConsolidatedVoteCount IS NULL and AuthorUserId=@AuthorId";

                    var existing = connection.Query<TaskSuggestion>(query, new { AuthorId = author.Id }).FirstOrDefault();

                    if (existing == null)
                    {
                        create = true;
                        existing = new TaskSuggestion { AuthorUserId = author.Id };
                    }
                    else
                    {
                        previous = new TaskSuggestion
                        {
                            AuthorUserId = existing.AuthorUserId,
                            PollId = existing.PollId,
                            Timestamp = existing.Timestamp,
                            Description = existing.Description,
                            ConsolidatedVoteCount = existing.ConsolidatedVoteCount,
                            ContainerChatId = existing.ContainerChatId,
                            ContainerMesssageId = existing.ContainerMesssageId
                        };
                    }

                    existing.PollId = poll.Id;
                    existing.AuthorUserId = author.Id;
                    existing.Description = descriptionUnescaped;
                    existing.ContainerChatId = containerChatId;
                    existing.ContainerMesssageId = containerMessageId;
                    existing.Timestamp = _clock.GetCurrentInstant();

                    if (create)
                        connection.Insert(existing, transaction: tx);
                    else
                        connection.Update(existing, transaction: tx);

                    tx.Commit();
                }
            }
        }

        public IEnumerable<TaskSuggestion> GetActiveTaskSuggestions()
        {
            using (var connection = CreateOpenConnection())
            {
                var query = @"select * from TaskSuggestion where ConsolidatedVoteCount IS NULL";
                return connection.Query<TaskSuggestion>(query);
            }
        }

        public TaskSuggestion GetExistingTaskSuggestion(int suggestionId)
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.Get<TaskSuggestion>(suggestionId);
            }
        }

        public void DeleteTaskSuggestion(int suggestionId)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    connection.Delete(
                        connection.Get<TaskSuggestion>(suggestionId));

                    tx.Commit();
                }
            }
        }

        public IEnumerable<Tuple<TaskPollVote, User>> GetVotesForTaskSuggestion(int suggestionId)
        {
            using (var connection = CreateOpenConnection())
            {
                var query = @"SELECT * from TaskPollVote v
                    LEFT JOIN User u on v.UserId=u.Id
                    WHERE v.TaskSuggestionId=@SuggestionId";

                return connection.Query<TaskPollVote, User, Tuple<TaskPollVote, User>>(query,
                    Tuple.Create<TaskPollVote, User>,
                    new { SuggestionId = suggestionId },
                    splitOn: "Id");
            }
        }

        public void SetOrUpdateTaskPollVote(User voter, int suggestionId, 
            int value, out bool updated)
        {
            updated = false;

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    var existing = connection.Query<TaskPollVote>(
                        @"select * from TaskPollVote 
                          where UserId=@VoterId and TaskSuggestionId=@SuggestionId",
                        new 
                        { 
                            VoterId = voter.Id, 
                            SuggestionId = suggestionId 
                        }, transaction: tx).FirstOrDefault();

                    if (existing == null)
                    {
                        var vote = new TaskPollVote
                        {
                            UserId = voter.Id,
                            TaskSuggestionId = suggestionId,
                            Value = value
                        };

                        connection.Insert<TaskPollVote>(vote, transaction: tx);
                    }
                    else
                    {
                        updated = true;

                        if (existing.Value == value)
                            return;

                        existing.Value = value;
                        connection.Update<TaskPollVote>(existing, transaction: tx);
                    }

                    tx.Commit();
                }
            }
        }

        public bool MaybeCreateVoteForAllActiveSuggestionsExcept(User user, int suggestionId, int defaultVoteValue)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    var qry = @"select count(v.Value) from TaskPollVote v
                    inner join TaskSuggestion ts on ts.Id = v.TaskSuggestionId
                    where v.UserId = @Id and ts.ConsolidatedVoteCount is null";

                    var count = connection.Query<int?>(qry, new { Id = user.Id }, transaction: tx).
                                    FirstOrDefault() ?? 0;

                    //Votes present
                    if (0 != count)
                        return false;

                    var multiInsertQry = @"insert into TaskPollVote(UserId, TaskSuggestionId, Value, Timestamp)
                    select @UserID, ts.Id, @VoteVal, @Timestamp from TaskSuggestion ts
                    where ts.ConsolidatedVoteCount is NULL and ts.Id != @SuggestionId";

                    connection.Execute(multiInsertQry,
                        new
                        {
                            UserID = user.Id,
                            VoteVal = defaultVoteValue,
                            Timestamp = _clock.GetCurrentInstant(),
                            SuggestionId = suggestionId,
                        }, transaction: tx);

                    tx.Commit();

                    return true;
                }
            }
        }

        public User CreateOrGetUserByTgIdentity(Telegram.Bot.Types.User source)
        {
            User result;

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    result = connection.Get<User>(source.Id, tx);

                    if (null == result)
                    {
                        result = new User
                        {
                            Id = source.Id,
                            Credentials = UserCredentials.User,
                            LastActivityUTC = _clock.GetCurrentInstant(),
                            State = UserState.Default,
                            Username = source.Username,
                            Name = source.FirstName + " " + source.LastName
                        };

                        connection.Insert(result, tx);
                    }

                    tx.Commit();
                }
            }

            return result;
        }

        public User GetExistingUserWithTgId(int id)
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.Get<User>(id);
            }
        }

        public User[] GetAllActiveUsersWithCredentials(UserCredentials userCredentials)
        {
            using (var connection = CreateOpenConnection())
            {
                //Get entries that are not with yet unfinished voting

                var query = @"select * from User where State = @State and (Credentials & @Flag) == @Flag";

                return connection.Query<User>(query, new
                {
                    State = UserState.Default,
                    Flag = userCredentials
                }).ToArray();
            }
        }

        public void GetOrCreateContestEntry(User user,
            long chatId,
            int forwaredMessageId,
            int containerMessageId,
            int challengeRoundNumber,
            out ActiveContestEntry previousEntry)
        {
            previousEntry = null;

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    bool create = false;
                    var query = @"select * from ActiveContestEntry where AuthorUserId = @UserId and ConsolidatedVoteCount IS NULL";

                    var existing = connection.Query<ActiveContestEntry>(query, new { UserId = user.Id }, transaction: tx).FirstOrDefault();

                    if (existing == null)
                    {
                        existing = new ActiveContestEntry { AuthorUserId = user.Id };
                        create = true;
                    }
                    else
                    {
                        previousEntry = new ActiveContestEntry
                        {
                            ContainerChatId = existing.ContainerChatId,
                            ContainerMesssageId = existing.ContainerMesssageId,
                            ForwardedPayloadMessageId = existing.ForwardedPayloadMessageId,
                            AuthorUserId = existing.AuthorUserId,
                            Timestamp = existing.Timestamp,
                            ConsolidatedVoteCount = existing.ConsolidatedVoteCount,
                            ChallengeRoundNumber = existing.ChallengeRoundNumber
                        };
                    }

                    existing.ChallengeRoundNumber = challengeRoundNumber;
                    existing.ConsolidatedVoteCount = null;
                    existing.ContainerChatId = chatId;
                    existing.ContainerMesssageId = containerMessageId;
                    existing.ForwardedPayloadMessageId = forwaredMessageId;
                    existing.Timestamp = _clock.GetCurrentInstant();

                    if (create)
                        connection.Insert(existing, transaction: tx);
                    else
                        connection.Update(existing, transaction: tx);

                    tx.Commit();
                }
            }
        }



        public IEnumerable<ActiveContestEntry> GetActiveContestEntries()
        {
            using (var connection = CreateOpenConnection())
            {
                //Get entries that are not with yet unfinished voting

                var query = @"select * from ActiveContestEntry where ConsolidatedVoteCount IS NULL";

                return connection.Query<ActiveContestEntry>(query);
            }
        }

        public IEnumerable<Tuple<Vote, User>> GetVotesForEntry(int entryId)
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.Query<Vote, User, Tuple<Vote, User>>(@"SELECT *
                    FROM Vote v
                    LEFT JOIN User u ON u.Id= v.UserId
                    WHERE v.ContestEntryId = @EntryId",
                    Tuple.Create<Vote, User>,
                    new { EntryId = entryId }, splitOn: "Id");
            }

        }

        private class ContestEntryConsolidated
        {
            public int Id { get; set; }
            public int Sum { get; set; }
        }

        public IEnumerable<ActiveContestEntry> ConsolidateVotesForActiveEntriesGetAffected()
        {
            var result = new List<ActiveContestEntry>();

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    //Get sum of votes and Ids for active entries

                    var query = @"SELECT e.Id, COALESCE(SUM(v.sum_amount),0) AS Sum
                    FROM  ActiveContestEntry e
                    LEFT JOIN (
                       SELECT ContestEntryId, SUM(Value) AS sum_amount
                       FROM   Vote
                       GROUP  BY ContestEntryId
                       ) v ON v.ContestEntryId = e.id
                    WHERE e.ConsolidatedVoteCount IS NULL
                    GROUP BY e.id";

                    var voteSum = connection.Query<ContestEntryConsolidated>(query, transaction: tx).ToList();

                    //Save affected ActiveContestEntry and update their ConsolidatedVoteCount to sum from previous query

                    foreach (var entryConsolidated in voteSum)
                    {
                        var entry = connection.Get<ActiveContestEntry>(entryConsolidated.Id, transaction: tx);

                        entry.ConsolidatedVoteCount = entryConsolidated.Sum;
                        entry.Timestamp = _clock.GetCurrentInstant();

                        result.Add(entry);

                        connection.Update(entry, transaction: tx);
                    }


                    tx.Commit();
                }
            }

            return result;
        }

        public ActiveContestEntry GetExistingEntry(int entryId)
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.Get<ActiveContestEntry>(entryId);
            }
        }

        public ActiveContestEntry GetActiveContestEntryForUser(int userId)
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.QueryFirstOrDefault<ActiveContestEntry>(@"SELECT * from ActiveContestEntry where AuthorUserId=@UserId and ConsolidatedVoteCount IS NULL", new
                {
                    UserId = userId,
                });
            }
        }

        public int GetFinishedContestEntryCountForUser(int userId)
        {
            using (var connection = CreateOpenConnection())
            {
                return connection.QueryFirstOrDefault<int?>(
                    @"SELECT COUNT(id) from ActiveContestEntry where AuthorUserId=@UserId and ConsolidatedVoteCount IS NOT NULL",
                    new
                    {
                        UserId = userId,
                    }) ?? 0;
            }
        }

        public void UpdateContestEntry(ActiveContestEntry entry)
        {
            using (var connection = CreateOpenConnection())
            {
                connection.Update(entry);
            }
        }

        public void DeleteContestEntry(int deletedEntryId)
        {
            using (var connection = CreateOpenConnection())
            {
                connection.Delete(new ActiveContestEntry { Id = deletedEntryId });
            }
        }


        public void SetOrUpdateVote(User voter, int activeEntryId, int voteValue, out bool updated)
        {
            updated = false;

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    //Create or update vote record

                    var existing = connection.Query<Vote>(
                        @"select * from Vote where UserId=@UserId and ContestEntryId=@ContestEntryId ",
                        new
                        {
                            UserId = voter.Id,
                            ContestEntryId = activeEntryId
                        }, transaction: tx).FirstOrDefault();

                    if (null == existing)
                    {
                        existing = new Vote
                        {
                            ContestEntryId = activeEntryId,
                            Timestamp = _clock.GetCurrentInstant(),
                            UserId = voter.Id,
                            Value = voteValue
                        };

                        existing.Id = (int)connection.Insert(existing, tx);
                    }
                    else
                    {
                        if (existing.Value == voteValue)
                        {
                            //do nothing
                            updated = true;
                            return;
                        }

                        updated = true;
                        existing.Timestamp = _clock.GetCurrentInstant();
                        existing.Value = voteValue;

                        connection.Update<Vote>(existing, tx);
                    }

                    tx.Commit();
                }
            }
        }


        private SystemState GetOrCreateSystemStateInternal(DbConnection connection, DbTransaction tx)
        {
            var existing = connection.Get<SystemState>(1);

            if (null == existing)
            {
                existing = new SystemState
                {
                    Id = 1,
                    CurrentChallengeRoundNumber = 1,
                    PayloadJSON = null,
                    State = ContestState.Standby,
                    Timestamp = _clock.GetCurrentInstant()
                };

                existing.Id = (int)connection.Insert(existing, tx);
            }

            return existing;
        }


        public SystemState GetOrCreateCurrentState()
        {
            SystemState existing;

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    existing = GetOrCreateSystemStateInternal(connection, tx);

                    tx.Commit();
                }
            }

            return existing;
        }

        public void UpdateState<T>(Expression<Func<SystemState, T>> propertyExpression, T value)
        {
            if (!(propertyExpression.Body is MemberExpression memberExpression))
                throw new InvalidCastException("propertyExpression body must be a MemberExpression (e.g. a lambda like x=>x.SomeProperty)");

            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    var existing = GetOrCreateSystemStateInternal(connection, tx);

                    var propertyInfo = memberExpression.Member as PropertyInfo;

                    Debug.Assert(propertyInfo != null, "propertyInfo != null");

                    propertyInfo.SetValue(existing, value);

                    existing.Timestamp = _clock.GetCurrentInstant();

                    connection.Update(existing, transaction: tx);

                    tx.Commit();
                }
            }
        }

        public void AddOrUpdateActiveChat(long chatId, string chatName)
        {
            using (var connection = CreateOpenConnection())
            {
                using (var tx = connection.BeginTransaction())
                {
                    var existing = connection.Get<ActiveChat>(chatId, transaction: tx);

                    if (existing != null)
                    {
                        existing.Timestamp = _clock.GetCurrentInstant();
                        connection.Update(existing, transaction: tx);
                    }
                    else
                    {
                        connection.Insert(new ActiveChat
                        {
                            Id = chatId,
                            Name = chatName,
                            Timestamp = _clock.GetCurrentInstant()
                        }, transaction: tx);
                    }

                    tx.Commit();
                }
            }
        }

        public void RemoveActiveChat(long chatId)
        {
            using (var connection = CreateOpenConnection())
            {
                connection.Delete(new ActiveChat { Id = chatId });
            }

        }

        public void SetCurrentTask(SelectedTaskKind taskKind, string template)
        {
            UpdateState(s => s.CurrentTaskKind, taskKind);
            UpdateState(s => s.CurrentTaskTemplate, template);
        }



        public void UpdateUser(User user, long chatId)
        {
            using (var connection = CreateOpenConnection())
            {
                user.LastActivityUTC = _clock.GetCurrentInstant();
                user.ChatId = chatId;

                connection.Update<User>(user);
            }
        }

        public void DeleteUserWithPrivateChatId(long? chatId)
        {
            using (var connection = CreateOpenConnection())
            {
                var qty = connection.Execute(@"DELETE FROM User WHERE ChatId = @Id", new { Id = chatId });

                logger.Info($"{qty} user(s) with ChatId {chatId} deleted");
            }
        }

    }

}
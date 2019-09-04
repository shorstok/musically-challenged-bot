using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using musicallychallenged.Domain;

namespace musicallychallenged.Data
{
    public interface IRepository
    {
        User CreateOrGetUserByTgIdentity(Telegram.Bot.Types.User user);
        User GetExistingUserWithTgId(int id);
        void UpdateUser(User user, long chatId);

        User[] GetAllActiveUsersWithCredentials(UserCredentials userCredentials);
        void GetOrCreateContestEntry(User user, long chatId, int forwaredMessageId, int containerMessageId,
            int challengeRoundNumber, out ActiveContestEntry previous);

        IEnumerable<Tuple<Vote, User>> GetVotesForEntry(int entryId);

        IEnumerable<ActiveContestEntry> GetActiveContestEntries();
        IEnumerable<ActiveContestEntry> ConsolidateVotesForActiveEntriesGetAffected();
        ActiveContestEntry GetExistingEntry(int entryId);
        ActiveContestEntry GetActiveContestEntryForUser(int userId);
        void UpdateContestEntry(ActiveContestEntry entry);
        void DeleteContestEntry(int deletedEntryId);

        void SetOrRetractVote(User voter, int activeEntryId, int voteValue, out bool retracted);
        
        SystemState GetOrCreateCurrentState();
        void UpdateState<T>(Expression<Func<SystemState, T>> propertyExpression, T value);


        void AddOrUpdateActiveChat(long chatId, string chatName);
        void RemoveActiveChat(long chatId);


        void DeleteUserWithPrivateChatId(long? chatId);
        bool MigrateChat(long fromId, long toId);

        RandomTask[] GetLeastUsedRandomTasks();
        void UpdateRandomTask(RandomTask task);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using musicallychallenged.Domain;
using NodaTime;

namespace musicallychallenged.Data
{
    public interface IRepository
    {
        User CreateOrGetUserByTgIdentity(Telegram.Bot.Types.User user);
        User GetExistingUserWithTgId(long id);
        void UpdateUser(User user, long chatId);

        User[] GetAllActiveUsersWithCredentials(UserCredentials userCredentials);
        void GetOrCreateContestEntry(User user, long chatId, int forwaredMessageId, int containerMessageId,
            int challengeRoundNumber, out ActiveContestEntry previous);

        IEnumerable<Tuple<Vote, User>> GetVotesForEntry(int entryId);

        IEnumerable<ActiveContestEntry> GetActiveContestEntries();
        IEnumerable<ActiveContestEntry> ConsolidateVotesForActiveEntriesGetAffected();
        ActiveContestEntry GetExistingEntry(int entryId);
        ActiveContestEntry GetActiveContestEntryForUser(long userId);
        int GetFinishedContestEntryCountForUser(long userId);
        void UpdateContestEntry(ActiveContestEntry entry);
        void DeleteContestEntry(int deletedEntryId);

        void SetOrUpdateVote(User voter, int activeEntryId, int voteValue, out bool updated);
        
        SystemState GetOrCreateCurrentState();
        void UpdateState<T>(Expression<Func<SystemState, T>> propertyExpression, T value);
        void SetCurrentTask(SelectedTaskKind taskKind, string template);


        void AddOrUpdateActiveChat(long chatId, string chatName);
        void RemoveActiveChat(long chatId);


        void DeleteUserWithPrivateChatId(long? chatId);
        bool MigrateChat(long fromId, long toId);

        RandomTask[] GetLeastUsedRandomTasks();
        void UpdateRandomTask(RandomTask task);
        double? GetAverageVoteForUser(User user);
        int GetVoteCountForActiveEntriesForUser(User user);
        bool MaybeCreateVoteForAllActiveEntriesExcept(User user, int entryId, int defaultVoteValue);
        int CloseAllPostponeRequests(PostponeRequestState finalState);
        PostponeRequest[] GetOpenPostponeRequestsForUser(long authorId);
        long GetUsedPostponeQuotaForCurrentRoundMinutes();
        PostponeRequest[] CreatePostponeRequestRetrunOpen(User author, Duration postponeDuration);
        void FinalizePostponeRequests(PostponeRequest keyRequest);

        NextRoundTaskPoll GetOpenNextRoundTaskPoll();
        void CreateNextRoundTaskPoll();
        IEnumerable<TaskSuggestion> CloseNextRoundTaskPollAndConsolidateVotes();
        void CreateOrUpdateTaskSuggestion(User author, string description, long containerChatId,
            int containerMessageId, out TaskSuggestion previous);
        IEnumerable<TaskSuggestion> GetActiveTaskSuggestions();
        TaskSuggestion GetExistingTaskSuggestion(int suggestionId);
        IEnumerable<Tuple<TaskPollVote, User>> GetVotesForTaskSuggestion(int suggestionId);
        void SetOrUpdateTaskPollVote(User voter, int suggestionId,
            int value, out bool updated);
        bool MaybeCreateVoteForAllActiveSuggestionsExcept(User user, int suggestionId, int defaultVoteValue);
        void SetNextRoundTaskPollWinner(long winnerId);
        long? GetLastTaskPollWinnerId();
        void DeleteTaskSuggestion(int suggestionId);
        void MarkUserAsAdministrator(long result);
    }
}

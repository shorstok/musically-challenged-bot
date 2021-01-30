using musicallychallenged.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Services
{
    public class NextRoundTaskPollVotingController : ITelegramQueryHandler
    {
        public string Prefix { get; } = "nv";

        public Task ExecuteQuery(CallbackQuery callbackQuery)
        {
            throw new NotImplementedException();
        }

        public async Task StartVotingAsync()
        {
            throw new NotImplementedException();
        }

        public async Task<Tuple<VotingController.FinalizationResult, User>> FinalizeVoting()
        {
            throw new NotImplementedException();
        }
    }
}

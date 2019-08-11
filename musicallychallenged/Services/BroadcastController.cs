using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace musicallychallenged.Services
{
    public class BroadcastController
    {
        private readonly IRepository _repository;
        private readonly ITelegramClient _client;

        private static readonly ILog logger = Log.Get(typeof(BroadcastController));


        public BroadcastController(IRepository repository, ITelegramClient client)
        {
            _repository = repository;
            _client = client;
        }

        public async Task<Message> AnnounceInVotingChannel(string announcement, bool pin,
            params Tuple<string, string>[] templateValues)
        {
            var state = _repository.GetOrCreateCurrentState();

            return await AnnounceInternal(announcement, pin, templateValues,
                state.VotingChannelId);
        }

        public async Task<Message> AnnounceInMainChannel(string announcement, bool pin,
            params Tuple<string, string>[] templateValues)
        {
            var state = _repository.GetOrCreateCurrentState();

            return await AnnounceInternal(announcement, pin, templateValues,
                state.MainChannelId);
        }

        private async Task<Message> AnnounceInternal(string announcement,
            bool doPin,
            Tuple<string, string>[] templateValues, long? channelId)
        {
            if (channelId == null)
            {
                logger.Error($"state.[Main/Voting]ChannelId not set, cant announce");
                return null;
            }

            var content = LocTokens.SubstituteTokens(announcement, templateValues);

            var message = await _client.SendTextMessageAsync(channelId, content, ParseMode.Html).ConfigureAwait(false);

            if (message == null)
            {
                logger.Error($"Bot having trouble sending messages to main channel id {channelId}");
                return null;
            }

            if (doPin)
            {
                await Task.Delay(100).ConfigureAwait(false);

                await _client.PinChatMessageAsync(message.Chat.Id, message.MessageId)
                    .ConfigureAwait(false);

            }

            return message;
        }

        public async Task SqueakToAdministrators(string message)
        {
            var admins = _repository.GetAllActiveUsersWithCredentials(UserCredentials.Admin);

            foreach (var admin in admins)
            {
                if (admin.ChatId == null)
                {
                    logger.Warn($"Admin {admin.GetUsernameOrNameWithCircumflex()} has chatid = null");
                    continue;
                }

                await _client.SendTextMessageAsync(admin.ChatId.Value, message, ParseMode.Html);
            }
        }
    }
}
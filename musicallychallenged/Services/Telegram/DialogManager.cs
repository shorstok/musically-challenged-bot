using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using musicallychallenged.Config;
using Telegram.Bot.Types;

namespace musicallychallenged.Services.Telegram
{
    public class DialogManager
    {
        private readonly ITelegramClient _botService;
        private readonly BotConfiguration _configuration;

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Dialog>> _activeDialogs =
            new ConcurrentDictionary<int, ConcurrentDictionary<Guid, Dialog>>();

        public DialogManager(ITelegramClient botService, BotConfiguration configuration)
        {
            _botService = botService;
            _configuration = configuration;
        }

        public Dialog GetActiveDialogForUserId(int userId)
        {
            return !_activeDialogs.TryGetValue(userId, out var dialogs) ? 
                null : 
                dialogs.FirstOrDefault().Value;
        }

        public Dialog StartNewDialogExclusive(long chatId, int userId, string tag)
        {
            var dialogs = _activeDialogs.GetOrAdd(userId, new ConcurrentDictionary<Guid, Dialog>());

            //cancel all existing dialogs

            foreach (var dialog in dialogs)
            {
                dialog.Value.Cancel();

                //dialog would be removed in RecycleDialog too, but just in case
                dialogs.TryRemove(dialog.Key, out var _);
            }

            //create new dialog, save it and return

            var result = new Dialog(_botService,chatId,userId);

            result.Tag = tag;

            return dialogs.AddOrUpdate(result.DialogId, result, (id, existing) => result);
        }

        public void RecycleDialog(Dialog dialog)
        {
            if(!_activeDialogs.TryGetValue(dialog.UserId,out var dialogs))
                return;

            dialogs.TryRemove(dialog.DialogId, out var _);         
        }

        public Dialog GetActiveDialogByChatId(long chatId, User user)
        {
            if(!_activeDialogs.TryGetValue(user.Id,out var dialogs))
                return null;

            //it is almost always has to be first dialog, considering 1 chat per user rule
            return dialogs.Values.FirstOrDefault(dialog => dialog.ChatId == chatId);            
        }

        public void Prune()
        {
            var limit = TimeSpan.FromMinutes(_configuration.DialogInactivityTimeoutMinutes);

            foreach (var keyValuePair in _activeDialogs)
            {
                //just for readability
                var dialogs = keyValuePair.Value;

                foreach (var dialog in dialogs)
                {
                    if (DateTime.UtcNow - dialog.Value.LastUpdated <= limit) 
                        continue;

                    dialog.Value.Cancel();
                    dialogs.TryRemove(dialog.Key, out var _);
                }
            }
        }
    }
}
using System.Threading.Tasks;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types;

namespace musicallychallenged.Commands
{
    public interface ITelegramQueryHandler
    {
        /// <summary>
        /// Callback query <see cref="CallbackQuery"/> data should look like 'prefix:actual data'. 
        /// <see cref="CommandManager"/> looks for matching <see cref="ITelegramQueryHandler"/> using these prefixes
        /// </summary>
        string Prefix { get; }

        Task ExecuteQuery(CallbackQuery callbackQuery);
    }
}
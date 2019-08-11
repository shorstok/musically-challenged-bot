using System.Threading.Tasks;
using musicallychallenged.Domain;
using musicallychallenged.Services.Telegram;

namespace musicallychallenged.Commands
{
    public interface ITelegramCommandHandler
    {
        Task ProcessCommandAsync(Dialog dialog, User user);
        
        string CommandName { get; }
        string UserFriendlyDescription{get;}
    }
}
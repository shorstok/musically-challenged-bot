using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace musicallychallenged.Services.Sync
{
    public abstract class PayloadExtractor
    {
        public abstract Task<string> ExtractPayloadToFile(Message container, CancellationToken cancellationToken);
        public abstract Task DisposePayload(string payloadFile, CancellationToken token);
    }
}
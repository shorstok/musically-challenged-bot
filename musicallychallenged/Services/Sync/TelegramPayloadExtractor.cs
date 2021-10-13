using System.IO;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Logging;
using musicallychallenged.Services.Telegram;
using Telegram.Bot.Types;
using File = System.IO.File;

namespace musicallychallenged.Services.Sync
{
    public class TelegramPayloadExtractor : PayloadExtractor
    {
        private readonly ITelegramClient _telegramClient;
        
        private static readonly ILog logger = Log.Get(typeof(TelegramPayloadExtractor));
        private readonly string _payloadStoragePath;

        public TelegramPayloadExtractor(ITelegramClient telegramClient)
        {
            _telegramClient = telegramClient;
            _payloadStoragePath = Path.Combine(PathService.AppData, "temp-payloads");

            EnsurePayloadStoragePathExists();
        }

        private void EnsurePayloadStoragePathExists()
        {
            if (Directory.Exists(_payloadStoragePath)) 
                return;
            
            logger.Info($"Creating payload storage path {_payloadStoragePath}");
            Directory.CreateDirectory(_payloadStoragePath);
        }

        public override Task DisposePayload(string payloadFile, CancellationToken token)
        {
            if (File.Exists(payloadFile))
                File.Delete(payloadFile);
            else
                logger.Warn($"Dispose - payload not found: `{payloadFile}`");

            return Task.CompletedTask;
        }

        public override async Task<string> ExtractPayloadToFile(Message container, CancellationToken cancellationToken)
        {
            if (container?.Audio == null)
            {
                logger.Warn($"No audio payload to extract from message {container?.MessageId}");
                return null;
            }

            //Guess. FFMPEG wants some extension

            var extension = container?.Audio?.MimeType?.StartsWith("audio/")??false  ? "mp3" : "mp4"; //lol

            var tempFilename =
                Path.Combine(_payloadStoragePath, $"{container?.MessageId}-{Path.GetRandomFileName()}.{extension}");
            
            await using (var tempFileStream = File.Create(tempFilename))
            {
                await _telegramClient.DownloadFile(container, tempFileStream, cancellationToken);
            }

            return Path.GetFullPath(tempFilename);
        }
    }
}
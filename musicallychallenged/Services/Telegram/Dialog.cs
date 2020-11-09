using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace musicallychallenged.Services.Telegram
{
    public class Dialog : IDisposable
    {
        public static int MaxBoundedCapacity = 15; //unread messages throttle by default,
        
        public int UserId { get; }
        public long ChatId { get; }
        public DateTime LastUpdated { get; private set; }

        public Guid DialogId { get; } = Guid.NewGuid();

        public string Tag { get; set; }

        public ITelegramClient TelegramClient { get; }
        
        private readonly CancellationTokenSource _cancellation;
        private readonly BufferBlock<Message> _messageBlock;
        private readonly BufferBlock<CallbackQuery> _callbackQueryBlock;

        public Dialog(ITelegramClient telegramClient, long chatId, int userId)
        {
            TelegramClient = telegramClient;
            ChatId = chatId;
            UserId = userId;

            _cancellation = new CancellationTokenSource();

            _messageBlock = new BufferBlock<Message>(new DataflowBlockOptions
            {
                BoundedCapacity = MaxBoundedCapacity,
                CancellationToken = _cancellation.Token
            });

            _callbackQueryBlock= new BufferBlock<CallbackQuery>(new DataflowBlockOptions
            {
                BoundedCapacity = MaxBoundedCapacity,
                CancellationToken = _cancellation.Token
            });
        }
        
        public void Dispose()
        {
            _messageBlock.Complete();
            _callbackQueryBlock.Complete();
            _cancellation.Dispose();
        }

        public async Task<Message> GetMessageInThreadAsync(CancellationToken token)
        {
            LastUpdated = DateTime.UtcNow;

            return await _messageBlock.ReceiveAsync(token);
        }

        public async Task<CallbackQuery> GetCallbackQueryAsync(CancellationToken token)
        {
            LastUpdated = DateTime.UtcNow;

            return await _callbackQueryBlock.ReceiveAsync(token);
        }

        internal async Task NotifyMessageArrived(Message message)
        {
            LastUpdated = DateTime.UtcNow;

            await _messageBlock.SendAsync(message);
        }

        internal async Task NotifyCallbackQueryReceived(CallbackQuery callbackQuery)
        {
            LastUpdated = DateTime.UtcNow;

            await _callbackQueryBlock.SendAsync(callbackQuery);
        }

        public async Task<string> AskForMessageWithConfirmation(CancellationToken token, string whatToAskFor)
        {
            var message = await TelegramClient.SendTextMessageAsync(ChatId, whatToAskFor, ParseMode.Html,
                cancellationToken: token);

            if (null == message)
            {
                //sending failed for some reason -- no sense to wait for answer
                return null;
            }

            return (await GetMessageInThreadAsync(token)).Text;
        }

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }


    }
}
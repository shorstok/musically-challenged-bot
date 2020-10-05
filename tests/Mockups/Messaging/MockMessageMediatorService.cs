using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace tests.Mockups.Messaging
{
    public class MockMessageMediatorService
    {
        public delegate void InsertMockMessageEvent(Message message);
        public delegate void GetMockMessageEvent(long chatId, int messageId, out Message message);

        public event InsertMockMessageEvent OnInsertMockMessage;
        public event GetMockMessageEvent OnGetMockMessage;



        public Message GetMockMessage(long chatId, int messageId)
        {
            Message result = null;

            OnGetMockMessage?.Invoke(chatId, messageId, out result);

            return result;
        }

        public void InsertMockMessage(Message message)
        {
            if(message.Chat == null || message.Chat?.Id == 0)
                throw new ArgumentException("Message should have valid Chat set", nameof(message));
            if(message.MessageId == 0)
                throw new ArgumentException("Message should have valid MessageId set", nameof(message));

            InvokeInsertMockMessage(message);
        }


        protected virtual void InvokeInsertMockMessage(Message message)
        {
            OnInsertMockMessage?.Invoke(message);
        }

    }
}

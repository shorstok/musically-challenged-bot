namespace tests.Mockups.Messaging
{
    internal class AnswerCallbackQueryMock : MockMessage
    {
        public string CallbackQueryId { get; }
        public string Text { get; }
        public bool ShowAlert { get; }
        public string Url { get; }
        public int CacheTime { get; }

        public AnswerCallbackQueryMock(string callbackQueryId, string text, bool showAlert, string url, int cacheTime)
        {
            CallbackQueryId = callbackQueryId;
            Text = text;
            ShowAlert = showAlert;
            Url = url;
            CacheTime = cacheTime;
        }
    }
}
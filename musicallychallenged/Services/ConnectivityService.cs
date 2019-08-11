using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace musicallychallenged.Services
{
    public static class ConnectivityService
    {
        public static async Task<bool> CheckIsConnected()
        {
            if (await CheckUsingPing().ConfigureAwait(false))
                return true;

            if (await CheckUsingGoogleCn204().ConfigureAwait(false))
                return true;

            return false;
        }

        private static async Task<bool> CheckUsingGoogleCn204()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://g.cn/generate_204");
                request.UserAgent = "Android";
                request.KeepAlive = false;
                request.Timeout = 1500;

                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                    return response.ContentLength == 0 && response.StatusCode == HttpStatusCode.NoContent;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async Task<bool> CheckUsingPing()
        {
            try
            {
                var myPing = new Ping();
                var host = "google.com";

                var buffer = new byte[32];
                var timeout = 1000;

                var reply = await myPing.SendPingAsync(host, timeout, buffer);

                return reply?.Status == IPStatus.Success;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

}

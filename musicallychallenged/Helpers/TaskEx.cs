using System.Threading.Tasks;

namespace musicallychallenged.Helpers
{
    public static class TaskEx
    {
        public static async Task<object> TaskToObject<T>(Task<T> task) => await task;
    }
}

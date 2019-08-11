using System;
using System.IO;

namespace musicallychallenged.Services
{
    public static class PathService
    {
        public static string AppData { get; }

        static PathService()
        {
            AppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "music-challenge-bot");
        }

        public static void EnsurePathExists()
        {
            if (!Directory.Exists(AppData))
                Directory.CreateDirectory(AppData);
        }
    }
}
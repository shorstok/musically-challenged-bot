using System;
using System.IO;

namespace musicallychallenged.Services
{
    public static class PathService
    {
        public static string AppData =>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "music-challenge-bot");

        static PathService()
        {
            
        }

        public static void EnsurePathExists()
        {
            if (!Directory.Exists(AppData))
                Directory.CreateDirectory(AppData);
        }
    }
}
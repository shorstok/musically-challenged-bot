﻿using System;
using System.IO;

namespace musicallychallenged.Services
{
    public static class PathService
    {
        public static string AppData =>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "music-challenge-bot");
        
        public static string TempData =>Path.Combine(AppData,"temp");

        public static string BotDbPath => Path.Combine(AppData, @"bot.sqlite");

        static PathService()
        {
            
        }

        public static void EnsurePathExists()
        {
            if (!Directory.Exists(AppData))
                Directory.CreateDirectory(AppData);
            if (!Directory.Exists(TempData))
                Directory.CreateDirectory(TempData);
        }

        public static string GetTempFilename() => Path.Combine(TempData, Path.GetRandomFileName() + ".tmp");
    }
}
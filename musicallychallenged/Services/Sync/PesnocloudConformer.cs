using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Logging;

namespace musicallychallenged.Services.Sync
{
    public class PesnocloudConformer
    {
        private readonly IBotConfiguration _botConfiguration;

        private static readonly ILog logger = Log.Get(typeof(PesnocloudConformer));

        public PesnocloudConformer(IBotConfiguration botConfiguration)
        {
            _botConfiguration = botConfiguration;
            
            if(!File.Exists(_botConfiguration.FfmpegPath))
                logger.Error($"FFMPEG not found at {_botConfiguration.FfmpegPath} - conforming not available");
        }

        public async Task<string> ConformAudio(string sourceFileName,
            string desiredResultPath,
            CancellationToken token)
        {
            if (!Path.IsPathRooted(desiredResultPath))
                desiredResultPath = Path.GetFullPath(desiredResultPath ?? throw new ArgumentNullException(nameof(desiredResultPath)));
            if (!Path.IsPathRooted(sourceFileName))
                sourceFileName = Path.GetFullPath(
                    sourceFileName ?? throw new ArgumentNullException(nameof(sourceFileName)));
                        
            if(!File.Exists(_botConfiguration.FfmpegPath))
            {
                logger.Error($"FFMPEG not found at {_botConfiguration.FfmpegPath} - not conforming");
                return sourceFileName;
            }
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(_botConfiguration.FfmpegPath),
                Arguments = $"-y -i \"{sourceFileName}\" -acodec libmp3lame -b:a 192k \"{desiredResultPath}\"",
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = Process.Start(processStartInfo);
            
            if(null == process)
                return sourceFileName;

            var errorString = new StringBuilder();

            process.ErrorDataReceived += (_, args) => errorString.Append(args.Data);

            process.BeginErrorReadLine();

            Console.WriteLine($"Started {process.Id}");

            try
            {
                await process.WaitForExitAsync(token);

                if (process.ExitCode == 0) 
                    return desiredResultPath;
                
                if (errorString.Length > 0)
                {
                    logger.Error($"Ffmpeg conversion resulted in error: {errorString.ToString()}");
                    return sourceFileName;
                }

                return desiredResultPath;
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill();
            }
        }

    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Layout;
using musicallychallenged.Services;

namespace musicallychallenged.Logging
{
    public static class Log
    {
        static Log()
        {
            Log4NetCfgFilename = Path.Combine(PathService.AppData, "log4net.config");

            if (File.Exists(Log4NetCfgFilename))
            {
                XmlConfigurator.Configure(new FileInfo(Log4NetCfgFilename));
            }
            else
            {
                var layout = new PatternLayout("%-5level %logger{1}.%method - %message%newline")
                {
                    Header = "Logging started"
                };

                var appender = new ConsoleAppender
                {
                    Layout = layout
                };

                layout.ActivateOptions();
                appender.ActivateOptions();

                BasicConfigurator.Configure(appender);

                Get(typeof(Log)).Info("Logfile " + Log4NetCfgFilename +
                                      " not found, defaulting to ConsoleAppender test config.");
            }
        }

        public static string Log4NetCfgFilename { get; }

        public static ILog Get(Type what)
        {
            return LogManager.GetLogger(what);
        }

        public static ILog Get(string what)
        {
            return LogManager.GetLogger(what);
        }
    }
}

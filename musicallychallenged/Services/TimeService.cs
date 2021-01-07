using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using NodaTime;
using TimeZoneInfo = System.TimeZoneInfo;

namespace musicallychallenged.Services
{
    public class TimeService
    {
        private readonly IBotConfiguration _configuration;
        private readonly LocStrings _loc;
        private readonly IClock _clock;
        private readonly IRepository _repository;

        private static readonly ILog logger = Log.Get(typeof(TimeService));
        
        public TimeService(IBotConfiguration configuration, 
            LocStrings loc,
            IClock clock,
            IRepository repository)
        {
            _configuration = configuration;
            _loc = loc;
            _clock = clock;
            _repository = repository;
        }

        static TimeService()
        {

        }

        public ZonedDateTime GetInstantInBotTime(Instant instant)
        {
            return instant.InZone(DateTimeZoneProviders.Tzdb[_configuration.AnnouncementTimeZone]);
        }

        public bool TryParseLocalTimeInAnnouncementTimeZone(string input, out Instant result)
        {
            result = Instant.MinValue;

            var optionalYearLocalDtRegex =
                new Regex(
                    "(?<day>\\d+)[.\\/](?<mon>\\d+)([.\\/](?<year>\\d\\d|\\d\\d\\d\\d))?\\s+" +
                    "(?<hr>\\d+)[:;](?<min>\\d+)");

            try
            {
                var timeZone = DateTimeZoneProviders.
                    Tzdb[_configuration.AnnouncementTimeZone];
                
                var match = optionalYearLocalDtRegex.Match(input);

                if (!match.Success)
                    return false;

                //Required matches, throw on missing

                var day = int.Parse(match.Groups["day"].Value);
                var mon = int.Parse(match.Groups["mon"].Value);
                var hr = int.Parse(match.Groups["hr"].Value);
                var min = int.Parse(match.Groups["min"].Value);

                var year = match.Groups["year"]?.Success == true ? int.Parse(match.Groups["year"].Value) : (int?) null;

                if (year == null)
                    year = _clock.GetCurrentInstant().InZone(timeZone).Year;
                if (year < 100)
                    year = year + ((_clock.GetCurrentInstant().InZone(timeZone).Year) / 100) * 100;

                result = timeZone.
                    AtLeniently(new LocalDateTime(year.Value,mon,day,hr,min)).
                    ToInstant();            

                return true;
            }
            catch (Exception)
            {
                return false;
            }      
        }

        public string FormatDateAndTimeToAnnouncementTimezone(Instant date)
        {
            var zoned = date.InZone(DateTimeZoneProviders.Tzdb[_configuration.AnnouncementTimeZone]);

            return zoned.ToString(_configuration.AnnouncementDateTimeFormat,CultureInfo.InvariantCulture)+
                   " ("+_configuration.AnnouncementTimeDescriptor+")";;                   
        }

        public Instant ScheduleNextDeadlineIn(int days, int hoursAt)
        {
            var dayStart = _clock.
                GetCurrentInstant().
                InZone(DateTimeZoneProviders.Tzdb[_configuration.AnnouncementTimeZone]).
                Date.
                PlusDays(days);

            var deadline = DateTimeZoneProviders.Tzdb[_configuration.AnnouncementTimeZone].AtStartOfDay(dayStart)
                .PlusHours(hoursAt).
                ToInstant();

            logger.Info($"Setting deadline to {deadline}");

            _repository.UpdateState(s=>s.NextDeadlineUTC, deadline);

            return deadline;
        }

        public string FormatTimeLeftTillDeadline()
        {
            var duration = GetTimeLeftTillDeadline();

            var rounded = Duration.FromMinutes(Math.Round(duration.TotalMinutes / 15) * 15);

            if(rounded.Days == 0 && rounded.Hours == 0 && rounded.Minutes == 0)
                return _loc.AlmostNothing;

            var builder = new StringBuilder();           

            if (rounded.Days > 0)
                builder.Append($"{rounded.Days}{_loc.DimDays} ");            
            if (rounded.Hours > 0)
                builder.Append($"{rounded.Hours}{_loc.DimHours} ");            
            if (rounded.Minutes > 0)
                builder.Append($"{rounded.Minutes}{_loc.DimMinutes} ");            

            if (builder.Length == 0)
                return _loc.AlmostNothing;

            return builder.ToString().Trim();
        }

        public Duration GetTimeLeftTillDeadline()
        {
            var state = _repository.GetOrCreateCurrentState();

            return Period.Between(
                _clock.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb[_configuration.AnnouncementTimeZone])
                    .LocalDateTime,
                state.NextDeadlineUTC.InZone(DateTimeZoneProviders.Tzdb[_configuration.AnnouncementTimeZone])
                    .LocalDateTime).ToDuration();
        }
    }
}

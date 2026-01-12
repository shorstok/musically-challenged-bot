using System;
using System.Data;
using System.Data.SqlClient;
using Dapper;
using NodaTime;
using NodaTime.Text;

namespace musicallychallenged.Data
{
    /// <summary>
    /// Using string as Instant storage to circumvent database time conversion bugs altogether
    /// </summary>
    public class InstantHandler : SqlMapper.TypeHandler<Instant>
    {
        public static readonly InstantHandler Default = new InstantHandler();

        public override void SetValue(IDbDataParameter parameter, Instant value)
        {
            parameter.Value = InstantPattern.General.Format(value);

            if (parameter is SqlParameter sqlParameter)
            {
                sqlParameter.SqlDbType = SqlDbType.Text;
            }
        }

        public override Instant Parse(object value)
        {
            if (value is DateTime dateTime)
            {
                var dt = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                return Instant.FromDateTimeUtc(dt);
            }

            if (value is string iso)
            {
                var general = InstantPattern.General.Parse(iso);
                if (general.Success)
                    return general.Value;

                var extended = InstantPattern.ExtendedIso.Parse(iso);
                if (extended.Success)
                    return extended.Value;

                return Instant.MinValue;
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                return Instant.FromDateTimeOffset(dateTimeOffset);
            }

            throw new DataException("Cannot convert " + value.GetType() + " to NodaTime.Instant");
        }
    }
}

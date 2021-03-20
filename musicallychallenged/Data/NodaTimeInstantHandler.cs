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
                try
                {
                    return InstantPattern.General.Parse(iso).Value;
                }
                catch (Exception)
                {
                    return Instant.MinValue;                    
                }
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                return Instant.FromDateTimeOffset(dateTimeOffset);
            }

            throw new DataException("Cannot convert " + value.GetType() + " to NodaTime.Instant");
        }
    }
}
using System.Data.Common;

namespace musicallychallenged.Data
{
    public interface IDbConnectionFactory
    {
        DbConnection CreateOpenConnection();
    }
}

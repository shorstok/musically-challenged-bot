using System;

namespace musicallychallenged.Domain
{
    [Flags]
    public enum UserCredentials
    {
        User = 0,
        Admin = 1,
        Supervisor = 2
    }
}

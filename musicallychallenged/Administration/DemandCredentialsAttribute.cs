using System;
using musicallychallenged.Domain;

namespace musicallychallenged.Administration
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DemandCredentialsAttribute : Attribute
    {
        public UserCredentials[] Credentials { get; }

        public DemandCredentialsAttribute(params UserCredentials[] userCredentials)
        {
            Credentials = userCredentials;
        }
    }
}
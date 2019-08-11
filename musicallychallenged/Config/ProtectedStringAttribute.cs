using System;

namespace musicallychallenged.Config
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class ProtectedStringAttribute : Attribute
    {
        public const string CleartextPrefix = "cleartext:";
    }
}
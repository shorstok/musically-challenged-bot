using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace musicallychallenged.Config
{
    public static class CurrentUserProtectedString
    {
        public static string Protect(this string clearText,
            DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            if (clearText == null)
                throw new ArgumentNullException(nameof(clearText));

            var clearBytes = Encoding.UTF8.GetBytes(clearText);

            var encryptedBytes = ProtectedData.Protect(clearBytes, null, scope);

            return Convert.ToBase64String(encryptedBytes);
        }

        public static string Unprotect(this string encryptedText,
            DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            if (encryptedText == null)
                throw new ArgumentNullException(nameof(encryptedText));

            var encryptedBytes = Convert.FromBase64String(encryptedText);

            var clearBytes = ProtectedData.Unprotect(encryptedBytes, null, scope);

            return Encoding.UTF8.GetString(clearBytes);
        }

        public static bool GenerateProtectedPropertiesFromCleartext(object source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var protectedStrings = source.GetType().GetProperties()
                .Where(pi => pi.GetCustomAttributes(typeof(ProtectedStringAttribute)).Any()).ToArray();

            var removedClearsource = false;

            foreach (var propertyInfo in protectedStrings)
            {
                if (!(propertyInfo.GetValue(source) is string text))
                    continue;

                if (!text.StartsWith(ProtectedStringAttribute.CleartextPrefix))
                    continue;

                text = text.Substring(ProtectedStringAttribute.CleartextPrefix.Length);

                propertyInfo.SetValue(source, text.Protect());

                removedClearsource = true;
            }

            return removedClearsource;
        }
    }
}
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace musicallychallenged.Config
{
    public static class CurrentUserProtectedString
    {
        public static string Protect(this string clearText)
        {
            if (clearText == null)
                throw new ArgumentNullException(nameof(clearText));
            if (!OperatingSystem.IsWindows())
                throw new NotSupportedException("Data protection only in Windows");

            var clearBytes = Encoding.UTF8.GetBytes(clearText);

            var encryptedBytes = ProtectedData.Protect(clearBytes, null, DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encryptedBytes);
        }

        public static string Unprotect(this string encryptedText)
        {
            if (encryptedText == null)
                throw new ArgumentNullException(nameof(encryptedText));
            if (!OperatingSystem.IsWindows())
                throw new NotSupportedException("Data protection only in Windows");
            
            var encryptedBytes = Convert.FromBase64String(encryptedText);

            var clearBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);

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
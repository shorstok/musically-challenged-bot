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
            if (string.IsNullOrEmpty(clearText))
                return clearText;
            if (!OperatingSystem.IsWindows())
                return clearText;   //In non-windows environment pass cleartext transparently

            var clearBytes = Encoding.UTF8.GetBytes(clearText);

            var encryptedBytes = ProtectedData.Protect(clearBytes, null, DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encryptedBytes);
        }

        public static string Unprotect(this string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;
            if (!OperatingSystem.IsWindows())
                return encryptedText;   //In non-windows environment treat encryptedText as cleartext
            
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using musicallychallenged.Localization;
using NUnit.Framework;

namespace tests
{
    public static class LocalizationTestingHelper
    {
        private static readonly Regex NoNestedTagsRegex = new Regex(@"((\<[^\/]+\>)[^\<\>]*){2,}", RegexOptions.Compiled);
        private static readonly Regex AnyTagRegex = new Regex(@"\<\/?([^>\s]+).*\>", RegexOptions.Compiled);
        private static readonly Regex LocTokenRegex = new Regex("%([A-Z])+%", RegexOptions.Compiled);

        public static void AssertValidTelegramHtml(string text)
        {
            var allowedTags = new HashSet<string>(new[] {"b", "i", "u", "s", "a", "code", "pre"});

            if (NoNestedTagsRegex.IsMatch(text))
                Assert.Fail($"Input string `{text}` has nested tags - not allowed in telegram");

            foreach (Match match in AnyTagRegex.Matches(text))
            {
                if(match.Groups.Count != 2)
                    Assert.Fail($"Input string `{text}` has invalid html");
                if(!allowedTags.Contains(match.Groups[1].Value.ToLowerInvariant()))
                    Assert.Fail($"Input tag `{match.Groups[1].Value}` is not allowed");
            }

        }

        public static void AssertNoHTML(string text)
        {
            if(AnyTagRegex.IsMatch(text))
                Assert.Fail($"Messages with HTML should be sent with ParseMode.HTML (message - {text}");
        }

        public static void AssertNoUnsubstitutedLocTokens(string text)
        {
            if(LocTokenRegex.IsMatch(text))
                Assert.Fail($"Unreplaced token found in `{text}`");
        }
    }
}

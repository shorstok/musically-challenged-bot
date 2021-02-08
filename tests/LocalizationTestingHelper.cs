using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;

namespace tests
{
    public static class LocalizationTestingHelper
    {
        private static readonly Regex NoNestedTagsRegex = new Regex(@"((\<[^\/]+\>)[^\<\>]*){2,}", RegexOptions.Compiled);
        private static readonly Regex AnyTagRegex = new Regex(@"\<\/?([^>\s]+).*\>", RegexOptions.Compiled);

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
    }
}

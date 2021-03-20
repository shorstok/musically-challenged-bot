using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using log4net;
using musicallychallenged.Domain;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using NUnit.Framework;
using NUnit.Framework.Internal;
using Telegram.Bot.Types.ReplyMarkups;
using tests.DI;
using tests.Mockups;
using tests.Mockups.Messaging;

namespace tests
{
    [TestFixture]
    public class LocalizationTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(LocalizationTestFixture));

        [Test]
        public void LocalizationTextShouldHaveValidHTML()
        {
            using (var compartment = new TestCompartment())
            {
                int validatedCount = 0;

                foreach (var propertyInfo in compartment.Localization.GetType().GetProperties())
                {
                    if(!propertyInfo.CanRead || propertyInfo.PropertyType != typeof(string))
                        continue;

                    var locString = propertyInfo.GetValue(compartment.Localization) as string;

                    Assert.IsNotNull(locString, $"Property `{propertyInfo.Name}` evaluates to null");

                    LocalizationTestingHelper.AssertValidTelegramHtml(locString);

                    ++validatedCount;
                }

                TestContext.WriteLine($"Validated {validatedCount} localization strings");
            }
        }
    }
}
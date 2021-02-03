using log4net;
using musicallychallenged.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tests
{
    [TestFixture]
    class NextRoundTaskPollTestFixture
    {
        private static readonly ILog Logger = Log.Get(typeof(NextRoundTaskPollTestFixture));

        [Test]
        public async Task ShouldSwitchToNextRoundTakPollStateWhenWinnerInitiatesPoll()
        {

        }
    }
}

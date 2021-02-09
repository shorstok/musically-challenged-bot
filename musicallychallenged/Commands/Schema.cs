using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace musicallychallenged.Commands
{
    public static class Schema
    {
        public const string StandbyCommandName = "standby";
        public const string DeadlineCommandName = "deadline";
        public const string DeployCommandName  = "deploy";
        public const string FastForwardCommandName = "ffwd";
        public const string KickstartCommandName = "kickstart";
        public const string KickstartNextRoundTaskPollCommandName = "pollkickstart";

        public const string RemindCommandName = "remind";

        public const string SubmitCommandName = "submit";
        public const string DescribeCommandName = "describe";
        public const string PostponeCommandName = "postpone";

        public const string TaskSuggestCommandName = "tasksuggest";
    }
}

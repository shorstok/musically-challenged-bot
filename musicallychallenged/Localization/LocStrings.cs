using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// ReSharper disable StringLiteralTypo
// ReSharper disable InconsistentNaming

namespace musicallychallenged.Localization
{
    public static class LocTokens
    {
        public const string VoteCount = "%VOTES%";
        public const string User = "%USER%";
        public const string Users = "%USERS%";
        public const string ChallengeRound = "%ROUND%";
        public const string ChallengeRoundTag = "%ROUNDTAG%";
        public const string Time = "%TIME%";
        public const string Details = "%DETAILS%";
        public const string TaskDescription = "%TASK%";
        public const string Deadline = "%DEADLINE%";
        public const string VotingChannelLink = "%VOTECHANLNK%";
        public const string RulesUrl = "%RULES%";
        public const string TaskFromPreface = "%FROMPREFACE%";

        public static string SubstituteTokens(string template, params Tuple<string, string>[] tokens)
        {
            var stringBuilder = new StringBuilder(template);

            foreach (var token in tokens)
                stringBuilder.Replace(token.Item1, token.Item2);

            return stringBuilder.ToString();
        }
    }

    /// <summary>
    /// Note: telegram supports limited tag count (and doesnt support nested)
    /// 
    /// The following tags are currently supported:
    ///
    ///<b>bold</b>, <strong>bold</strong>
    ///<i>italic</i>, <em>italic</em>
    ///<a href="http://www.example.com/">inline URL</a>
    ///<a href="tg://user?id=123456789">inline mention of a user</a>
    ///<code>inline fixed-width code</code>
    ///<pre>pre-formatted fixed-width code block</pre>
    /// </summary>


    public class LocStrings
    {
        public string CancelButtonLabel { get; set; } = "Я передумал";
        public string PostponeCommandHandler_Cancelled { get; set; } = "Отменено";
        public string PostponeCommandHandler_GeneralFailure { get; set; } = "😵";

        public string PostponeCommandHandler_AcceptedTemplate { get; set; } =
            $"Заявка принята. Нужен кворум ({LocTokens.Users} чел.) для переноса дедлайна";
        public string PostponeCommandHandler_AcceptedPostponedTemplate { get; set; } =
            $"Заявка принята, кворум ({LocTokens.Users} чел.) уже есть. Дедлайн передвинут";
        public string PostponeCommandHandler_DeniedNoQuotaLeftTemplate { get; set; } =
            $"На каждый раунд конкурса выделяется макс. {LocTokens.Time} ч. переноса дедлайна. " +
            $"К сожалению, оставшаяся квота по переносам не позвоялет принять вашу заявку 😐";

        public string PostponeCommandHandler_DeniedAlreadyHasOpenTemplate { get; set; } =
            $"У вас уже есть незакрытая заявка на этот раунд. Дождитесь наличия кворума по переносу ({LocTokens.Users} чел. необходимо)";

        public string PostponeCommandHandler_Preamble { get; set; } =
            "С помощью этой команды можно отложить дедлайн на некоторе время. Укажите, на какое время вы хотите отложить дедлайн";

        public string PostponeService_DeadlinePostponedQuorumFulfilled { get; set; } =
            $"Достаточное количество людей попросило отсрочку";

        public string ContestController_DeadlinePostponed { get; set; } =
            $"Дедлайн был перенесён на {LocTokens.Deadline}.\n" +
            $"Причина: {LocTokens.Details}";
        
        public string YouAreBanned { get; set; } = "Извините, бот недоступен 🧐";
        public string MissingCredentials { get; set; } = "Извините, команда недоступна";

        public string UnknownCommandUsageTemplate { get; set; } = $"Добрый день! Я - бот для челленджей. Воспользуйтесь одной из следующих команд: {Environment.NewLine}{Environment.NewLine}{LocTokens.Details}";

        public string GenericStandbyAnnouncement { get; set; } = "Челленджи приостановлены 😐";
        public string NotEnoughEntriesAnnouncement { get; set; } = "Челленджи приостановлены из-за нехватки участников 😐";
        public string NotEnoughVotesAnnouncement { get; set; } = $"Челленджи приостановлены из-за недостаточной активности участников (максимум голосовавших - {LocTokens.VoteCount} чел.) 😐";

        public string NotEnoughSuggestionsAnnouncement { get; set; } = "Челленджи приостановлены из-за нехватки предложенных заданий 😐";

        public string WeHaveAWinner { get; set; } = $@"В раунде Писца победил(а) {LocTokens.User} ({LocTokens.VoteCount} голосов), в данный момент он(а) выбирает задание для следующего раунда...";
        public string WeHaveWinners { get; set; } = $"В раунде Писца первое место разделили несколько человек: {LocTokens.Users} ({LocTokens.VoteCount} голосов). " +
                                                    $"С помощью рулетки из них был выбран {LocTokens.User}, который в данный момент он выбирает задание для следующего раунда...";

        public string CongratsPrivateMessage { get; set; } = "🎉🎉 Поздравляем с победой в раунде Писца! 🎉🎉";
        public string ChooseWiselyPrivateMessage { get; set; } = "Постарайтесь сформулировать задание для следующего раунда с учетом пожеланий Администрации 🧐";

        public string RandomTaskButtonLabel { get; set; } = "Давайте случайное";
        public string NextRoundTaskPollButtonLabel { get; set; } = "создадим голосование";
        
        public string RandomTaskSelectedMessage { get; set; } = "Для следующего раунда будет использовано случаное задание";
        public string TaskSelectedMessage { get; set; } = "Задание отправлено Администрации";
        public string InitiatedNextRoundTaskPollMessage { get; set; } = "Задание будет выбрано комьюнити";

        public string InvalidTaskMessage { get; set; } = "Извините, это не похоже на задание. Отошлите следующим в сообщении текст задания следующего раунда";
        public string SlackWarningMesage { get; set; } = $"Внимание, время на формулировку задания ограничено ({LocTokens.Time} ч.), " +
                                                         $"по истечению будет автоматически выбран вариант со случайным заданием";

        public string ChooseNextRoundTaskPrivateMessage { get; set; } = "Опишите в следующем сообщении задание для следующего раунда, выберите случайное задание (оно будет выбрано из внутреннего пула заданий случайным образом), либо создайте публичное голосование за новое задание";
        public string InnerCircleApprovedTaskMessage { get; set; } =
            "Ваше задание получило одобрение Администрации 🤩";
        public string InnerCircleDeclinedMessage { get; set; } =
            $"<b>Ваше задание было отклонено Администрацией</b>😕. Причина: {Environment.NewLine}{LocTokens.Details}";

        public string AdminApproveLabel { get; set; } = $"Approve";
        public string AdminDeclineLabel { get; set; } = $"Decline";
        public string AdminOverrideLabel { get; set; } = $"OVERRIDE";

        public string AdminVotingPrivateStarted { get; set; } = $"<b>👮‍ Началось внутреннее голосование администрации (премодерация задания на следующий раунд конурса). 👮‍</b>{Environment.NewLine}" +
                                                                $"Победитель предложил такое задание: {Environment.NewLine}{Environment.NewLine}{LocTokens.Details}{Environment.NewLine}{Environment.NewLine}" +
                                                                $"Вы можете либо оставить его (<b>Approve</b>), либо отправить на доработку. " +
                                                                $"При отправке на доработку укажите, в чем это задание было неприемлемо";

        public string AdminVotingSomeoneVotedNotification { get; set; } = $"Администратор {LocTokens.User} отдал(а) свой голос 🧐{Environment.NewLine}{LocTokens.Details}";

        public string AdminVotingDetailsApproved { get; set; } = $"Задание одобряю всецело 👍";
        public string AdminVotingDetailsDenied { get; set; } = $"Отправляю на доработку по следующей причине:{Environment.NewLine}<b>{LocTokens.Details}</b>";
        public string AdminVotingDetailsOverridden { get; set; } = $"Заменяю текст задания на следующее:{Environment.NewLine}<b>{LocTokens.Details}</b>";

        public string AdminVotingTaskFromRandomTaskRepository { get; set; } = $"<i>NB: Задание было выбрано из пула существующих заданий (по просьбе победителя прошлого раунда)</i>";

        public string AdminVotingTypeReasonForDeclineMessage { get; set; } = $"<b>Пожалуйста, в следующем сообщении опишите причину отказа. Она будет отправлена победителю последнего раунда</b>";
        public string AdminVotingTypeOverridingTaskMessage { get; set; } = $"<b>Пожалуйста, в следующем сообщении ввведите текст задания на замену</b>";


        public string KickstartCommandHandler_Description { get; set; } = "Начать новый раунд с ноги";
        public string AddMidvotePinCommandHandler_Description { get; set; } = "Добавить пин для участия по блату";
        public string KickstartNextRoundTaskPollCommandHandler_Description { get; set; } = "Начать выбор нового задания комьюнити с ноги";
        public string StandbyCommandHandler_Description { get; set; } = "Присыпить бота";
        public string FastForwardCommandHandler_Description { get; set; } = "Промотать время вперед";

        public string DescribeContestEntryCommandHandler_Description { get; set; } = "Добавить описание к отправленной работе";
        public string PostponeCommandHandler_Description { get; set; } = "Запросить отсрочку дедлайна для текущего конкурса";
        public string PostponeCommandHandler_OnlyForKnownUsers { get; set; } = "Запросить отсрочку дедлайна может только человек, отправлявший работы на предыдущие раунды";
        public string CommandHandler_OnlyAvailableInContestState { get; set; } = "Данная команда пока недоступна. Дождитесь начала следующего раунда!";
        public string DescribeContestEntryCommandHandler_SendEntryFirst { get; set; } = "Вначале нужно отправить работу на конкурс (с помощью команды /submit)";
        public string DescribeContestEntryCommandHandler_SubmitGuidelines { get; set; } = "Отправьте описание следующим сообщением (в виде текста!) ⬇️";
        public string DescribeContestEntryCommandHandler_SubmissionFailed { get; set; } = "Допустимы только текстовые описания";
        public string DescribeContestEntryCommandHandler_SubmissionTooLong { get; set; } = "Допустимы только короткие текстовые описания (512 символов максимум) 🧐";
        public string DescribeContestEntryCommandHandler_SubmissionSucceeded { get; set; } = "Спасибо за участие! Ваше описание принято";

        public string SubmitContestEntryCommandHandler_Description { get; set; } = "Отправить работу на текущий конкурс";
        public string SubmitContestEntryCommandHandler_OnlyAvailableInContestState { get; set; } = "Прием работ (пока) закрыт. Дождитесь начала следующего раунда!";
        public string SubmitContestEntryCommandHandler_ProvideMidvotePin { get; set; } = "Следующим сообщением напишите пин для подачи работы 'по блату'";
        public string SubmitContestEntryCommandHandler_InvalidMidvotePin { get; set; } = "Неверный пин :(";
        public string SubmitContestEntryCommandHandler_SubmitGuidelines { get; set; } = "Отправьте работу следующим сообщением в виде аудиофайла ⬇️";

        public string SubmitContestEntryCommandHandler_SubmissionFailed { get; set; } = "Что-то это не похоже на работу 🧐";
        public string SubmitContestEntryCommandHandler_SubmissionFailedNoAudio { get; set; } = "Допустимы только аудиофайлы 🧐 " +
            "Если хочется дать ссылку на youtube, то сделайте это в описании работы";
        public string SubmitContestEntryCommandHandler_SubmissionFailedTooLarge { get; set; } = "Допустимы только аудиофайлы меньше 20 Мб 🧐 (это ограничение Telegram bot API)";
        public string SubmitContestEntryCommandHandler_SubmissionSucceeded { get; set; } = "Спасибо за участие! Ваша работа принята";

        public string TaskSuggestCommandHandler_Description { get; set; } = "Отправить свой вариант задания для следующего челенджа";
        public string TaskSuggestCommandHandler_OnlyAvailableInSuggestionCollectionState { get; set; } = "Прием заданий (пока) закрыт. Дождитесь следующего голосования за задание!";
        public string TaskSuggestCommandHandler_SubmitGuidelines { get; set; } = $@"Отправьте свой вариант задания следующим сообщением (текст от 10 символов). Повторное использование команды обновит уже существующее задание.

Обратите внимание на то, <a href=""{LocTokens.VotingChannelLink}"">какие задания уже предложили</a>, чтобы не повторяться.";
        public string TaskSuggestCommandHandler_SubmitionFailed { get; set; } = "Сообщение должно содержать текст длиной от 10 символов";
        public string TaskSuggestCommandHandler_SubmitionSucceeded { get; set; } = "Спасибо за участие! Ваше задание было принято";
        
        public string NextRoundTaskPollController_SuggestionTemplate { get; set; } = $"<b>Задание от </b>{LocTokens.User}{Environment.NewLine}{LocTokens.Details}";

        public string NextRoundTaskPollController_AnnouncementTemplateMainChannel { get; set; } = 
            $@"Победитель предыдущего раунда, {LocTokens.User}, решил отдать выбор задания комьюнити ПесноПисца.

Процедура будет проходить в два этапа:
1) Прием ботом @nsctheorbot заданий от участников с помощью команды /<code>tasksuggest</code> <b>(команда отсылается в *личке* боту)</b>. Приём заданий закончится в <b>{LocTokens.Deadline}</b>.

2) Голосование за лучшее задание в <a href=""{LocTokens.VotingChannelLink}"">музыкальном архиве</a>. Оно начнётся сразу после закрытия принятия работ.

<a href=""{LocTokens.RulesUrl}"">Все правила</a>";

        public string NextRoundTaskPollController_AnnouncementTemplateVotingChannel { get; set; } = $"🧐 <b>Начался выбор нового задания всем комьюнити!</b> 🧐";

        public string NextRoundTaskPoll_PhasePostponed { get; set; } =
            $"Из-за недостаточной активности (мало вариантов прислали!) фаза сбора заданий продолжается до <b>{LocTokens.Deadline}</b>! 🎉 ";

        public string TaskSuggestionVotingStarted { get; set; } =
            $"Началось голосование за задание для следующего челенджа в Писце! 🎉 " +
            $"Пожалуйста, пройдите в <a href=\"{LocTokens.VotingChannelLink}\">канал для голосования</a> и проставьте всем честные оценки! " +
            $"Голосование будет проходить до <b>{LocTokens.Deadline}</b>";

        public string WeHaveAWinnerTaskSuggestion { get; set; } = $@"В голосовании за следующее задание победил {LocTokens.User} ({LocTokens.VoteCount} голосов), в скором времени начнется следующий раунд челенджей...";
        public string WeHaveWinnersTaskSuggestion { get; set; } = $"В голосовании за следующее задание первое место разделили несколько человек: {LocTokens.Users} ({LocTokens.VoteCount} голосов). " +
                                                    $"С помощью рулетки из них был выбран {LocTokens.User}, чье задание будет использовано в следующем раунде челенджей, который начнется в скором времени...";

        public string VotingStarted { get; set; } =
            $"Началось голосование за лучшие работы в Писце! 🎉 " +
            $"Пожалуйста, пройдите в <a href=\"{LocTokens.VotingChannelLink}\">канал для голосования</a> и проставьте всем честные оценки! " +
            $"Голосование будет проходить до {LocTokens.Deadline}";

        public string VotigResultsTemplate { get; set; } =
            $"<b>Результаты голосования</b>{Environment.NewLine}{LocTokens.Users}";

        public string VotingStatsHeader{ get; set; } =
            $"<code>Статистика участников:</code>";

        public string ThankYouForVote { get; set; } = $"{LocTokens.User}, голос ({LocTokens.VoteCount}) учтен. Статистика может обновляться не сразу.";
        public string VoteUpdated { get; set; } = $"{LocTokens.User}, голос ({LocTokens.VoteCount}) обновлен. Статистика может обновляться не сразу.";

        public string AnonymousAuthor { get; set; } = "🤖";

        public string ContestTaskPreface_Manual { get; set; } = $"Задание от";
        public string ContestTaskPreface_Random { get; set; } = $"Случайно выбраное задание по воле";
        public string ContestTaskPreface_Poll { get; set; } = $"Коллективно было выбрано задание от";
        
        public string Contest_FreshEntryTemplate { get; set; } = $"<b>⬆️ работа </b>{LocTokens.User}{Environment.NewLine}{LocTokens.Details}";

        public string ContestDeadline_EnoughEntriesTemplateFinal { get; set; } = $"Внимание, до конца приема работ на Писец осталось <b>{LocTokens.Time}</b>. После этого начнется этап голосования. Подавайте работы вовремя, ведь бот неумолим! 😊";
        public string ContestDeadline_NotEnoughEntriesTemplateFinal { get; set; } = $"Внимание, до конца приема работ на Писец осталось <b>{LocTokens.Time}</b>, и на данный момент работ на конкурс подали совсем мало. " +
                                                                               $"К сожалению, если так и будет на момент окончания приема работ, бот будет вынужден прекратить челленджи из-за инактива 😐";
        public string ContestDeadline_EnoughEntriesTemplateIntermediate { get; set; } = $"Напоминаем, что до конца приема работ (на данный момент подано {LocTokens.Details} работ) осталось <b>{LocTokens.Time}</b>. После этого начнется этап голосования! 😊";
        public string ContestDeadline_NotEnoughEntriesTemplateIntermediate { get; set; } = $"Напоминаем, что до конца приема работ на Писец осталось <b>{LocTokens.Time}</b>, и на данный момент работ на конкурс подали совсем мало ({LocTokens.Details} шт). " +
                                                                               $"К сожалению, если так и будет на момент окончания приема работ, бот будет вынужден прекратить челленджи из-за инактива 😐";

        public string ContestStartMessageTemplateForVotingChannel { get; set; } = $"🎉 <b>Начался новый ({LocTokens.Details}й) раунд челленджа!</b> 🎉{Environment.NewLine}" +
                                                                                  $"Задание:{Environment.NewLine}{Environment.NewLine}" +
                                                                                  $"{LocTokens.TaskDescription}";

        public string ContestStartMessageTemplateForMainChannelPin { get; set; } = $@"{LocTokens.TaskFromPreface} {LocTokens.User}:

{LocTokens.TaskDescription}

Написать работу и отправить ее боту @nsctheorbot нужно до <b>{LocTokens.Deadline}</b>

<a href=""{LocTokens.RulesUrl}"">Правила/вопросы/что происходит</a>";

        public string Never { get; set; } = "Никогда";
        public string NotAvailable { get; set; } = "н/д";
        public string Now { get; set; } = "Сейчас";
        public string AlmostNothing { get; set; } = "Почти ничего";

        public string DimDays { get; set; } = "дн.";
        public string DimHours { get; set; } = "ч.";
        public string DimMinutes { get; set; } = "мин.";
        public string SomethingWentWrongGeneric { get; set; } = "Что-то пошло не так 😵";
        public string ClientTookTooLongToRespond { get; set; } = "😴";

        public string GeneralReactivationDueToErrorsMessage { get; set; } =
            $"К сожалению, произошла какая-то ошибка и бот был перезапущен. Все нужно будет начать заново. 😒";

        public string VotingWithoutWinnerSituation { get; set; } =
            $"К сожалению, победитель недоступен (заблокировал бота или удалил аккаунт), поэтому вариант `отправить задание на доработку` недоступен. 😒";

    }
}

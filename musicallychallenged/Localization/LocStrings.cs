using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public string YouAreBanned { get; set; } = "Извините, бот недоступен 🧐";
        public string MissingCredentials { get; set; } = "Извините, команда недоступна";

        public string UnknownCommandUsageTemplate { get; set; } = $"Добрый день! Я - бот для челленджей. Воспользуйтесь одной из следующих команд: {Environment.NewLine}{Environment.NewLine}{LocTokens.Details}";

        public string NotEnoughEntriesAnnouncement { get; set; } = "Челленджи приостановлены из-за нехватки участников 😐";
        public string NotEnoughVotesAnnouncement { get; set; } = $"Челленджи приостановлены из-за недостаточной активности участников (максимум голосовавших - {LocTokens.VoteCount} чел.) 😐";

        public string WeHaveAWinner { get; set; } = $@"В раунде Писца победил {LocTokens.User} ({LocTokens.VoteCount} голосов), в данный момент он выбирает задание для следующего раунда...";
        public string WeHaveWinners { get; set; } = $"В раунде Писца первое место разделили несколько человек: {LocTokens.Users} ({LocTokens.VoteCount} голосов). " +
                                                    $"С помощью рулетки из них был выбран {LocTokens.User}, который в данный момент он выбирает задание для следующего раунда...";

        public string CongratsPrivateMessage { get; set; } = "🎉🎉 Поздравляем с победой в раунде Писца! 🎉🎉";
        public string ChooseWiselyPrivateMessage { get; set; } = "Постарайтесь сформулировать задание для следующего раунда с учетом пожеланий Администрации 🧐";

        public string RandomTaskButtonLabel { get; set; } = "Давайте случайное";
        
        public string RandomTaskSelectedMessage { get; set; } = "Для следующего раунда будет использовано случаное задание";
        public string TaskSelectedMessage { get; set; } = "Задание отправлено Администрации";

        public string InvalidTaskMessage { get; set; } = "Извините, это не похоже на задание. Отошлите следующим в сообщении текст задания следующего раунда";
        public string SlackWarningMesage { get; set; } = $"Внимание, время на формулировку задания ограничено ({LocTokens.Time} ч.), " +
                                                         $"по истечению будет автоматически выбран вариант со случайным заданием";

        public string ChooseNextRoundTaskPrivateMessage { get; set; } = "Опишите в следующем сообщении задание для следующего раунда, либо выберите случайное задание " +
                                                                        "(оно будет выбрано из внутреннего пула заданий случайным образом)";
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

        public string AdminVotingSomeoneVotedNotification { get; set; } = $"Администратор {LocTokens.User} отдал свой голос 🧐{Environment.NewLine}{LocTokens.Details}";

        public string AdminVotingDetailsApproved { get; set; } = $"Задание одобряю всецело 👍";
        public string AdminVotingDetailsDenied { get; set; } = $"Отправляю на доработку по следующей причине:{Environment.NewLine}<b>{LocTokens.Details}</b>";
        public string AdminVotingDetailsOverridden { get; set; } = $"Заменяю текст задания на следующее:{Environment.NewLine}<b>{LocTokens.Details}</b>";

        public string AdminVotingTaskFromRandomTaskRepository { get; set; } = $"<i>NB: Задание было выбрано из пула существующих заданий (по просьбе победителя прошлого раунда)</i>";

        public string AdminVotingTypeReasonForDeclineMessage { get; set; } = $"<b>Пожалуйста, в следующем сообщении опишите причину отказа. Она будет отправлена победителю последнего раунда</b>";
        public string AdminVotingTypeOverridingTaskMessage { get; set; } = $"<b>Пожалуйста, в следующем сообщении ввведите текст задания на замену</b>";


        public string KickstartCommandHandler_Description { get; set; } = "Начать новый раунд с ноги";
        public string StandbyCommandHandler_Description { get; set; } = "Присыпить бота";
        public string FastForwardCommandHandler_Description { get; set; } = "Промотать время вперед";

        public string DescribeContestEntryCommandHandler_Description { get; set; } = "Добавить описание к отправленной работе";
        public string DescribeContestEntryCommandHandler_OnlyAvailableInContestState { get; set; } = "Прием работ (пока) закрыт. Дождитесь начала следующего раунда!";
        public string DescribeContestEntryCommandHandler_SendEntryFirst { get; set; } = "Вначале нужно отправить работу на конкурс (с помощью команды /submit)";
        public string DescribeContestEntryCommandHandler_SubmitGuidelines { get; set; } = "Отправьте описание следующим сообщением (в текста!) ⬇️";
        public string DescribeContestEntryCommandHandler_SubmissionFailed { get; set; } = "Допустимы только текстовые описания";
        public string DescribeContestEntryCommandHandler_SubmissionSucceeded { get; set; } = "Спасибо за участие! Ваше описание принято";

        public string SubmitContestEntryCommandHandler_Description { get; set; } = "Отправить работу на текущий конкурс";
        public string SubmitContestEntryCommandHandler_OnlyAvailableInContestState { get; set; } = "Прием работ (пока) закрыт. Дождитесь начала следующего раунда!";
        public string SubmitContestEntryCommandHandler_SubmitGuidelines { get; set; } = "Отправьте работу следующим сообщением в виде аудиофайла либо ссылки ⬇️";

        public string SubmitContestEntryCommandHandler_SubmissionFailed { get; set; } = "Допустимы только аудиофайлы и ссылки (на вашу работу в youtube, например) 🧐";
        public string SubmitContestEntryCommandHandler_SubmissionSucceeded { get; set; } = "Спасибо за участие! Ваша работа принята";

        public string VotingStarted { get; set; } =
            $"Началось голосование за лучшие работы в Писце! 🎉 " +
            $"Пожалуйста, пройдите в <a href=\"{LocTokens.VotingChannelLink}\">канал для голосования</a> и проставьте всем честные оценки! " +
            $"Голосование будет проходить до {LocTokens.Deadline}";

        public string VotigResultsTemplate { get; set; } =
            $"<b>Результаты голосования</b>{Environment.NewLine}{LocTokens.Users}";

        public string VotigStatsHeader{ get; set; } =
            $"<code>Статистика участников:</code>";

        public string ThankYouForVote { get; set; } = $"Спасибо, ваш голос ({LocTokens.VoteCount}) учтен. Статистика может обновляться не сразу.";
        public string VoteRemoved { get; set; } = $"Ваш голос ({LocTokens.VoteCount}) отменен. Статистика может обновляться не сразу.";

        public string AnonymousAuthor { get; set; } = "🤖";

        public string Contest_FreshEntryTemplate { get; set; } = $"⬆️ Работа от {LocTokens.User} ⬆️{LocTokens.Details}";

        public string ContestDeadline_EnoughEntriesTemplate { get; set; } = $"Внимание, до конца приема работ на Писец осталось <b>{LocTokens.Time}</b>. После этого начнется этап голосования. Подавайте работы вовремя, ведь бот неумолим! 😊";
        public string ContestDeadline_NotEnoughEntriesTemplate { get; set; } = $"Внимание, до конца приема работ на Писец осталось <b>{LocTokens.Time}</b>, и на данный момент работ на конкурс подали совсем мало. " +
                                                                               $"К сожалению, если так и будет на момент окончания приема работ, бот будет вынужден прекратить челленджи из-за инактива 😐";

        public string ContestStartMessageTemplateForVotingChannel { get; set; } = $"🎉 <b>Начался новый ({LocTokens.Details}й) раунд челленджа!</b> 🎉";

        public string ContestStartMessageTemplateForMainChannelPin { get; set; } = $@"Задание от {LocTokens.User}:

{LocTokens.TaskDescription}

Написать работу и отправить ее боту @nsctheorbot с помощью команды /<code>submit</code> <b>(команда отсылается в *личке* боту)</b> нужно до <b>{LocTokens.Deadline}</b>
Текстовое описание к работе (по желанию) можно отправить командой /<code>describe</code>

Если вы залили видео на ютуб, то, пожалуйста, вставьте ссылку на оригинал в описании вашего видео.

Голосование за лучшую работу начнется по окончанию принятия работ. Победитель определит задание для следующего челленджа и должен будет написать его администрации.

Если победивших будет 2 или больше, тот, чье задание будет взято для следующего челленджа определит рандомайзер. Если победитель не объявится, будет использовано случайное задание из пула с идеями.

Администрация имеет последнее слово в вопросе о допущении задания для челленджа и может как изменить его, так и отвергнуть.

Если у вас будут какие-то идеи для новый челленджей, пишите в чат. Администрация перешлет их боту.

Оставайтесь маленькими музыкашками 😊

Не забывайте о нашем <a href=""{LocTokens.VotingChannelLink}"">музыкальном архиве</a>
";

        public string Never { get; set; } = "Никогда";
        public string NotAvailable { get; set; } = "н/д";
        public string Now { get; set; } = "Сейчас";

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

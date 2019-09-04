BEGIN TRANSACTION;
DROP TABLE IF EXISTS "ActiveContestEntry";
CREATE TABLE IF NOT EXISTS "ActiveContestEntry" (
	"Id"	INTEGER,
	"AuthorUserId"	INTEGER NOT NULL,
	"ChallengeRoundNumber"	INTEGER NOT NULL,
	"Timestamp"	TEXT NOT NULL,
	"ConsolidatedVoteCount"	INTEGER,
	"ContainerChatId"	INTEGER NOT NULL,
	"ContainerMesssageId"	INTEGER NOT NULL,
	"ForwardedPayloadMessageId"	INTEGER NOT NULL,
	"Description"	TEXT,
	PRIMARY KEY("Id")
);
DROP TABLE IF EXISTS "SystemState";
CREATE TABLE IF NOT EXISTS "SystemState" (
	"Id"	INTEGER,
	"State"	INTEGER NOT NULL,
	"CurrentChallengeRoundNumber"	INTEGER NOT NULL,
	"Timestamp"	TEXT NOT NULL,
	"VotingChannelId"	INTEGER,
	"MainChannelId"	INTEGER,
	"CurrentWinnerId"	INTEGER,
	"CurrentTaskTemplate"	TEXT,
	"ContestDurationDays"	float,
	"VotingDurationDays"	float,
	"NextDeadlineUTC"	TEXT NOT NULL,
	"PayloadJSON"	TEXT,
	"CurrentTaskMessagelId"	INTEGER,
	"CurrentVotingStatsMessageId"	INTEGER,
	PRIMARY KEY("Id")
);
DROP TABLE IF EXISTS "Vote";
CREATE TABLE IF NOT EXISTS "Vote" (
	"Id"	INTEGER,
	"UserId"	INTEGER NOT NULL,
	"ContestEntryId"	INTEGER NOT NULL,
	"Timestamp"	TEXT NOT NULL,
	"Value"	INTEGER NOT NULL,
	PRIMARY KEY("Id")
);
DROP TABLE IF EXISTS "User";
CREATE TABLE IF NOT EXISTS "User" (
	"Id"	INTEGER,
	"Username"	VARCHAR,
	"Name"	VARCHAR,
	"ChatId"	INTEGER,
	"LastActivityUTC"	TEXT NOT NULL,
	"ReceivesNotificatons"	bit NOT NULL,
	"State"	INTEGER NOT NULL,
	"Credentials"	INTEGER NOT NULL,
	PRIMARY KEY("Id")
);
DROP TABLE IF EXISTS "ActiveChat";
CREATE TABLE IF NOT EXISTS "ActiveChat" (
	"Id"	INTEGER,
	"Name"	TEXT,
	"Timestamp"	DATETIME NOT NULL,
	PRIMARY KEY("Id")
);

CREATE TABLE "RandomTask" (
	"Id"	INTEGER UNIQUE,
	"Description"	TEXT,
	"LastUsed"	TEXT,
	"UsedCount"	INTEGER NOT NULL DEFAULT 0,
	"Priority"	INTEGER NOT NULL DEFAULT 0,
	"OriginalAuthorName"	TEXT,
	PRIMARY KEY("Id")
);

COMMIT;

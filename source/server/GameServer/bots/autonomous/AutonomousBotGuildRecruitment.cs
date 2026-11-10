using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>How open a bot guild is to recruiting the player (Living Server backlog 1.2 / Erenshor-style gate).</summary>
public enum BotGuildRecruitmentPolicy
{
    Open = 0,
    Friendly = 1,
    Application = 2,
    Closed = 3,
}

/// <summary>
/// Pure recruitment-gate rules: policy from charter/ordinal, standing from bot memory of the player,
/// and whether a guild may invite. Grouping is never refused — only guild invites (docs/LIVING-SERVER-BACKLOG.md).
/// Behind <see cref="Properties.BOT_GUILD_RECRUIT_GATE"/>.
/// </summary>
public static class AutonomousBotGuildRecruitment
{
    /// <summary>Friendly: at least one good group/rez (or a friendly member).</summary>
    public const int FriendlyStanding = 1;

    /// <summary>Application: two groups' worth of standing, or a member vouch.</summary>
    public const int ApplicationStanding = 2;

    /// <summary>Closed: strong relationship only (friendly mood and several deeds).</summary>
    public const int ClosedStanding = 3;

    /// <summary>
    /// Starting policy mix by charter (backlog 1.2): Social/Leveling lean Open, Crafting Friendly,
    /// RvR/Raiding lean Application or Closed. Ordinal varies a little so every realm gets a mix.
    /// </summary>
    public static BotGuildRecruitmentPolicy PolicyFor(BotGuildCharter charter, int ordinal)
    {
        int n = Math.Abs(ordinal);
        return charter switch
        {
            BotGuildCharter.Social => n % 5 == 0 ? BotGuildRecruitmentPolicy.Friendly : BotGuildRecruitmentPolicy.Open,
            BotGuildCharter.Leveling => n % 4 == 0 ? BotGuildRecruitmentPolicy.Friendly : BotGuildRecruitmentPolicy.Open,
            BotGuildCharter.Crafting => BotGuildRecruitmentPolicy.Friendly,
            BotGuildCharter.Rvr => n % 3 == 0 ? BotGuildRecruitmentPolicy.Closed : BotGuildRecruitmentPolicy.Application,
            BotGuildCharter.Raiding => n % 2 == 0 ? BotGuildRecruitmentPolicy.Closed : BotGuildRecruitmentPolicy.Application,
            _ => BotGuildRecruitmentPolicy.Open,
        };
    }

    /// <summary>Relationship score from memory counts (groups, rezzes, insults).</summary>
    public static int StandingScore(int grouped, int rezzed, int abused) =>
        Math.Max(0, grouped + rezzed * 2 - abused * 2);

    public static int StandingScore(OfflineBotMemoryRecord row) =>
        row == null ? 0 : StandingScore(row.Grouped, row.Rezzed, row.Abused);

    /// <summary>
    /// Whether this policy lets the player in, given best standing/mood toward any member and how many
    /// members like the player (Friendly) or vouch (Application).
    /// </summary>
    public static bool AllowsInvite(BotGuildRecruitmentPolicy policy, int standing, string mood,
        int friendlyMembers, int vouchers)
    {
        mood = (mood ?? "neutral").Trim().ToLowerInvariant();
        return policy switch
        {
            BotGuildRecruitmentPolicy.Open => true,
            BotGuildRecruitmentPolicy.Friendly =>
                mood == "friendly" || standing >= FriendlyStanding || friendlyMembers >= 1,
            BotGuildRecruitmentPolicy.Application =>
                standing >= ApplicationStanding || vouchers >= 1,
            BotGuildRecruitmentPolicy.Closed =>
                mood == "friendly" && standing >= ClosedStanding,
            _ => true,
        };
    }

    /// <summary>Closed guilds never advertise; Friendly only whispers after standing; Open/Application may /faction.</summary>
    public static bool MayAdvertiseInFaction(BotGuildRecruitmentPolicy policy) =>
        policy is BotGuildRecruitmentPolicy.Open or BotGuildRecruitmentPolicy.Application;

    public static bool GateEnabled => Properties.BOT_GUILD_RECRUIT_GATE;

    /// <summary>Standing snapshot of a player against one bot guild's online members (memory rows).</summary>
    public static (int Standing, string Mood, int FriendlyMembers, int Vouchers) StandingWithGuild(
        GamePlayer player, IEnumerable<GameBot> members)
    {
        int best = 0;
        string mood = "neutral";
        int friendly = 0;
        int vouchers = 0;
        if (player == null || members == null)
            return (0, mood, 0, 0);

        foreach (GameBot bot in members)
        {
            if (!AutonomousLivingServer.TryRecall(bot, player, out OfflineBotMemoryRecord row))
                continue;
            int score = StandingScore(row);
            string rowMood = AutonomousLivingServer.MoodOf(row);
            if (rowMood == "friendly")
            {
                friendly++;
                if (score >= FriendlyStanding)
                    vouchers++;
            }
            if (score > best || (score == best && rowMood == "friendly" && mood != "friendly"))
            {
                best = score;
                mood = rowMood;
            }
        }

        return (best, mood, friendly, vouchers);
    }

    /// <summary>True when the gate is off, or the guild's policy accepts this player.</summary>
    public static bool GuildMayInvitePlayer(GamePlayer player, AutonomousBotRealmGuilds.BotGuildInfo info,
        IEnumerable<GameBot> members)
    {
        if (!GateEnabled || info == null)
            return true;
        BotGuildRecruitmentPolicy policy = PolicyFor(info.Charter, info.Ordinal);
        (int standing, string mood, int friendly, int vouchers) = StandingWithGuild(player, members);
        return AllowsInvite(policy, standing, mood, friendly, vouchers);
    }

    public static string GateRefusalHint(BotGuildRecruitmentPolicy policy) => policy switch
    {
        BotGuildRecruitmentPolicy.Friendly => "group with us a bit first, then ask again",
        BotGuildRecruitmentPolicy.Application => "we need a vouch or more time with us before inviting",
        BotGuildRecruitmentPolicy.Closed => "we're closed to new blood unless we know you well",
        _ => "not recruiting you right now",
    };
}

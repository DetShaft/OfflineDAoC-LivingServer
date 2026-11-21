using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Logging;
using Activity = DOL.GS.AutonomousBattlegroundActivity.Activity;

namespace DOL.GS;

/// <summary>
/// Battleground parties (owner, 2026-11-21): gamebots on a battleground tour that share an activity form real groups.
/// Keep fighters near each other group up in threes to eights, half the hunters hunt in pairs or threes, and grinders on
/// the same camp group up (two to four). The leader keeps its own plan; the others follow it (grinders work the shared
/// camp side by side). A party is not a coordinator session (AutonomousBotGroupCoordinator.Pulse leaves it alone), so
/// the battleground tour keeps running it. Members leave when their own phase changes, they leave the battleground or
/// the tour, or the party drops under two.
/// </summary>
public static class AutonomousBattlegroundParties
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundParties));

    public const long PassMilliseconds = 10_000;

    private sealed record Party(ushort Region, eRealm Realm, Activity Activity, string Camp, long Formed);

    private static readonly ConcurrentDictionary<Group, Party> Parties = new();
    private static readonly object Sync = new();
    private static long _nextPass;

    public static bool IsParty(Group group) => group != null && Parties.ContainsKey(group);

    /// <summary>The living leader of the bot's battleground party, or null when it is in none.</summary>
    public static GameBot LeaderOf(GameBot bot) =>
        bot?.Group is { } group && Parties.ContainsKey(group) ? group.LivingLeader as GameBot : null;

    public static int Count => Parties.Count;

    /// <summary>One bounded pass every <see cref="PassMilliseconds"/>, server-wide (called from bots' battleground turns).</summary>
    public static void Pulse(long now)
    {
        if (now < _nextPass) return;
        lock (Sync)
        {
            if (now < _nextPass) return;
            _nextPass = now + PassMilliseconds;
            try
            {
                Prune();
                Form(now);
            }
            catch (Exception exception)
            {
                Log.Error("Battleground party pass failed", exception);
            }
        }
    }

    private static bool OnTour(GameBot bot, ushort region) =>
        bot != null && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && bot.IsAutonomousWorldBot &&
        !bot.IsTemporaryGroupHelper && !bot.IsPlayerLedGroup && bot.CurrentRegionID == region &&
        AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground);

    private static bool Fits(GameBot bot, Party party)
    {
        long key = AutonomousBattlegroundActivity.KeyOf(bot);
        if (AutonomousBattlegroundActivity.ActivityOf(key) != party.Activity) return false;
        return party.Activity != Activity.Grind ||
               string.Equals(AutonomousBattlegroundActivity.CampOf(key), party.Camp, StringComparison.Ordinal);
    }

    private static void Prune()
    {
        foreach ((Group group, Party party) in Parties.ToArray())
        {
            List<GameLiving> members = group.GetMembersInTheGroup();
            if (members.Any(member => member is GamePlayer) || members.Count < 2)
            {
                // A player joined (their invite wins) or the group fell apart elsewhere: not ours any more.
                Parties.TryRemove(group, out _);
                if (members.Count < 2 && members.Count > 0 && !members.Any(member => member is GamePlayer)) group.DisbandGroup();
                continue;
            }
            foreach (GameBot member in members.OfType<GameBot>().ToArray())
            {
                // A dead member keeps its place (it comes back from the portal keep); one that left the tour, the
                // battleground or the party's activity leaves the group.
                bool keeps = !member.IsAlive && member.CurrentRegionID == party.Region ||
                             OnTour(member, party.Region) && Fits(member, party);
                if (!keeps) group.RemoveMember(member);
            }
            if (group.MemberCount < 2)
            {
                Parties.TryRemove(group, out _);
                if (group.MemberCount > 0) group.DisbandGroup();
                Log.Info($"BG_PARTY_ENDED region={party.Region} realm={GlobalConstants.RealmToName(party.Realm)} activity={party.Activity}");
            }
        }
    }

    private static void Form(long now)
    {
        var candidates = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.Group == null && BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID) &&
                          OnTour(bot, bot.CurrentRegionID) && !bot.InCombat && !bot.IsOnStableMasterRoute &&
                          (bot.Brain as BotBrain)?.HasAggro != true)
            .Select(bot => (Bot: bot, Key: AutonomousBattlegroundActivity.KeyOf(bot)))
            .Select(entry => (entry.Bot, entry.Key, Activity: AutonomousBattlegroundActivity.ActivityOf(entry.Key),
                Camp: AutonomousBattlegroundActivity.CampOf(entry.Key)))
            .Where(entry => entry.Activity.HasValue)
            .Where(entry => entry.Activity != Activity.OutskirtHunt || AutonomousBattlegroundActivity.HuntsInParty(entry.Key))
            .Where(entry => entry.Activity != Activity.Grind || entry.Camp != null)
            .GroupBy(entry => (entry.Bot.CurrentRegionID, entry.Bot.Realm, entry.Activity.Value, entry.Camp));
        int formed = 0;
        foreach (var cohort in candidates)
        {
            // Lowest key leads, so a cohort forms the same way from pass to pass.
            var bots = cohort.OrderBy(entry => entry.Key).Select(entry => entry.Bot).ToArray();
            var positions = bots.Select(bot => new Vector2(bot.X, bot.Y)).ToArray();
            foreach (int[] indexes in AutonomousBattlegroundActivity.FormParties(positions, cohort.Key.Item3))
            {
                if (formed >= 12) return;
                GameBot[] members = indexes.Select(i => bots[i]).ToArray();
                if (TryForm(members, new Party(cohort.Key.CurrentRegionID, cohort.Key.Realm, cohort.Key.Item3, cohort.Key.Camp, now)))
                    formed++;
            }
        }
    }

    private static bool TryForm(GameBot[] members, Party party)
    {
        GameBot leader = members[0];
        var group = new Group(leader);
        GroupMgr.AddGroup(group);
        if (!group.AddMember(leader))
        {
            GroupMgr.RemoveGroup(group);
            return false;
        }
        // Registered before the others join, so the coordinator's pulse never gives it a session.
        Parties[group] = party;
        foreach (GameBot member in members.Skip(1)) group.AddMember(member);
        if (group.MemberCount < 2)
        {
            Parties.TryRemove(group, out _);
            group.DisbandGroup();
            return false;
        }
        Log.Info($"BG_PARTY_FORMED region={party.Region} realm={GlobalConstants.RealmToName(party.Realm)} activity={party.Activity} " +
                 $"size={group.MemberCount} leader=\"{leader.Name}\" camp=\"{party.Camp ?? "-"}\" " +
                 $"members=\"{string.Join(",", group.GetMembersInTheGroup().Select(member => member.Name))}\"");
        return true;
    }
}

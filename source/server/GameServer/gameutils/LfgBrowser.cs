using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Filters for /lfg list and optional narrowing of the find-group window.</summary>
    public sealed class LfgListFilters
    {
        public string ClassContains { get; init; }
        public int? MinLevel { get; init; }
        public int? MaxLevel { get; init; }
        public string ZoneContains { get; init; }

        public bool Matches(LfgWindowEntry entry)
        {
            if (entry == null)
                return false;
            if (!string.IsNullOrWhiteSpace(ClassContains) &&
                (entry.ClassName == null || entry.ClassName.IndexOf(ClassContains, StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (MinLevel.HasValue && entry.Level < MinLevel.Value)
                return false;
            if (MaxLevel.HasValue && entry.Level > MaxLevel.Value)
                return false;
            if (!string.IsNullOrWhiteSpace(ZoneContains) &&
                (entry.ZoneDescription == null ||
                 entry.ZoneDescription.IndexOf(ZoneContains, StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            return true;
        }

        /// <summary>
        /// Parse trailing /lfg list args: optional class, optional min or min-max, optional zone words.
        /// Examples: Healer | Healer 1-20 | 40-50 Gotar | Warrior 30 Gotar Hills
        /// </summary>
        public static LfgListFilters Parse(string[] args, int startIndex)
        {
            if (args == null || startIndex >= args.Length)
                return new LfgListFilters();

            string classContains = null;
            int? min = null;
            int? max = null;
            int i = startIndex;

            if (i < args.Length && !LooksLikeLevel(args[i]))
            {
                classContains = args[i];
                i++;
            }

            if (i < args.Length && TryParseLevelBand(args[i], out int lo, out int hi))
            {
                min = lo;
                max = hi;
                i++;
            }

            string zone = i < args.Length ? string.Join(" ", args, i, args.Length - i) : null;
            return new LfgListFilters
            {
                ClassContains = classContains,
                MinLevel = min,
                MaxLevel = max,
                ZoneContains = string.IsNullOrWhiteSpace(zone) ? null : zone.Trim(),
            };
        }

        private static bool LooksLikeLevel(string token) => TryParseLevelBand(token, out _, out _);

        private static bool TryParseLevelBand(string token, out int min, out int max)
        {
            min = 0;
            max = 0;
            if (string.IsNullOrWhiteSpace(token))
                return false;
            string t = token.Trim();
            int dash = t.IndexOf('-');
            if (dash > 0)
            {
                if (!int.TryParse(t[..dash], out min) || !int.TryParse(t[(dash + 1)..], out max))
                    return false;
                if (min > max)
                    (min, max) = (max, min);
                return min >= 1 && max <= 50;
            }
            if (!int.TryParse(t, out min))
                return false;
            max = min;
            return min >= 1 && min <= 50;
        }
    }

    /// <summary>
    /// Builds Looking For Group listings: flagged players plus solo advertising / seeking gamebots.
    /// </summary>
    public static class LfgBrowser
    {
        public static IList<LfgWindowEntry> BuildFor(GamePlayer viewer, LfgListFilters filters = null)
        {
            filters ??= new LfgListFilters();
            var list = new List<LfgWindowEntry>();
            if (viewer == null)
                return list;

            foreach (GamePlayer player in GroupMgr.LookingForGroupPlayers() ?? Array.Empty<GamePlayer>())
            {
                if (player == null || player == viewer)
                    continue;
                if (!GameServer.ServerRules.IsAllowedToGroup(viewer, player, true))
                    continue;
                LfgWindowEntry entry = LfgWindowEntry.FromPlayer(player);
                if (filters.Matches(entry))
                    list.Add(entry);
            }

            foreach (Group group in GroupMgr.ListGroupByStatus(0x00) ?? Array.Empty<Group>())
            {
                GamePlayer leader = group?.Leader;
                if (leader == null || leader == viewer)
                    continue;
                if (!GameServer.ServerRules.IsAllowedToGroup(group.Leader, viewer, true))
                    continue;
                LfgWindowEntry entry = LfgWindowEntry.FromPlayer(leader);
                // FromPlayer sets IsGrouped from leader.Group
                if (filters.Matches(entry))
                    list.Add(entry);
            }

            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (!ShouldListBot(viewer, bot))
                    continue;
                LfgWindowEntry entry = LfgWindowEntry.FromBot(bot);
                if (filters.Matches(entry))
                    list.Add(entry);
            }

            return list
                .OrderBy(e => e.IsGrouped ? 0 : 1)
                .ThenBy(e => e.Level)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Solo same-realm free bots that are advertising LFG or awaiting matchmaking / RvR group seek.</summary>
        public static bool ShouldListBot(GamePlayer viewer, GameBot bot)
        {
            if (viewer == null || bot?.IsAutonomousWorldBot != true)
                return false;
            if (bot.ObjectState != GameObject.eObjectState.Active)
                return false;
            if (bot.Realm != viewer.Realm)
                return false;
            if (bot.Group != null && bot.Group.MemberCount > 1)
                return false;
            // #55: a duty (stable route, realm raid, siege, relic, and the rest) cannot come to an invite, so it is not offered.
            if (BotGroupInvite.DutyOf(bot) != BotGroupInvite.Duty.None)
                return false;
            if (BotLfgAds.IsAdvertising(bot))
                return true;
            // Also list solos the Living Server treats as seeking a group (matchmaking / RvR).
            return AutonomousObjectiveAssignments.IsAwaitingGroupMatchmaking(bot);
        }

        public static IList<string> FormatTextLines(IList<LfgWindowEntry> entries, int maxLines = 40)
        {
            var lines = new List<string>();
            if (entries == null || entries.Count == 0)
            {
                lines.Add("No one matching is looking for a group.");
                lines.Add("Hint: /invite Name  or  /send Name hello");
                return lines;
            }

            lines.Add($"Looking for group ({entries.Count}):");
            int shown = 0;
            foreach (LfgWindowEntry e in entries)
            {
                if (shown >= maxLines)
                {
                    lines.Add($"... and {entries.Count - shown} more.");
                    break;
                }
                string tag = e.IsBot ? " [BOT]" : string.Empty;
                string role = e.IsGrouped ? "LFM" : "LFG";
                string zone = string.IsNullOrWhiteSpace(e.ZoneDescription) ? "?" : e.ZoneDescription;
                lines.Add($"{role} {e.Name}{tag}  L{e.Level} {e.ClassName}  {zone}");
                shown++;
            }
            lines.Add("Invite with /invite Name — tell with /send Name …");
            return lines;
        }
    }
}

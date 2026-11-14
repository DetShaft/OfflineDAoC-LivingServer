using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// Round-robin group drops and the /look list a player chooses from.
    /// A player's share stays on the corpse. A friended companion or an autonomous
    /// gamebot takes its turn into its own bags. A /spawn helper is not in the turn.
    /// </summary>
    public static class GroupLoot
    {
        public static long[] SplitCopper(long total, int count)
        {
            long[] shares = new long[Math.Max(0, count)];
            if (count <= 0 || total <= 0)
                return shares;

            long quotient = total / count;
            long remainder = total % count;
            for (int index = 0; index < count; index++)
                shares[index] = quotient + (index < remainder ? 1 : 0);
            return shares;
        }

        public static string Label(GameStaticItemTimed drop)
        {
            if (drop is GameMoney money)
                return Money.GetString(money.Value);
            if (drop is WorldInventoryItem item && item.ReservedFor != null)
                return $"{drop.Name} (your share)";
            return drop?.Name ?? string.Empty;
        }

        public static List<GameStaticItemTimed> LookList(GamePlayer player, IEnumerable<GameStaticItem> nearby)
        {
            List<GameStaticItemTimed> drops = new();
            if (player == null || nearby == null)
                return drops;

            foreach (GameStaticItem entry in nearby)
            {
                if (entry is not GameStaticItemTimed timed || timed.ObjectState == GameObject.eObjectState.Deleted)
                    continue;
                if (timed is WorldInventoryItem item)
                {
                    if (item.Item == null || !item.Item.IsPickable)
                        continue;
                    if (item.ReservedFor != null && item.ReservedFor != player)
                        continue;
                }
                else if (timed is not GameMoney)
                    continue;

                if (timed.Owners.Count > 0 && !timed.IsOwner(player))
                    continue;
                drops.Add(timed);
            }

            drops.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return drops;
        }

        public static bool TryChoose(IReadOnlyList<GameStaticItemTimed> drops, string request, out GameStaticItemTimed chosen, out string error)
        {
            chosen = null;
            error = null;
            if (drops == null || drops.Count == 0)
            {
                error = "Nothing here is yours to look through.";
                return false;
            }

            request = request?.Trim() ?? string.Empty;
            if (request.Length == 0)
            {
                error = "Say which drop, for example /look take 1.";
                return false;
            }

            if (int.TryParse(request, out int number))
            {
                if (number < 1 || number > drops.Count)
                {
                    error = $"There is no drop {number}. /look shows {drops.Count}.";
                    return false;
                }

                chosen = drops[number - 1];
                return true;
            }

            List<GameStaticItemTimed> matches = new();
            foreach (GameStaticItemTimed drop in drops)
            {
                if (drop.Name != null && drop.Name.Contains(request, StringComparison.OrdinalIgnoreCase))
                    matches.Add(drop);
            }

            if (matches.Count == 1)
            {
                chosen = matches[0];
                return true;
            }

            error = matches.Count == 0
                ? $"Nothing here is named {request}."
                : $"More than one drop matches {request}. Use the number from /look.";
            return false;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    public enum eLootChoice
    {
        Waiting,
        Pass,
        Greed,
        Need
    }

    public sealed class LootRollEntry
    {
        public GameLiving Member { get; init; }
        public eLootChoice Choice { get; set; }
        /// <summary>1-100 once the roll is decided. 0 for a member who passed.</summary>
        public int Roll { get; set; }
        /// <summary>How well the drop's bonuses suit this member's class (LootClassFit). Among Need rolls the best fit wins.</summary>
        public double Fit { get; set; }
    }

    /// <summary>One piece of gear the group is rolling for. The drop lies on the ground until the roll ends.</summary>
    public sealed class LootRoll
    {
        public int Id { get; init; }
        public Group Group { get; init; }
        public WorldInventoryItem Drop { get; init; }
        public List<LootRollEntry> Entries { get; } = new();
        public Lock Sync { get; } = new();
        public bool Closed { get; set; }
        public ECSGameTimer Timer { get; set; }
        /// <summary>Who received the drop, or whose it is on the ground. Null when everyone passed.</summary>
        public GameLiving Winner { get; set; }
    }

    /// <summary>
    /// Need or greed rolls for gear that drops in a group with a player in it.
    /// A bot answers at once: Need when the piece is an upgrade it can wear and its bonuses suit the bot's class,
    /// Greed otherwise, Pass with a full bag.
    /// A player answers with /loot need, /loot greed or /loot pass (the new client's loot window sends these).
    /// Need beats Greed. Among Need rolls the member whose class gets the most from the bonuses wins; members
    /// whose fit is close (LootClassFit.TieShare) and Greed rolls are decided by the highest 1-100 roll.
    /// </summary>
    public static class GroupLootRolls
    {
        public const int SecondsToChoose = 60;
        /// <summary>The start of every roll line. The new client's loot window reads lines that begin with it.</summary>
        public const string Prefix = "Loot roll #";

        private static readonly ConcurrentDictionary<int, LootRoll> _open = new();
        private static int _nextId;

        /// <summary>Starts the timeout. Tests replace it so no game timer is needed.</summary>
        public static Action<LootRoll> Schedule { get; set; } = StartTimer;

        /// <summary>The 1-100 die. Tests replace it.</summary>
        public static Func<int> Die { get; set; } = static () => Util.Random(1, 100);

        public static List<LootRoll> Open => _open.Values.ToList();

        /// <summary>Gear is rolled for: one weapon, armor piece, shield, instrument or jewel. Stacks and trophies are not.</summary>
        public static bool IsRollWorthy(DbInventoryItem item)
        {
            if (item?.Template == null || item.IsStackable || !item.IsPickable)
                return false;
            if ((eObjectType)item.Object_Type == eObjectType.GenericItem)
                return false;
            return item.Item_Type is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable;
        }

        public static eLootChoice BotChoice(GameBot bot, DbInventoryItem item)
        {
            if (bot?.Inventory == null || item == null ||
                bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid)
                return eLootChoice.Pass;

            // Only a gamebot wears what it loots. The check is the one its own loot, merchant
            // and Realm Exchange decisions use: its class can use the piece, the weapon fits
            // its spec, and it is better than what it wears in that slot. On top of that, the
            // piece's bonuses must suit the class: a caster does not Need a staff made for a
            // Friar (+Staff, Strength) just because it is a better staff than its own.
            try
            {
                if (bot.IsAutonomousWorldBot && AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, item, out _) &&
                    LootClassFit.Of(bot, item).SuitsClass)
                    return eLootChoice.Need;
            }
            catch (Exception)
            {
                // An appraisal that fails must not stop the roll. The bot still rolls for coin.
            }

            return eLootChoice.Greed;
        }

        /// <summary>
        /// Opens a roll for this drop. False when the drop is not gear or no player is in the turn,
        /// in which case the group deals it round-robin as before.
        /// </summary>
        public static bool TryStart(Group group, WorldInventoryItem drop, IReadOnlyList<GameLiving> eligible)
        {
            if (group == null || drop == null || eligible == null || drop.PendingRoll != null || !IsRollWorthy(drop.Item))
                return false;
            if (!eligible.Any(static member => member is GamePlayer))
                return false;

            LootRoll roll = new()
            {
                Id = Interlocked.Increment(ref _nextId),
                Group = group,
                Drop = drop
            };

            foreach (GameLiving member in eligible)
            {
                if (member is GamePlayer)
                    roll.Entries.Add(new LootRollEntry { Member = member, Choice = eLootChoice.Waiting, Fit = FitOf(member, drop.Item) });
                else if (member is GameBot bot)
                    roll.Entries.Add(new LootRollEntry { Member = bot, Choice = BotChoice(bot, drop.Item), Fit = FitOf(bot, drop.Item) });
            }

            drop.PendingRoll = roll;
            _open[roll.Id] = roll;

            string line = null;
            foreach (LootRollEntry entry in roll.Entries)
            {
                if (entry.Member is not GamePlayer player || player.Client == null)
                    continue;
                line ??= StartLine(roll);
                player.Out.SendMessage(line, eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
                player.Out.SendMessage($"Choose with /loot need {roll.Id}, /loot greed {roll.Id} or /loot pass {roll.Id}.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }

            Schedule?.Invoke(roll);
            return true;
        }

        private static double FitOf(GameLiving member, DbInventoryItem item)
        {
            try
            {
                return LootClassFit.Of(member, item).Score;
            }
            catch (Exception)
            {
                // A fit that cannot be judged leaves the die to decide.
                return 0;
            }
        }

        /// <summary>
        /// "Loot roll #7: cloth pants | Level 10 cloth legs, AF 20, 95% quality | 60 seconds | Need: Brokk | Greed: 2".
        /// </summary>
        public static string StartLine(LootRoll roll)
        {
            List<string> needers = roll.Entries.Where(static e => e.Choice == eLootChoice.Need).Select(static e => e.Member.Name).ToList();
            int greeders = roll.Entries.Count(static e => e.Choice == eLootChoice.Greed);
            return $"{Prefix}{roll.Id}: {Clean(roll.Drop.Name)} | {Clean(Describe(roll.Drop.Item))} | {SecondsToChoose} seconds | " +
                   $"Need: {(needers.Count == 0 ? "nobody yet" : string.Join(", ", needers))} | Greed: {greeders}";
        }

        private static string Clean(string text) => (text ?? string.Empty).Replace('|', '/');

        /// <summary>A one-line summary of a piece of gear, enough to choose Need, Greed or Pass.</summary>
        public static string Describe(DbInventoryItem item)
        {
            if (item == null)
                return string.Empty;

            List<string> parts = new();
            eObjectType type = (eObjectType)item.Object_Type;
            string kind = GlobalConstants.ObjectTypeToName(item.Object_Type) ?? string.Empty;
            int bracket = kind.IndexOf(" (", StringComparison.Ordinal);
            if (bracket > 0)
                kind = kind[..bracket];
            string slot = GlobalConstants.SlotToName(item.Item_Type) ?? string.Empty;
            parts.Add($"Level {item.Level} {kind} {slot}".Replace("  ", " ").Trim());

            if (type is >= eObjectType._FirstWeapon and <= eObjectType._LastWeapon)
            {
                parts.Add($"{item.DPS_AF / 10.0:0.0} DPS");
                parts.Add($"{item.SPD_ABS / 10.0:0.0} speed");
            }
            else if (type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor)
            {
                parts.Add($"AF {item.DPS_AF}");
                if (item.SPD_ABS > 0)
                    parts.Add($"{item.SPD_ABS}% absorb");
            }

            if (item.Quality > 0)
                parts.Add($"{item.Quality}% quality");

            (int amount, int property)[] bonuses =
            {
                (item.Bonus1, item.Bonus1Type), (item.Bonus2, item.Bonus2Type), (item.Bonus3, item.Bonus3Type),
                (item.Bonus4, item.Bonus4Type), (item.Bonus5, item.Bonus5Type), (item.Bonus6, item.Bonus6Type),
                (item.Bonus7, item.Bonus7Type), (item.Bonus8, item.Bonus8Type), (item.Bonus9, item.Bonus9Type),
                (item.Bonus10, item.Bonus10Type), (item.ExtraBonus, item.ExtraBonusType)
            };
            foreach ((int amount, int property) in bonuses)
            {
                if (amount == 0 || property == 0)
                    continue;
                parts.Add($"{SkillBase.GetPropertyName((eProperty)property)} {(amount > 0 ? "+" : string.Empty)}{amount}");
            }

            return string.Join(", ", parts);
        }

        public static List<LootRoll> WaitingOn(GamePlayer player)
        {
            List<LootRoll> rolls = new();
            if (player == null)
                return rolls;

            foreach (LootRoll roll in _open.Values)
            {
                lock (roll.Sync)
                {
                    if (!roll.Closed && roll.Entries.Any(e => e.Member == player && e.Choice == eLootChoice.Waiting))
                        rolls.Add(roll);
                }
            }

            rolls.Sort(static (left, right) => left.Id.CompareTo(right.Id));
            return rolls;
        }

        /// <summary>
        /// Records a player's choice. An id of 0 means the oldest roll still waiting on this player.
        /// The roll ends as soon as every player in it has answered.
        /// </summary>
        public static bool Answer(GamePlayer player, int id, eLootChoice choice, out string error)
        {
            error = null;
            if (player == null || choice is eLootChoice.Waiting)
            {
                error = "Choose need, greed or pass.";
                return false;
            }

            LootRoll roll;
            if (id == 0)
            {
                roll = WaitingOn(player).FirstOrDefault();
                if (roll == null)
                {
                    error = "No loot roll is waiting on you.";
                    return false;
                }
            }
            else if (!_open.TryGetValue(id, out roll))
            {
                error = $"Loot roll {id} is over.";
                return false;
            }

            bool everyoneAnswered;
            lock (roll.Sync)
            {
                LootRollEntry mine = roll.Entries.FirstOrDefault(e => e.Member == player);
                if (roll.Closed || mine == null)
                {
                    error = mine == null ? $"You are not in loot roll {roll.Id}." : $"Loot roll {roll.Id} is over.";
                    return false;
                }

                if (mine.Choice != eLootChoice.Waiting)
                {
                    error = $"You already chose {mine.Choice} for {roll.Drop.Name}.";
                    return false;
                }

                mine.Choice = choice;
                everyoneAnswered = roll.Entries.All(static e => e.Choice != eLootChoice.Waiting);
            }

            if (everyoneAnswered)
                Finish(roll);
            return true;
        }

        /// <summary>Ends the roll now. A player who has not answered rolls Greed, so a missed window never loses the drop.</summary>
        public static void Finish(LootRoll roll)
        {
            if (roll == null)
                return;
            roll.Drop.WithPickUpLock(() => Resolve(roll));
        }

        /// <summary>
        /// Rolls for every member in the winning choice and returns them best first: everyone who chose
        /// Need, then everyone who chose Greed. No two members of a choice share a roll.
        /// Greed goes by roll. Need goes by class fit first: the best fit and everyone within
        /// LootClassFit.TieShare of it go by roll, then the next best fit, and so on. A drop without
        /// bonuses fits everyone equally, so it goes by roll alone.
        /// </summary>
        public static List<LootRollEntry> Rank(IEnumerable<LootRollEntry> entries, Func<int> die)
        {
            List<LootRollEntry> ranked = new();
            if (entries == null)
                return ranked;

            die ??= Die;
            foreach (eLootChoice choice in new[] { eLootChoice.Need, eLootChoice.Greed })
            {
                HashSet<int> used = new();
                List<LootRollEntry> tier = entries.Where(e => e.Choice == choice).ToList();
                foreach (LootRollEntry entry in tier)
                {
                    int roll = Math.Clamp(die(), 1, 100);
                    for (int attempt = 0; attempt < 200 && used.Contains(roll); attempt++)
                        roll = Math.Clamp(die(), 1, 100);
                    // A die that keeps repeating (a test, or 100 members) still gets a free number.
                    while (used.Contains(roll))
                        roll = roll > 1 ? roll - 1 : 100;
                    used.Add(roll);
                    entry.Roll = roll;
                }

                tier.Sort(static (left, right) => right.Roll.CompareTo(left.Roll));
                ranked.AddRange(choice == eLootChoice.Need ? ByFit(tier) : tier);
            }

            return ranked;
        }

        /// <summary>Orders Need rolls (already sorted by roll) by fit bands; within a band the roll order stays.</summary>
        private static List<LootRollEntry> ByFit(List<LootRollEntry> byRoll)
        {
            List<LootRollEntry> left = new(byRoll);
            List<LootRollEntry> ordered = new(byRoll.Count);
            while (left.Count > 0)
            {
                double best = left.Max(static e => e.Fit);
                double floor = best * LootClassFit.TieShare;
                List<LootRollEntry> band = left.Where(e => e.Fit >= floor).ToList();
                ordered.AddRange(band);
                left.RemoveAll(band.Contains);
            }

            return ordered;
        }

        /// <summary>True when the winner took a Need roll over a higher die because its class fits the drop better.</summary>
        public static bool WonOnFit(LootRoll roll, LootRollEntry winner) =>
            winner?.Choice == eLootChoice.Need &&
            roll.Entries.Any(e => e != winner && e.Choice == eLootChoice.Need && e.Roll > winner.Roll);

        private static void Resolve(LootRoll roll)
        {
            lock (roll.Sync)
            {
                if (roll.Closed)
                    return;
                roll.Closed = true;
                foreach (LootRollEntry entry in roll.Entries)
                {
                    if (entry.Choice == eLootChoice.Waiting)
                        entry.Choice = eLootChoice.Greed;
                }
            }

            _open.TryRemove(roll.Id, out _);
            roll.Timer?.Stop();
            roll.Timer = null;
            WorldInventoryItem drop = roll.Drop;
            drop.PendingRoll = null;
            // The drop decayed or was removed while the roll was open.
            if (drop.ObjectState == GameObject.eObjectState.Deleted || drop.Item == null)
            {
                Tell(roll, $"{Prefix}{roll.Id}: {Clean(drop.Name)} is gone.");
                return;
            }

            List<LootRollEntry> ranked = Rank(roll.Entries, Die);
            LootRollEntry winner = null;
            bool waitsOnGround = false;
            foreach (LootRollEntry entry in ranked)
            {
                if (entry.Member is GameBot bot)
                {
                    if (bot.ObjectState != GameObject.eObjectState.Active || bot.TryAutoPickUpItem(drop) is not TryPickUpResult.Success)
                        continue;
                    winner = entry;
                    break;
                }

                if (entry.Member is GamePlayer player)
                {
                    // A player who logged out or left the group since the drop cannot hold it,
                    // and a reserved drop would be lost to everyone. The next roll takes it.
                    if (player.ObjectState != GameObject.eObjectState.Active || !roll.Group.IsInTheGroup(player))
                        continue;
                    winner = entry;
                    if (GiveToPlayer(player, drop))
                        break;
                    // A full bag keeps the win. The drop waits on the ground for this player alone.
                    drop.ReservedFor = player;
                    waitsOnGround = true;
                    break;
                }
            }

            roll.Winner = winner?.Member;
            if (winner == null)
            {
                drop.ReservedFor = null;
                Tell(roll, $"{Prefix}{roll.Id}: nobody takes {Clean(drop.Name)}. It stays on the ground.");
                return;
            }

            string rolls = string.Join(", ", roll.Entries
                .OrderByDescending(static e => e.Choice).ThenByDescending(static e => e.Roll)
                .Select(static e => e.Choice == eLootChoice.Pass ? $"{e.Member.Name} Pass" : $"{e.Member.Name} {e.Choice} {e.Roll}"));
            foreach (LootRollEntry entry in roll.Entries)
            {
                if (entry.Member is not GamePlayer player || player.Client == null)
                    continue;
                bool mine = entry == winner;
                StringBuilder line = new($"{Prefix}{roll.Id}: {(mine ? "You win" : winner.Member.Name + " wins")} {Clean(drop.Name)} with {winner.Choice} {winner.Roll}.");
                if (WonOnFit(roll, winner))
                    line.Append(mine ? " Its bonuses suit your class best." : $" Its bonuses suit {winner.Member.Name}'s class best.");
                if (mine && waitsOnGround)
                    line.Append(" Your backpack is full, so it waits on the ground for you.");
                line.Append($" Rolls: {rolls}.");
                player.Out.SendMessage(line.ToString(), eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
            }
        }

        private static bool GiveToPlayer(GamePlayer player, WorldInventoryItem drop)
        {
            DbInventoryItem item = drop.Item;
            if (player?.Inventory == null || item == null || player.ObjectState != GameObject.eObjectState.Active)
                return false;
            if (!player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item))
                return false;

            InventoryLogging.LogInventoryAction("(ground)", player, eInventoryActionType.Loot, item.Template, 1);
            drop.ReservedFor = null;
            if (!drop.RemoveFromWorld())
                drop.ObjectState = GameObject.eObjectState.Inactive;
            return true;
        }

        private static void Tell(LootRoll roll, string line)
        {
            foreach (LootRollEntry entry in roll.Entries)
            {
                if (entry.Member is GamePlayer player && player.Client != null)
                    player.Out.SendMessage(line, eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
            }
        }

        private static void StartTimer(LootRoll roll)
        {
            roll.Timer = new ECSGameTimer(roll.Drop, _ =>
            {
                Finish(roll);
                return 0;
            }, SecondsToChoose * 1000);
        }

        /// <summary>Forgets every open roll. For tests.</summary>
        public static void Reset()
        {
            foreach (LootRoll roll in _open.Values)
            {
                roll.Timer?.Stop();
                roll.Drop.PendingRoll = null;
            }

            _open.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// How well a bot twists its chants or songs (issue #32, owner 2026-11-12): a bot learns its class as it levels, so a
    /// low-level Paladin or Minstrel holds one chant or song and a level 40+ one twists by what the group needs.
    /// </summary>
    public enum TwistTier
    {
        /// <summary>Levels 1-14 by default: one chant or song, switched on clear changes only. No twisting.</summary>
        Novice = 1,
        /// <summary>15-29: the anchor plus one secondary, refreshed with a safe margin, never while moving.</summary>
        Practiced = 2,
        /// <summary>30-39: the anchor plus the two secondaries the group needs most, also while moving.</summary>
        Skilled = 3,
        /// <summary>40+: every useful secondary by need, and an emergency switch to the heal chant or song.</summary>
        Expert = 4,
    }

    /// <summary>What the group needs right now, as the twisting bot sees it.</summary>
    public readonly record struct TwistNeeds(
        bool Fighting,
        bool Traveling,
        bool Grouped,
        int LowestHealthPercent,
        bool LowPower,
        bool LowEndurance);

    /// <summary>
    /// A chant or song the bot can cast now. Priority orders them (highest first; the caller's anchor rule may put its
    /// anchor first with int.MaxValue); 0 means not useful now, so it is never a secondary. CooldownMilliseconds is the
    /// remaining reuse; RemainingMilliseconds is the shortest life left on the song's child effect in the group.
    /// </summary>
    public readonly record struct TwistSong(
        int Id,
        eSpellType Type,
        int Priority,
        int CastMilliseconds,
        int CooldownMilliseconds,
        long RemainingMilliseconds);

    /// <summary>One brain's twisting state.</summary>
    public sealed class TwistMemory
    {
        public int AnchorId;
        public long AnchorSince;
        public bool Traveling;
        public int LastCastId;
        public long LastCastTick;
        public long LastTwistTick = long.MinValue / 2;
    }

    /// <summary>
    /// The planner's answer. CastId 0 casts nothing. Why is a short tag for the BOT_TWIST log line. Twist is true for a
    /// secondary cast (the anchor's return is not a twist).
    /// </summary>
    public readonly record struct TwistDecision(int CastId, string Why, int AnchorId, bool Twist = false)
    {
        public static TwistDecision None(string why, int anchorId) => new(0, why, anchorId);
    }

    /// <summary>
    /// Plans one twisting pulse. Pure, so the unit tests and the bot simulation harness run the same rules as the brain.
    /// Rules at every tier: never recast the running chant or song (a recast turns it off), never recast the one just
    /// cast inside <see cref="BotSongTwistPolicy.TankChantRecastGuardMilliseconds"/>, never leave the anchor unless its
    /// reuse is ready (so the bot can return at once) and its child has life for the round trip.
    /// </summary>
    public static class BotTwistPlanner
    {
        /// <summary>Default level at which each tier above Novice starts: Practiced, Skilled, Expert.</summary>
        public static readonly int[] DefaultTierLevels = { 15, 30, 40 };

        /// <summary>Default seconds between two twist casts for Practiced, Skilled and Expert.</summary>
        public static readonly int[] DefaultGapSeconds = { 6, 3, 0 };

        public static TwistTier TierFor(int level, IReadOnlyList<int> tierLevels = null)
        {
            tierLevels ??= DefaultTierLevels;
            if (tierLevels.Count >= 3 && level >= tierLevels[2]) return TwistTier.Expert;
            if (tierLevels.Count >= 2 && level >= tierLevels[1]) return TwistTier.Skilled;
            if (tierLevels.Count >= 1 && level >= tierLevels[0]) return TwistTier.Practiced;
            return TwistTier.Novice;
        }

        /// <summary>
        /// Parses a setting such as "15,30,40" (three ascending numbers). Anything else gives the fallback, so a typo
        /// in the setting cannot leave every bot without songs.
        /// </summary>
        public static int[] ParseThree(string text, int[] fallback, int min, int max)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;
            string[] parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return fallback;
            var values = new int[3];
            for (int i = 0; i < 3; i++)
                if (!int.TryParse(parts[i], out values[i]) || values[i] < min || values[i] > max)
                    return fallback;
            return values;
        }

        /// <summary>How many secondaries a tier twists with its anchor.</summary>
        public static int MaxSecondaries(TwistTier tier) => tier switch
        {
            TwistTier.Novice => 0,
            TwistTier.Practiced => 1,
            TwistTier.Skilled => 2,
            _ => int.MaxValue,
        };

        /// <summary>How long a tier keeps its anchor before it may switch to another one (#41's 10 s at Novice).</summary>
        public static int HoldMilliseconds(TwistTier tier) => tier switch
        {
            TwistTier.Novice => BotSongTwistPolicy.PaladinChantHoldMilliseconds,
            TwistTier.Practiced => 8_000,
            TwistTier.Skilled => 6_000,
            _ => 4_000,
        };

        /// <summary>Extra time before a secondary's child runs out at which the tier refreshes it.</summary>
        public static int RefreshMarginMilliseconds(TwistTier tier) => tier switch
        {
            TwistTier.Practiced => 1_500,
            TwistTier.Skilled => 750,
            _ => 0,
        };

        /// <summary>Minimum time between two twist casts (the return to the anchor does not count).</summary>
        public static int GapMilliseconds(TwistTier tier, IReadOnlyList<int> gapSeconds = null)
        {
            gapSeconds ??= DefaultGapSeconds;
            int index = tier switch { TwistTier.Practiced => 0, TwistTier.Skilled => 1, TwistTier.Expert => 2, _ => -1 };
            return index < 0 || index >= gapSeconds.Count ? int.MaxValue : Math.Max(0, gapSeconds[index]) * 1000;
        }

        /// <summary>
        /// Health at which a groupmate's danger lets a Skilled or Expert bot drop its anchor hold for the heal chant
        /// or song. Novice and Practiced bots do not react within the hold.
        /// </summary>
        public static int EmergencyHealthPercent(TwistTier tier) => tier switch
        {
            TwistTier.Skilled => 40,
            TwistTier.Expert => 55,
            _ => 0,
        };

        public static bool IsHealType(eSpellType type) => type is eSpellType.CombatHeal or eSpellType.HealthRegenBuff;

        /// <summary>
        /// How much the group needs a chant or song now, 1-100. Every song that passed the caller's filters keeps at
        /// least 1, so the score only reorders: a song the bot would have sung before is never dropped by it.
        /// </summary>
        public static int Score(eSpellType type, TwistNeeds needs)
        {
            bool injured = needs.LowestHealthPercent < 90;
            bool badlyHurt = needs.LowestHealthPercent < 60;
            int score = type switch
            {
                eSpellType.SpeedEnhancement => needs.Fighting ? 1 : needs.Traveling ? 100 : needs.Grouped ? 45 : 20,
                eSpellType.CombatHeal => needs.Fighting ? (badlyHurt ? 90 : injured ? 70 : 20) : (injured ? 35 : 5),
                eSpellType.HealthRegenBuff => needs.Fighting ? (badlyHurt ? 80 : injured ? 55 : 15) : (injured ? 75 : 15),
                eSpellType.PowerRegenBuff => needs.LowPower ? (needs.Fighting ? 60 : 70) : 10,
                eSpellType.EnduranceRegenBuff => needs.LowEndurance ? (needs.Fighting ? 65 : 50) : needs.Traveling ? 40 : needs.Fighting ? 30 : 25,
                eSpellType.DamageAdd => needs.Fighting ? 75 : 3,
                eSpellType.SpecArmorFactorBuff => needs.Fighting ? 60 : 3,
                eSpellType.AblativeArmor => needs.Fighting ? 58 : 3,
                eSpellType.Bladeturn => needs.Fighting ? 62 : 3,
                eSpellType.BodyResistBuff or eSpellType.ColdResistBuff or eSpellType.EnergyResistBuff or
                    eSpellType.HeatResistBuff or eSpellType.MatterResistBuff or eSpellType.SpiritResistBuff or
                    eSpellType.BodySpiritEnergyBuff or eSpellType.HeatColdMatterBuff => needs.Fighting ? 18 : 2,
                _ => 10,
            };
            return Math.Clamp(score, 1, 100);
        }

        /// <summary>
        /// Plans one pulse.
        /// songs: every chant or song the bot may cast now (affordable, not filtered for reuse), any order.
        /// activeId: the running pulse source, 0 if none.
        /// keepAnchorWhileMoving: #41 for Paladins, an anchor switch waits while the bot or its leader moves.
        /// twistSolo: whether an ungrouped bot twists at all (Paladins: no, see SoloPaladinAnchor).
        /// </summary>
        public static TwistDecision Plan(TwistTier tier, TwistNeeds needs, IReadOnlyList<TwistSong> songs, int activeId,
            TwistMemory memory, long now, bool moving, bool keepAnchorWhileMoving, bool twistSolo,
            IReadOnlyList<int> gapSeconds = null)
        {
            if (songs == null || songs.Count == 0)
                return TwistDecision.None("no-songs", memory.AnchorId);
            List<TwistSong> ordered = songs.OrderByDescending(song => song.Priority).ToList();
            TwistSong desired = ordered[0];
            TwistSong? anchor = ordered.Any(song => song.Id == memory.AnchorId)
                ? ordered.First(song => song.Id == memory.AnchorId) : null;

            // A brain that has no anchor yet adopts the running chant or song (after a reload or a buff batch), as if
            // it had been held long enough when this brain never cast one.
            if (anchor == null && activeId != 0 && ordered.Any(song => song.Id == activeId))
            {
                anchor = ordered.First(song => song.Id == activeId);
                memory.AnchorId = activeId;
                memory.AnchorSince = memory.LastCastId == activeId ? memory.LastCastTick : now - HoldMilliseconds(tier);
            }

            bool travelStarted = needs.Traveling && !memory.Traveling;
            memory.Traveling = needs.Traveling;
            bool emergency = EmergencyHealthPercent(tier) > 0 && needs.Fighting &&
                needs.LowestHealthPercent < EmergencyHealthPercent(tier) && IsHealType(desired.Type);
            string anchorWhy = null;
            if (anchor == null)
            {
                anchor = desired;
                anchorWhy = "anchor";
            }
            else if (desired.Id != anchor.Value.Id)
            {
                bool held = now - memory.AnchorSince >= HoldMilliseconds(tier);
                bool travelSwitch = travelStarted && desired.Type == eSpellType.SpeedEnhancement;
                bool movingBlocks = keepAnchorWhileMoving && moving && !emergency;
                if (!movingBlocks && (held || travelSwitch || emergency))
                {
                    anchor = desired;
                    anchorWhy = emergency ? "emergency" : travelSwitch ? "travel" : "switch";
                }
            }
            if (anchor.Value.Id != memory.AnchorId)
            {
                memory.AnchorId = anchor.Value.Id;
                memory.AnchorSince = now;
            }
            TwistSong anchorSong = anchor.Value;

            if (activeId != anchorSong.Id)
            {
                if (anchorSong.CooldownMilliseconds > 0)
                    return TwistDecision.None("anchor-reuse", anchorSong.Id);
                if (memory.LastCastId == anchorSong.Id && now - memory.LastCastTick < BotSongTwistPolicy.TankChantRecastGuardMilliseconds)
                    return TwistDecision.None("recast-guard", anchorSong.Id);
                string why = anchorWhy ?? (activeId == 0 ? "restart" : "return");
                return Cast(memory, anchorSong.Id, why, twist: false);
            }

            int secondaries = MaxSecondaries(tier);
            if (secondaries == 0)
                return TwistDecision.None("hold", anchorSong.Id);
            if (!needs.Grouped && !twistSolo)
                return TwistDecision.None("solo-hold", anchorSong.Id);
            if (moving && tier < TwistTier.Skilled)
                return TwistDecision.None("moving", anchorSong.Id);
            if (now - memory.LastTwistTick < GapMilliseconds(tier, gapSeconds))
                return TwistDecision.None("gap", anchorSong.Id);
            if (anchorSong.CooldownMilliseconds > 0)
                return TwistDecision.None("anchor-reuse", anchorSong.Id);

            foreach (TwistSong secondary in ordered.Where(song => song.Id != anchorSong.Id && song.Priority > 0).Take(secondaries))
            {
                if (secondary.CooldownMilliseconds > 0)
                    continue;
                int roundTrip = Math.Max(0, secondary.CastMilliseconds) + Math.Max(0, anchorSong.CastMilliseconds) + 1000;
                if (anchorSong.RemainingMilliseconds > roundTrip &&
                    secondary.RemainingMilliseconds <= roundTrip + RefreshMarginMilliseconds(tier))
                    return Cast(memory, secondary.Id, "twist", twist: true);
            }
            return TwistDecision.None("steady", anchorSong.Id);
        }

        private static TwistDecision Cast(TwistMemory memory, int id, string why, bool twist) =>
            new(id, why, memory.AnchorId, twist);

        /// <summary>
        /// Records a planned cast that went out. A cast that failed is not recorded, so the recast guard and the twist
        /// gap never hold back a cast that never happened.
        /// </summary>
        public static void Committed(TwistMemory memory, TwistDecision decision, long now)
        {
            if (decision.CastId == 0)
                return;
            memory.LastCastId = decision.CastId;
            memory.LastCastTick = now;
            if (decision.Twist)
                memory.LastTwistTick = now;
        }
    }
}

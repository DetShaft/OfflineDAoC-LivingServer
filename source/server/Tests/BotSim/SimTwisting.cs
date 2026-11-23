using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DOL.GS;

namespace DOL.UnitTests.BotSim
{
    /// <summary>
    /// Chant and song twisting (issue #32): one grouped chanting or singing bot per class (Paladin, Minstrel, Bard,
    /// Skald) at levels 10, 22, 35 and 45 runs a scripted 10-minute loop of travel, fights and rests. Its group's
    /// health, power and endurance follow a fixed curve; the bot decides with the real BotTwistPlanner (live) or the
    /// real BotSongTwistPolicy.Choose, GroupPaladinAnchor and MayStartTankChant (before). The spells' cast times, child
    /// lifetimes, pulse frequencies and reuse timers are the game database's (Chants, Instruments, Bard Nurture,
    /// Regrowth and Music, Battlesongs); instrument songs last 1.8 times as long, as with a bot's starter instrument.
    /// The brain's surrounding rules are mirrored by hand. Simulation results are tests only.
    /// </summary>
    public static class SimTwisting
    {
        public const string Name = "twisting";
        public const int Seconds = 600;
        public const int StepMs = 250;
        public const double InstrumentDuration = 1.8;
        public static readonly int[] Levels = { 10, 22, 35, 45 };

        public enum Rules { Before, Live }

        public static readonly Rules[] AllRules = { Rules.Before, Rules.Live };

        public static string Label(Rules rules) => rules == Rules.Before ? "before (#41 hold, old song order)" : "live (#32 tiers)";

        public sealed record SongDef(int Id, string Name, eSpellType Type, int Level, int Value, int CastMs, int DurationMs,
            int FrequencyMs, int ReuseMs, bool Instrument)
        {
            public bool Instant => CastMs == 0;
        }

        public sealed class ClassDef
        {
            public string Name;
            public eRealm Realm;
            public bool Paladin;
            /// <summary>Keeps its instrument out in fights (a support Bard, #48); the others fight in melee.</summary>
            public bool KeepsPlaying;
            public List<SongDef> Songs = new();
        }

        private static int _nextId = 1;

        private static void Ranks(ClassDef c, string name, eSpellType type, int[] levels, int[] values, int castMs,
            int durationS, int reuseS, bool instrument)
        {
            for (int i = 0; i < levels.Length; i++)
                c.Songs.Add(new SongDef(_nextId++, $"{name} {i + 1}", type, levels[i], values[Math.Min(i, values.Length - 1)],
                    castMs, durationS * 1000, durationS * 1000, reuseS * 1000, instrument));
        }

        private static void Resists(ClassDef c, int castMs, int durationS, int reuseS)
        {
            eSpellType[] types = { eSpellType.BodyResistBuff, eSpellType.SpiritResistBuff, eSpellType.EnergyResistBuff,
                eSpellType.HeatResistBuff, eSpellType.ColdResistBuff, eSpellType.MatterResistBuff };
            for (int i = 0; i < types.Length; i++)
                Ranks(c, types[i].ToString(), types[i], new[] { 25 + i, 44 + i }, new[] { 10, 20 }, castMs, durationS, reuseS, false);
        }

        /// <summary>The four classes with their group chants and songs from the game database (resists only where maintained).</summary>
        public static List<ClassDef> Classes()
        {
            _nextId = 1;
            var paladin = new ClassDef { Name = "Paladin", Realm = eRealm.Albion, Paladin = true };
            Ranks(paladin, "endurance", eSpellType.EnduranceRegenBuff, new[] { 2, 12, 22, 32, 42 }, new[] { 1, 2, 3, 4, 5 }, 0, 5, 0, false);
            Ranks(paladin, "combat heal", eSpellType.CombatHeal, new[] { 2, 5, 8, 12, 16, 20, 26, 35, 45 }, new[] { 3, 5, 8, 11, 14, 17, 22, 29, 37 }, 0, 6, 8, false);
            Ranks(paladin, "damage add", eSpellType.DamageAdd, new[] { 3, 7, 10, 15, 21, 28, 36, 46 }, new[] { 0 }, 0, 6, 8, false);
            Ranks(paladin, "armor", eSpellType.SpecArmorFactorBuff, new[] { 4, 9, 13, 18, 23, 29, 38, 48 }, new[] { 24, 33, 40, 51, 61, 75, 99, 127 }, 0, 6, 8, false);

            var minstrel = new ClassDef { Name = "Minstrel", Realm = eRealm.Albion };
            Ranks(minstrel, "regen", eSpellType.HealthRegenBuff, new[] { 1, 8, 14, 19, 25, 31, 37, 47 }, new[] { 2, 8, 13, 17, 22, 26, 31, 39 }, 3000, 6, 0, true);
            Ranks(minstrel, "power", eSpellType.PowerRegenBuff, new[] { 10, 21, 30, 39, 50 }, new[] { 1, 2, 3, 4, 5 }, 3000, 6, 0, true);
            Ranks(minstrel, "speed", eSpellType.SpeedEnhancement, new[] { 3, 11, 23, 33, 43 }, new[] { 144, 156, 174, 189, 204 }, 3000, 6, 0, true);
            Ranks(minstrel, "ablative", eSpellType.AblativeArmor, new[] { 30, 40, 50 }, new[] { 25, 40, 70 }, 0, 6, 8, false);

            var bard = new ClassDef { Name = "Bard", Realm = eRealm.Hibernia, KeepsPlaying = true };
            Ranks(bard, "endurance", eSpellType.EnduranceRegenBuff, new[] { 2, 12, 22, 32, 42 }, new[] { 1, 2, 3, 4, 5 }, 3000, 5, 0, true);
            Ranks(bard, "power", eSpellType.PowerRegenBuff, new[] { 10, 21, 30, 39, 50 }, new[] { 1, 2, 3, 4, 5 }, 3000, 6, 0, true);
            Ranks(bard, "speed", eSpellType.SpeedEnhancement, new[] { 3, 13, 23, 33, 43 }, new[] { 144, 159, 174, 189, 204 }, 3000, 6, 0, true);
            Ranks(bard, "regen", eSpellType.HealthRegenBuff, new[] { 6, 14, 25, 35, 45 }, new[] { 6, 13, 22, 30, 38 }, 3000, 6, 0, true);
            Resists(bard, 0, 5, 8);

            var skald = new ClassDef { Name = "Skald", Realm = eRealm.Midgard };
            Ranks(skald, "regen", eSpellType.HealthRegenBuff, new[] { 1, 10, 20, 30, 40, 50 }, new[] { 2, 10, 18, 26, 34, 42 }, 0, 5, 8, false);
            Ranks(skald, "speed", eSpellType.SpeedEnhancement, new[] { 3, 13, 23, 33, 43 }, new[] { 144, 159, 174, 189, 204 }, 0, 5, 8, false);
            Ranks(skald, "damage add", eSpellType.DamageAdd, new[] { 5, 9, 14, 19, 25, 35, 46 }, new[] { 0 }, 0, 5, 8, false);
            Resists(skald, 0, 5, 8);
            return new List<ClassDef> { paladin, minstrel, bard, skald };
        }

        /// <summary>A stretch of the scripted loop.</summary>
        public sealed record Segment(string Kind, int Start, int Length, int Dip);

        public static List<Segment> Schedule(int seed)
        {
            var rng = new Random(seed * 7919 + 13);
            var list = new List<Segment>();
            int t = 0;
            while (t < Seconds)
            {
                int travel = 40 + rng.Next(41), fight = 30 + rng.Next(21), rest = 20 + rng.Next(21), dip = 35 + rng.Next(36);
                list.Add(new Segment("travel", t, travel, 100)); t += travel;
                list.Add(new Segment("fight", t, fight, dip)); t += fight;
                list.Add(new Segment("rest", t, rest, 100)); t += rest;
            }
            return list;
        }

        /// <summary>The group's state at a time: what a bot's needs would read.</summary>
        public static TwistNeeds NeedsAt(List<Segment> schedule, double seconds)
        {
            Segment s = schedule.LastOrDefault(x => x.Start <= seconds) ?? schedule[0];
            double f = Math.Clamp((seconds - s.Start) / s.Length, 0, 1);
            switch (s.Kind)
            {
                case "fight":
                    double health = f < 0.5 ? 100 - (100 - s.Dip) * (f / 0.5) : s.Dip + (80 - s.Dip) * ((f - 0.5) / 0.5);
                    return new TwistNeeds(true, false, true, (int)Math.Round(health), f > 0.4, f > 0.5);
                case "rest":
                    return new TwistNeeds(false, false, true, (int)Math.Round(80 + 20 * f), f < 0.6, f < 0.3);
                default:
                    return new TwistNeeds(false, true, true, 100, false, false);
            }
        }

        public sealed class Run
        {
            public Rules Rules;
            public string Class;
            public eRealm Realm;
            public int Level, Seed;
            public TwistTier Tier;
            public int Casts, TwistCasts, Toggles, MaxCastsIn5s;
            public readonly Dictionary<string, (double Need, double Covered)> Coverage = new();
            public readonly List<string> Timeline = new();

            public void Cover(string need, bool has, bool covered)
            {
                if (!has) return;
                Coverage.TryGetValue(need, out var c);
                Coverage[need] = (c.Need + StepMs, c.Covered + (covered ? StepMs : 0));
            }

            public double? Share(string need) => Coverage.TryGetValue(need, out var c) && c.Need > 0 ? c.Covered / c.Need : null;
        }

        public static readonly string[] Needs = { "speed while traveling", "damage or armor in fights", "heal when under 60%", "power when low", "endurance when low", "resists in fights" };

        private static bool IsFightBuff(eSpellType type) => type is eSpellType.DamageAdd or eSpellType.SpecArmorFactorBuff or eSpellType.AblativeArmor;
        private static bool IsResist(eSpellType type) => type.ToString().Contains("Resist");

        public static List<Run> RunAll(int[] seeds = null)
        {
            seeds ??= SimBattleground.DefaultSeeds;
            var runs = new List<Run>();
            foreach (Rules rules in AllRules)
                foreach (ClassDef c in Classes())
                    foreach (int level in Levels)
                        foreach (int seed in seeds)
                            runs.Add(RunOne(rules, c.Name, level, seed));
            return runs;
        }

        public static Run RunOne(Rules rules, string className, int level, int seed)
        {
            ClassDef c = Classes().Single(x => x.Name == className);
            List<SongDef> known = c.Songs.Where(s => s.Level <= level).ToList();
            List<Segment> schedule = Schedule(seed);
            var run = new Run { Rules = rules, Class = c.Name, Realm = c.Realm, Level = level, Seed = seed, Tier = BotTwistPlanner.TierFor(level) };

            int activeId = 0; long nextPulse = 0;
            var expires = new Dictionary<eSpellType, long>();
            var reuse = new Dictionary<int, long>();
            int castingId = 0; long castingUntil = 0; bool castingTwist = false;
            long nextDecision = 0;
            var memory = new TwistMemory();
            int lastTankChantId = 0; long lastTankChantTick = 0;
            var castTimes = new List<long>();
            SongDef Def(int id) => known.First(s => s.Id == id);
            int Duration(SongDef s) => (int)(s.DurationMs * (s.Instrument ? InstrumentDuration : 1));

            void Complete(long now, SongDef s, bool twist)
            {
                run.Casts++;
                if (twist) run.TwistCasts++;
                castTimes.Add(now);
                if (s.Id == activeId)
                {
                    // Casting the running song turns it off.
                    run.Toggles++;
                    activeId = 0;
                    return;
                }
                activeId = s.Id;
                expires[s.Type] = now + Duration(s);
                nextPulse = now + s.FrequencyMs;
                reuse[s.Id] = now + s.ReuseMs;
                if (run.Timeline.Count < 80)
                    run.Timeline.Add($"{now / 1000.0:0.00}s cast {s.Name}{(twist ? " (twist)" : "")}");
            }

            for (long now = 0; now < Seconds * 1000L; now += StepMs)
            {
                TwistNeeds needs = NeedsAt(schedule, now / 1000.0);
                if (castingId != 0 && now >= castingUntil)
                {
                    Complete(now, Def(castingId), castingTwist);
                    castingId = 0;
                }
                if (activeId != 0 && now >= nextPulse)
                {
                    SongDef s = Def(activeId);
                    expires[s.Type] = now + Duration(s);
                    nextPulse += s.FrequencyMs;
                }

                if (castingId == 0 && now >= nextDecision)
                {
                    (int id, bool twist, int delay) = rules == Rules.Before
                        ? DecideBefore(c, known, needs, now, ref activeId, expires, reuse, ref lastTankChantId, ref lastTankChantTick)
                        : DecideLive(c, known, needs, now, ref activeId, expires, reuse, memory, run.Tier);
                    if (id != 0)
                    {
                        SongDef s = Def(id);
                        if (s.Instant)
                            Complete(now, s, twist);
                        else
                        {
                            castingId = id; castingUntil = now + s.CastMs; castingTwist = twist;
                        }
                        nextDecision = now + (c.Paladin ? 750 : s.CastMs + 250);
                    }
                    else
                        nextDecision = now + delay;
                }

                bool Has(Func<eSpellType, bool> type) => known.Any(s => type(s.Type));
                bool On(Func<eSpellType, bool> type) => expires.Any(e => type(e.Key) && e.Value > now);
                bool fightBuffUsable = c.Paladin || c.KeepsPlaying || known.Any(s => IsFightBuff(s.Type) && s.Instant && !s.Instrument);
                run.Cover(Needs[0], needs.Traveling && Has(t => t == eSpellType.SpeedEnhancement), On(t => t == eSpellType.SpeedEnhancement));
                run.Cover(Needs[1], needs.Fighting && Has(IsFightBuff) && fightBuffUsable, On(IsFightBuff));
                run.Cover(Needs[2], needs.Fighting && needs.LowestHealthPercent < 60 && Has(BotTwistPlanner.IsHealType), On(BotTwistPlanner.IsHealType));
                run.Cover(Needs[3], needs.LowPower && Has(t => t == eSpellType.PowerRegenBuff), On(t => t == eSpellType.PowerRegenBuff));
                run.Cover(Needs[4], needs.LowEndurance && Has(t => t == eSpellType.EnduranceRegenBuff), On(t => t == eSpellType.EnduranceRegenBuff));
                run.Cover(Needs[5], needs.Fighting && Has(IsResist), On(IsResist));
            }
            for (int i = 0; i < castTimes.Count; i++)
                run.MaxCastsIn5s = Math.Max(run.MaxCastsIn5s, castTimes.Skip(i).TakeWhile(t => t - castTimes[i] < 5_000).Count());
            return run;
        }

        private static long Remaining(Dictionary<eSpellType, long> expires, eSpellType type, long now) =>
            expires.TryGetValue(type, out long e) ? Math.Max(0, e - now) : 0;

        private static int Cooldown(Dictionary<int, long> reuse, int id, long now) =>
            reuse.TryGetValue(id, out long r) ? (int)Math.Max(0, r - now) : 0;

        /// <summary>The rules before #32: performers by BotSongTwistPolicy.Choose; grouped Paladins hold one chant (#41).</summary>
        private static (int Id, bool Twist, int Delay) DecideBefore(ClassDef c, List<SongDef> known, TwistNeeds needs, long now,
            ref int activeId, Dictionary<eSpellType, long> expires, Dictionary<int, long> reuse, ref int lastId, ref long lastTick)
        {
            if (c.Paladin)
            {
                var chants = known.GroupBy(s => s.Type).Select(g => g.OrderByDescending(s => s.Level).First()).ToList();
                eSpellType anchorType = BotSongTwistPolicy.GroupPaladinAnchor(needs.Fighting, needs.LowestHealthPercent < 95);
                SongDef chosen = PaladinAnchor(chants, anchorType, needs.Fighting);
                if (chosen == null || Cooldown(reuse, chosen.Id, now) > 0)
                    return (0, false, 750);
                if (!BotSongTwistPolicy.MayStartTankChant(chosen.Id, activeId, lastId, lastTick, now, needs.Traveling,
                        keepWhileMoving: true, holdMilliseconds: BotSongTwistPolicy.PaladinChantHoldMilliseconds))
                    return (0, false, 750);
                lastId = chosen.Id; lastTick = now;
                return (chosen.Id, false, 750);
            }
            if (needs.Fighting && !c.KeepsPlaying)
            {
                // Before #32 a performer in melee ended its song and sang nothing until the fight was over.
                activeId = 0;
                return (0, false, 1_000);
            }
            List<SongDef> songs = known
                .Where(s => s.Type != eSpellType.SpeedEnhancement || !needs.Fighting)
                .OrderByDescending(s => s.Type == eSpellType.SpeedEnhancement && !needs.Fighting)
                .ThenByDescending(s => s.Value).ThenByDescending(s => s.Level).ToList();
            if (songs.Count == 0)
                return (0, false, 4_000);
            long n = now;
            Dictionary<eSpellType, long> e = expires;
            Dictionary<int, long> r = reuse;
            int chosenId = BotSongTwistPolicy.Choose(songs[0].Id, activeId,
                songs.Select(s => new BotSongTwistPolicy.Song(s.Id, s.CastMs, Cooldown(r, s.Id, n), Remaining(e, s.Type, n))).ToArray());
            return (chosenId, chosenId != 0 && chosenId != songs[0].Id, 750);
        }

        private static SongDef PaladinAnchor(List<SongDef> chants, eSpellType anchorType, bool fighting) =>
            chants.FirstOrDefault(s => s.Type == anchorType) ??
            chants.FirstOrDefault(s => s.Type == eSpellType.DamageAdd && fighting) ??
            chants.FirstOrDefault(s => s.Type == eSpellType.EnduranceRegenBuff) ??
            chants.FirstOrDefault(s => s.Type == eSpellType.SpecArmorFactorBuff) ??
            chants.FirstOrDefault(s => s.Type != eSpellType.CombatHeal);

        /// <summary>The live rules: BotTwistPlanner, as TryMaintainPaladinChant and TryMaintainClassicSongTwist call it.</summary>
        private static (int Id, bool Twist, int Delay) DecideLive(ClassDef c, List<SongDef> known, TwistNeeds needs, long now,
            ref int activeId, Dictionary<eSpellType, long> expires, Dictionary<int, long> reuse, TwistMemory memory, TwistTier tier)
        {
            IEnumerable<SongDef> pool = known;
            bool meleeChanting = !c.Paladin && needs.Fighting && !c.KeepsPlaying;
            if (meleeChanting)
            {
                // Issue #32: songs that need the instrument end in melee; instant chants without one keep running.
                int runningId = activeId;
                SongDef running = known.FirstOrDefault(s => s.Id == runningId);
                if (running != null && (running.Instrument || !running.Instant || running.Type == eSpellType.SpeedEnhancement))
                    activeId = 0;
                pool = pool.Where(s => s.Instant && !s.Instrument && s.Type != eSpellType.SpeedEnhancement);
            }
            if (!c.Paladin)
                pool = pool.Where(s => s.Type != eSpellType.SpeedEnhancement || !needs.Fighting);
            List<SongDef> songs = pool.GroupBy(s => s.Type)
                .Select(g => g.OrderByDescending(s => s.Level).ThenByDescending(s => s.Value).First())
                .OrderByDescending(s => s.Type == eSpellType.SpeedEnhancement && !needs.Fighting)
                .ThenByDescending(s => s.Value).ThenByDescending(s => s.Level).ToList();
            if (songs.Count == 0)
                return (0, false, meleeChanting ? 1_000 : 4_000);

            SongDef anchor = null;
            if (c.Paladin)
            {
                anchor = PaladinAnchor(songs, BotSongTwistPolicy.GroupPaladinAnchor(needs.Fighting, needs.LowestHealthPercent < 95), needs.Fighting);
                if (anchor == null)
                    return (0, false, 750);
            }
            long n = now;
            Dictionary<eSpellType, long> e = expires;
            Dictionary<int, long> r = reuse;
            TwistDecision d = BotTwistPlanner.Plan(tier, needs,
                songs.Select(s => new TwistSong(s.Id, s.Type, s == anchor ? int.MaxValue : BotTwistPlanner.Score(s.Type, needs),
                    s.CastMs, Cooldown(r, s.Id, n), Remaining(e, s.Type, n))).ToArray(),
                activeId, memory, now, needs.Traveling, keepAnchorWhileMoving: c.Paladin, twistSolo: !c.Paladin && !meleeChanting);
            BotTwistPlanner.Committed(memory, d, now);
            return (d.CastId, d.Twist, 750);
        }

        private static string P(double? share) => share == null ? "-" : (share.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        private static double? MeanShare(IEnumerable<Run> runs, string need)
        {
            double needMs = runs.Sum(r => r.Coverage.TryGetValue(need, out var c) ? c.Need : 0);
            double covered = runs.Sum(r => r.Coverage.TryGetValue(need, out var c) ? c.Covered : 0);
            return needMs == 0 ? null : covered / needMs;
        }

        public static List<SimFinding> Evaluate(List<Run> runs)
        {
            var findings = new List<SimFinding>();
            foreach (Rules rules in AllRules)
            {
                var set = runs.Where(r => r.Rules == rules).ToList();
                bool isLive = rules == Rules.Live;
                void Add(string text, bool ok, string measured) =>
                    findings.Add(new SimFinding { Scenario = Name, Variant = Label(rules), Expectation = text, Measured = measured, Ok = ok, Live = isLive });

                int toggles = set.Sum(r => r.Toggles);
                Add("No chant or song is ever recast while it runs (that turns it off)", toggles == 0, $"{toggles} toggles");
                int burst = set.Max(r => r.MaxCastsIn5s);
                Add("At most 3 casts in any 5 s (no chant spam)", burst <= 3, $"{burst} at most");
                int noviceTwists = set.Where(r => r.Tier == TwistTier.Novice).Sum(r => r.TwistCasts);
                Add("Novice bots (level 10) never twist", noviceTwists == 0, $"{noviceTwists} twist casts");
                double? speed = MeanShare(set.Where(r => !r.Class.Equals("Paladin")), Needs[0]);
                Add("Performers keep speed up at least 90% of travel", speed >= 0.9, P(speed));
                foreach (string cls in new[] { "Paladin", "Minstrel", "Bard", "Skald" })
                {
                    var expert = set.Where(r => r.Class == cls && r.Tier == TwistTier.Expert).ToList();
                    var novice = set.Where(r => r.Class == cls && r.Level == Levels[0]).ToList();
                    double? fight = MeanShare(expert, Needs[1]);
                    double? beforeFight = MeanShare(runs.Where(r => r.Rules == Rules.Before && r.Class == cls && r.Tier == TwistTier.Expert), Needs[1]);
                    if (fight != null && isLive)
                        Add($"Level 45 {cls}: damage or armor up in fights at least as much as before", fight >= beforeFight - 0.001,
                            $"{P(fight)} (before {P(beforeFight)})");
                    double? heal = MeanShare(expert, Needs[2]);
                    double? beforeHeal = MeanShare(runs.Where(r => r.Rules == Rules.Before && r.Class == cls && r.Tier == TwistTier.Expert), Needs[2]);
                    if (heal != null && isLive)
                        Add($"Level 45 {cls}: heal chant or song up when a groupmate is under 60% at least as much as before", heal >= beforeHeal - 0.001,
                            $"{P(heal)} (before {P(beforeHeal)})");
                    // Only the needs both levels have a song for: a level 45 bot's extra songs (resists) must not count against it.
                    string[] shared = Needs.Where(nd => MeanShare(expert, nd) != null && MeanShare(novice, nd) != null).ToArray();
                    double expertNeeds = shared.Select(nd => MeanShare(expert, nd).Value).DefaultIfEmpty(0).Average();
                    double noviceNeeds = shared.Select(nd => MeanShare(novice, nd).Value).DefaultIfEmpty(0).Average();
                    if (isLive)
                        Add($"{cls}: level 45 covers its group's needs at least as well as level 10 (a learning curve; needs both levels have a song for)", expertNeeds >= noviceNeeds - 0.001,
                            $"{P(expertNeeds)} vs {P(noviceNeeds)}");
                }
            }
            return findings;
        }

        public static string Markdown(List<Run> runs, List<SimFinding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Chant and song twisting by level (issue #32)");
            sb.AppendLine();
            sb.AppendLine($"One grouped bot per class at levels {string.Join(", ", Levels)} through {Seconds / 60} minutes of travel (40-80 s), fights (30-50 s; the lowest groupmate dips to 35-70% and recovers to 80%; power runs low from 40% in, endurance from halfway) " +
                          "and rests (20-40 s). Real BotTwistPlanner (live) or BotSongTwistPolicy.Choose / GroupPaladinAnchor / MayStartTankChant (before); spell timings from the game database. " +
                          "A Bard is modelled as the group's support (keeps playing in fights, #48); Paladin, Minstrel and Skald fight in melee. Simulation results are tests only, not in-game evidence.");
            sb.AppendLine();
            sb.AppendLine($"Runs: {runs.Count / AllRules.Length} per variant ({runs.Select(r => r.Seed).Distinct().Count()} seeds).");
            sb.AppendLine();
            sb.AppendLine("Tiers (setting bots / twist_tier_levels, default 15,30,40): Novice 1-14 holds one; Practiced 15-29 one secondary, not while moving; Skilled 30-39 two, emergency heal under 40%; Expert 40+ all useful, emergency heal under 55%.");
            sb.AppendLine();
            sb.AppendLine("## Expectations");
            sb.AppendLine();
            sb.AppendLine("| Variant | Result | Expectation | Measured |");
            sb.AppendLine("|---|---|---|---|");
            foreach (SimFinding f in findings)
                sb.AppendLine($"| {f.Variant} | {(f.Ok ? "pass" : "**FINDING**")} | {f.Expectation} | {f.Measured} |");
            sb.AppendLine();

            sb.AppendLine("## By class and level");
            sb.AppendLine();
            sb.AppendLine("Share of the time each need was there (and the class had a song for it at that level) that a matching child effect was up. '-' means no such need or song.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Realm | Class | Level | Tier | Casts / min | Twists / min | Most casts in 5 s | " + string.Join(" | ", Needs) + " |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|" + string.Concat(Needs.Select(_ => "---|")));
            foreach (Rules rules in AllRules)
                foreach (var g in runs.Where(r => r.Rules == rules).GroupBy(r => (r.Class, r.Level)))
                {
                    var set = g.ToList();
                    Run first = set[0];
                    sb.AppendLine($"| {Label(rules)} | {SimRoster.Short(first.Realm)} | {first.Class} | {first.Level} | {first.Tier} | {F1(set.Average(r => r.Casts) * 60.0 / Seconds)} | " +
                                  $"{F1(set.Average(r => r.TwistCasts) * 60.0 / Seconds)} | {set.Max(r => r.MaxCastsIn5s)} | " +
                                  string.Join(" | ", Needs.Select(nd => P(MeanShare(set, nd)))) + " |");
                }
            sb.AppendLine();

            sb.AppendLine("## By realm");
            sb.AppendLine();
            sb.AppendLine("The same rules apply to every class; the realms differ only in which classes chant or sing and what songs they have (Albion: Paladin and Minstrel; Hibernia: Bard, and the Warden keeps one situational chant; Midgard: Skald).");
            sb.AppendLine();
            sb.AppendLine("| Variant | Realm | Classes | " + string.Join(" | ", Needs) + " |");
            sb.AppendLine("|---|---|---|" + string.Concat(Needs.Select(_ => "---|")));
            foreach (Rules rules in AllRules)
                foreach (var g in runs.Where(r => r.Rules == rules).GroupBy(r => r.Realm))
                    sb.AppendLine($"| {Label(rules)} | {SimRoster.Short(g.Key)} | {string.Join(", ", g.Select(r => r.Class).Distinct())} | " +
                                  string.Join(" | ", Needs.Select(nd => P(MeanShare(g, nd)))) + " |");
            sb.AppendLine();

            sb.AppendLine("## Timeline (seed 1, first casts)");
            foreach (Run run in runs.Where(r => r.Seed == runs.Min(x => x.Seed) && r.Level == 45))
            {
                sb.AppendLine();
                sb.AppendLine($"### {Label(run.Rules)}: {run.Class} {run.Level}");
                sb.AppendLine();
                foreach (string line in run.Timeline.Take(30))
                    sb.AppendLine($"- {line}");
            }
            return sb.ToString();
        }

        public static string Write(List<Run> runs, List<SimFinding> findings)
        {
            string md = Markdown(runs, findings);
            string folder = SimReport.OutputFolder();
            File.WriteAllText(Path.Combine(folder, Name + ".md"), md);
            File.AppendAllLines(Path.Combine(folder, "findings.tsv"),
                findings.Select(f => string.Join('\t', f.Ok ? "pass" : "FINDING", f.Scenario, f.Variant, f.Live ? "live" : "compare", f.Expectation, f.Measured)));
            return md;
        }
    }
}

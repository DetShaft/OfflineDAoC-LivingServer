using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.UnitTests.BotSim
{
    /// <summary>
    /// Upstream Living Server 1.2's open-field RvR group AI (chris4aker/OfflineDAoC-LivingServer, tag v1.2, commit
    /// 3ba8f20), carried into the harness BY HAND for a comparison only. None of this code ships in this fork, and the
    /// harness does not call upstream's own classes: their numbers and decision order are copied from these files:
    /// <list type="bullet">
    /// <item>AutonomousRvrPerception.cs (layer 1): sure notice within 1,000, none past 3,500, the normal curve's medians
    /// (0.75 s at 1,000, 1.5 s at 1,500, 6 s at 2,500, 20 s at 3,400), scans every 1,125 ms by two lookers, the
    /// combat, casting, group and facing factors, a 0.5-2 s reaction delay, and the group noticing an enemy's mates
    /// within 1,500.</item>
    /// <item>RvrEngagement.cs (work package 4): Observe for the tier's watch (Average 9 s x patience factor) until contact;
    /// Engage at odds 0.45 or better; Retreat below 0.30, or when the group has no healer left, the enemy has one and the
    /// odds are under 0.38 (a lost fight). The power formula: (level/50)^2, health floor 0.25, a held head 0.25, a pet 0.3,
    /// +15% per healer up to three.</item>
    /// <item>RvrAssist.cs and RvrTargetPriority.cs (work package 2): main assist and called target, the 2003 kill order
    /// with the realm variants (vs Midgard the Shaman first and the Healer below the casters; vs Hibernia Bard, Eldritch,
    /// Druid), the call held 4-8 s, 20% score wobble and 10% of calls on a tank (Average tier), follow delays of
    /// 0.6-2.5 s and 25% of members tunnelling a whole fight.</item>
    /// <item>RvrCrowdControlPlan.cs: one subject per crowd controller, never the kill target, by the hold order (healers
    /// first, vs Midgard highest); a resisted mez is cast again; an area mez only when it spares the kill target; no
    /// under-fire or two-in-the-area check (those are this fork's #156 / #170 rules).</item>
    /// <item>RvrInterruptPlan.cs: each enemy caster or healer that is not the kill target and not held gets one interrupter
    /// with a means, at most half the group; in this model only melee within 400 qualify (no sim class has an instant or
    /// a sub-2 s spell, a bow or a pet), looking again every 2-2.5 s.</item>
    /// </list>
    /// Not modelled: the Kite stance's movement and RvrPositioning (backline slots, 350 spread, healer kiting), the tank
    /// peel (the only tank in the standard groups is the scripted leader), stealth openers, the tier's misread odds, and
    /// the warband relay. Upstream has none of this fork's caster kiting (BotCasterSurvival) or line of sight walk (#176),
    /// so its casters stand and cast, and its base rules are the ones before #170.
    /// Upstream's scope guard keeps a group a player leads or is in on the older rules; where the harness puts these
    /// tactics on side 0 (the scripted player's group) it is a what-if: the field plan run by a group whose leader charges.
    /// </summary>
    public sealed class Upstream12FieldTactics : CurrentBotTactics
    {
        public enum Tier { Casual, Average, Veteran }

        public Tier SkillTier { get; init; } = Tier.Average;

        public enum Stance { Quiet, Observe, Engage, Retreat }

        private sealed class GroupPlan
        {
            public long NextBoard, NextScan;
            public readonly Dictionary<SimAgent, long> NoticedAt = new();
            public Stance Stance = Stance.Quiet;
            public long StanceSince, ObserveSince;
            public SimAgent Called;
            public long CalledAt, FightId, LastCallAt = -100_000;
            public readonly Dictionary<SimAgent, SimAgent> CrowdControl = new(); // controller -> subject
            public readonly Dictionary<SimAgent, SimAgent> Interrupts = new(); // interrupter -> caster
            public readonly Dictionary<SimAgent, long> NextLook = new();
            public readonly Dictionary<SimAgent, bool> LookedCasting = new();
        }

        private readonly Dictionary<SimAgent, GroupPlan> _plans = new();
        private ulong _worldSalt;
        private bool _salted;

        public Upstream12FieldTactics()
        {
            Label = "upstream 1.2 field plan";
            SharedFocus = false;
            CompanionRecall = false;
            ProximityAggroRadius = 0; // the perception rule below replaces the instant 1,500 on sight
            DirectAttackerFirst = true; // a bot that is hit fights back (RvrEngagement.MayTakeTarget); the call overrides it
            Fixes170 = false;
            EdgeCaseFixes = false;
        }

        // ---- numbers copied from upstream v1.2 -------------------------------------------------------------------------

        public const int AlwaysNoticeRadius = 1_000;
        public const int OuterRange = 3_500;
        public const int ScanMilliseconds = 1_125;
        public const int PartyNoticeRadius = 1_500;
        public const int CallRadius = 2_500;
        public const double EngageOdds = 0.45, RetreatOdds = 0.30, LostOdds = 0.38;
        private static readonly (double Distance, double Median)[] NormalAnchors =
            { (1_000, 0.75), (1_500, 1.5), (2_500, 6), (3_400, 20) };

        public long WatchBase => SkillTier switch { Tier.Casual => 6_000, Tier.Veteran => 12_000, _ => 9_000 };
        public double Wobble => SkillTier switch { Tier.Veteran => 0.12, Tier.Casual => 0.28, _ => 0.20 };
        public double WrongCallShare => SkillTier switch { Tier.Veteran => 0.05, Tier.Casual => 0.15, _ => 0.10 };
        public (long Min, long Max) FollowDelayRange => SkillTier switch { Tier.Casual => (1_000, 4_000), Tier.Veteran => (300, 1_500), _ => (600, 2_500) };
        public double TunnelShare => SkillTier switch { Tier.Casual => 0.40, Tier.Veteran => 0.10, _ => 0.25 }; // boldness 0.5
        public (long Min, long Max) LookRange => SkillTier switch { Tier.Veteran => (1_500, 2_000), Tier.Casual => (2_500, 3_000), _ => (2_000, 2_500) };
        public double RiseNoticeChance => SkillTier switch { Tier.Veteran => 0.90, Tier.Casual => 0.60, _ => 0.75 };

        /// <summary>A 0-1 roll from the run's seed and two numbers (upstream rolls are hashes too, so a run replays).</summary>
        private double Roll(long a, long b, ulong salt)
        {
            unchecked
            {
                ulong x = _worldSalt ^ salt;
                x = Mix(x ^ (ulong)a);
                x = Mix(x ^ (ulong)b * 0x9E3779B97F4A7C15UL);
                return (x >> 11) * (1.0 / (1UL << 53));
            }
        }

        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        // ---- kill order (RvrTargetPriority) --------------------------------------------------------------------------

        private enum KillRole { Caster, Healer, CrowdControl, Melee, Tank, Pet, Unknown }

        private static KillRole KillRoleOf(SimAgent e)
        {
            if (e.Kit.IsPet)
                return KillRole.Pet;
            if (e.Kit.IsNpc)
                return KillRole.Unknown;
            if (e.ClassName is "Bard" or "Shaman")
                return KillRole.CrowdControl;
            return e.Role switch
            {
                SimRole.Caster => KillRole.Caster,
                SimRole.Healer => KillRole.Healer,
                SimRole.Mezzer => e.ClassName == "Healer" ? KillRole.Healer : KillRole.Caster, // Sorcerer: a cloth caster
                SimRole.Melee => KillRole.Melee,
                SimRole.Tank => KillRole.Tank,
                _ => KillRole.Unknown,
            };
        }

        private static double BaseScore(KillRole role) => role switch
        {
            KillRole.Caster => 100,
            KillRole.Healer => 90,
            KillRole.CrowdControl => 70,
            KillRole.Melee => 40,
            KillRole.Unknown => 35,
            KillRole.Tank => 20,
            _ => 5,
        };

        private static double RealmScore(SimAgent e, KillRole role) => (e.Realm, e.ClassName) switch
        {
            (DOL.GS.eRealm.Midgard, "Shaman") => 130,
            (DOL.GS.eRealm.Midgard, "Healer") => 85,
            (DOL.GS.eRealm.Hibernia, "Bard") => 130,
            (DOL.GS.eRealm.Hibernia, "Eldritch") => 120,
            (DOL.GS.eRealm.Hibernia, "Druid") => 110,
            _ => BaseScore(role),
        };

        private static bool TargetsGroup(SimAgent e, List<SimAgent> group) =>
            e.Target != null && group.Contains(e.Target) || e.Casting?.Target != null && group.Contains(e.Casting.Target);

        private static double Score(SimAgent e, List<SimAgent> group)
        {
            KillRole role = KillRoleOf(e);
            double score = e.Kit.IsPet ? BaseScore(KillRole.Pet) : RealmScore(e, role);
            if (TargetsGroup(e, group))
                score += 5;
            return score + Math.Clamp(100 - e.HealthPercent, 0, 100) * 0.15;
        }

        private static double HoldScore(SimAgent e, List<SimAgent> group)
        {
            double score = KillRoleOf(e) switch
            {
                KillRole.Healer => e.Realm == DOL.GS.eRealm.Midgard ? 120 : 100,
                KillRole.Caster => 90,
                KillRole.CrowdControl => 85,
                KillRole.Melee => 50,
                KillRole.Tank => 40,
                KillRole.Unknown => 30,
                _ => 5,
            };
            return TargetsGroup(e, group) ? score + 10 : score;
        }

        private static bool IsSpellcaster(SimAgent e) =>
            !e.Kit.IsNpc && (e.Kit.CastsNukes || e.Kit.Heals || e.ClassName is "Shaman" or "Bard");

        // ---- perception (AutonomousRvrPerception) --------------------------------------------------------------------

        private static double MedianSecondsToNotice(double distance)
        {
            if (distance <= AlwaysNoticeRadius)
                return 0;
            if (distance > OuterRange)
                return double.PositiveInfinity;
            var anchors = NormalAnchors;
            int i = 0;
            while (i < anchors.Length - 2 && distance > anchors[i + 1].Distance)
                i++;
            double t = (distance - anchors[i].Distance) / Math.Max(1, anchors[i + 1].Distance - anchors[i].Distance);
            double from = Math.Log(anchors[i].Median), to = Math.Log(anchors[i + 1].Median);
            return Math.Exp(from + t * (to - from));
        }

        /// <summary>AutonomousRvrPerception.ChancePerScan: the per-scan chance that gives that median.</summary>
        private static double ChancePerScan(double medianSeconds)
        {
            if (double.IsPositiveInfinity(medianSeconds))
                return 0;
            double m = medianSeconds * 1000 / ScanMilliseconds;
            if (m <= 0.5)
                return 1;
            int k = (int)Math.Floor(m);
            double x = m - k;
            if (k == 0)
                return 0.5 / x;
            double lo = 0, hi = 1, p = 1 - Math.Pow(0.5, 1 / m);
            for (int step = 0; step < 50; step++)
            {
                double qk1 = Math.Pow(1 - p, k - 1), qk = qk1 * (1 - p);
                double f = (1 - x * p) * qk - 0.5;
                if (Math.Abs(f) < 1e-12)
                    break;
                if (f > 0) lo = p; else hi = p;
                double slope = -x * qk - (1 - x * p) * k * qk1;
                double next = slope < 0 ? p - f / slope : double.NaN;
                p = next > lo && next < hi ? next : (lo + hi) / 2;
            }
            return p;
        }

        private static double NoticeChance(SimWorld w, SimAgent looker, SimAgent enemy)
        {
            double distance = looker.DistanceTo(enemy);
            if (distance <= AlwaysNoticeRadius)
                return 1.0;
            if (distance > OuterRange)
                return 0.0;
            double rate = 1.75; // the groups face each other: inside the 120-degree facing cone
            if (w.Now - enemy.LastDamageTime < SimWorld.InCombatWindowMilliseconds)
                rate *= 2.0;
            if (enemy.Casting != null)
                rate *= 1.5;
            if (enemy.Group.Count(m => m.Alive) >= 3)
                rate *= 1.5;
            return Math.Min(0.95, ChancePerScan(MedianSecondsToNotice(distance) / rate));
        }

        private long ReactionDelay(SimAgent member, SimAgent enemy) =>
            500 + (long)Math.Round(Roll(member.Id, enemy.Id, 0x52454143) * 1_500);

        // ---- the group's board ---------------------------------------------------------------------------------------

        private GroupPlan PlanOf(SimAgent a)
        {
            SimAgent key = a.Leader ?? a;
            if (!_plans.TryGetValue(key, out GroupPlan plan))
                _plans[key] = plan = new GroupPlan();
            return plan;
        }

        private static List<SimAgent> Heads(SimAgent a) => a.Group.Where(m => m.Alive && !m.Kit.IsNpc).ToList();

        private static double Power(SimWorld w, IEnumerable<SimAgent> side)
        {
            var list = side.Where(s => s.Alive).ToList();
            double sum = 0;
            foreach (SimAgent s in list)
            {
                double head = s.Kit.IsPet ? 0.3 : Math.Pow(s.Level / 50.0, 2) * (0.25 + 0.75 * s.HealthPercent / 100.0);
                if (s.Controlled(w.Now))
                    head *= 0.25;
                sum += head;
            }
            int healers = Math.Min(3, list.Count(s => s.Kit.Heals));
            return sum * (1 + 0.15 * healers);
        }

        private void Board(SimWorld w, SimAgent a, GroupPlan plan)
        {
            List<SimAgent> group = a.Group;
            List<SimAgent> heads = Heads(a);
            if (heads.Count == 0)
                return;

            // Layer 1: two lookers scan every 1,125 ms.
            if (w.Now >= plan.NextScan)
            {
                plan.NextScan = w.Now + ScanMilliseconds;
                var lookers = heads.Where(h => h.IsBot).OrderBy(h => h.Id).Take(2).ToList();
                foreach (SimAgent enemy in w.Enemies(a))
                {
                    if (plan.NoticedAt.ContainsKey(enemy))
                        continue;
                    bool noticed = TargetsGroup(enemy, group) || w.HitSideWithin(a.Side, enemy, 5_000);
                    foreach (SimAgent looker in lookers)
                    {
                        if (noticed || !w.LineOfSight(looker, enemy))
                            continue;
                        noticed = Roll(looker.Id, enemy.Id * 100_000 + w.Now / ScanMilliseconds, 0x53434e) < NoticeChance(w, looker, enemy);
                    }
                    if (!noticed)
                        continue;
                    plan.NoticedAt[enemy] = w.Now;
                    // Noticing one enemy means noticing its group mates within 1,500 of it.
                    foreach (SimAgent mate in w.Enemies(a).Where(m => m != enemy && m.DistanceTo(enemy) <= PartyNoticeRadius))
                        if (!plan.NoticedAt.ContainsKey(mate))
                            plan.NoticedAt[mate] = w.Now;
                }
                foreach (SimAgent gone in plan.NoticedAt.Keys.Where(e => !e.Alive).ToList())
                    plan.NoticedAt.Remove(gone);
            }

            if (w.Now < plan.NextBoard)
                return;
            plan.NextBoard = w.Now + 1_000;

            var enemies = w.Enemies(a).ToList();
            var contact = enemies.Where(e => TargetsGroup(e, group) || heads.Any(h => h.Target == e && (h.AttackingNow || h.Casting?.Target == e))).ToList();
            var noticedEnemies = enemies.Where(e => contact.Contains(e) || contact.Any(c => c.DistanceTo(e) <= PartyNoticeRadius)).ToList();
            bool inContact = contact.Count > 0;

            // Work package 4: the stance.
            double mine = Power(w, group), theirs = Power(w, enemies.Where(e => plan.NoticedAt.ContainsKey(e) || contact.Contains(e)));
            double odds = mine + theirs <= 0 ? 0.5 : mine / (mine + theirs);
            bool lost = inContact && !heads.Any(h => h.Kit.Heals) && enemies.Any(e => e.Kit.Heals && plan.NoticedAt.ContainsKey(e)) && odds < LostOdds;
            Stance previous = plan.Stance;
            bool minHeld = w.Now - plan.StanceSince >= 5_000;
            if (plan.NoticedAt.Count == 0 && !inContact)
                plan.Stance = Stance.Quiet;
            else if (plan.Stance == Stance.Retreat)
            {
                Vec2 centre = Centre(heads);
                if (minHeld && !enemies.Any(e => e.Position.DistanceTo(centre) <= CallRadius) && odds >= EngageOdds)
                    plan.Stance = Stance.Engage;
            }
            else if (inContact && (lost || odds < RetreatOdds) && (minHeld || plan.Stance != Stance.Engage))
                plan.Stance = Stance.Retreat;
            else if (inContact)
                plan.Stance = Stance.Engage;
            else if (plan.Stance == Stance.Quiet)
            {
                plan.Stance = heads.Count >= 2 && enemies.Count(e => plan.NoticedAt.ContainsKey(e)) >= 2 ? Stance.Observe : Stance.Engage;
                plan.ObserveSince = w.Now;
            }
            else if (plan.Stance == Stance.Observe)
            {
                double factor = (0.5 + 5 / 9.0) * (0.8 + 0.4 * Roll(heads[0].Id, plan.ObserveSince, 0x57415443)); // patience 5 (no personality)
                if (w.Now - plan.ObserveSince >= Math.Min(20_000, (long)Math.Round(WatchBase * factor)))
                    plan.Stance = odds >= EngageOdds ? Stance.Engage : odds < RetreatOdds ? Stance.Retreat : Stance.Engage; // Kite is fought as Engage here
            }
            if (plan.Stance != previous)
            {
                plan.StanceSince = w.Now;
                w.Log($"UPSTREAM stance {previous} -> {plan.Stance} for {(a.Leader ?? a).Name}'s group (odds {odds:0.00}{(lost ? ", lost fight" : "")})");
            }

            bool runsPlan = plan.Stance == Stance.Engage && heads.Count >= 2;
            if (!runsPlan)
            {
                plan.Called = null;
                plan.CrowdControl.Clear();
                plan.Interrupts.Clear();
                return;
            }

            // Work package 2: the call.
            Vec2 friendly = Centre(heads);
            var candidates = noticedEnemies.Where(e => e.Position.DistanceTo(friendly) <= CallRadius && CanAggro(w, a, e)).ToList();
            var free = candidates.Where(e => !e.Mezzed(w.Now) && !e.Kit.IsPet).ToList();
            if (free.Count == 0)
                free = candidates.Where(e => e.Kit.IsPet && !e.Mezzed(w.Now)).ToList();
            if (free.Count == 0)
                free = candidates;
            if (free.Count == 0)
                plan.Called = null;
            else
            {
                long hold = 4_000 + (long)Math.Round(Roll(heads[0].Id, plan.CalledAt, 0x484f4c44) * 4_000);
                bool holding = plan.Called != null && free.Contains(plan.Called) && !plan.Called.Mezzed(w.Now) && w.Now - plan.CalledAt < hold;
                if (!holding)
                {
                    SimAgent pick = null;
                    if (Roll(heads[0].Id, w.Now, 0x57524f4e) < WrongCallShare)
                        pick = Best(free.Where(e => KillRoleOf(e) == KillRole.Tank), group, heads[0].Id, w.Now);
                    pick ??= Best(free, group, heads[0].Id, w.Now);
                    if (pick != plan.Called)
                        w.Log($"UPSTREAM call: {(a.Leader ?? a).Name}'s group on {pick.Label}");
                    plan.FightId = plan.FightId != 0 && w.Now - plan.LastCallAt <= 20_000 ? plan.FightId : Math.Max(1, w.Now);
                    plan.Called = pick;
                    plan.CalledAt = w.Now;
                }
                plan.LastCallAt = w.Now;
            }

            // Crowd-control plan: one subject per controller, never the kill target.
            var subjects = noticedEnemies.Where(e => !e.Kit.IsPet && e != plan.Called).ToList();
            foreach (SimAgent controller in plan.CrowdControl.Keys.ToList())
            {
                SimAgent subject = plan.CrowdControl[controller];
                bool held = subject.Controlled(w.Now);
                if (!controller.Alive || !subjects.Contains(subject) || controller.Controlled(w.Now) ||
                    !held && w.ImmuneToMez(subject))
                    plan.CrowdControl.Remove(controller);
            }
            foreach (SimAgent controller in heads.Where(h => h.IsBot && h.CcSpells.Count > 0 && !plan.CrowdControl.ContainsKey(h) && !h.Controlled(w.Now)))
            {
                SimAgent best = subjects.Where(e => !plan.CrowdControl.ContainsValue(e) && !e.Controlled(w.Now) && !w.ImmuneToMez(e))
                    .OrderByDescending(e => HoldScore(e, group)).ThenBy(e => e.Id).FirstOrDefault();
                if (best != null)
                    plan.CrowdControl[controller] = best;
            }

            // Interrupt duty: melee within 400 of a free enemy caster or healer, at most half the heads.
            var casters = noticedEnemies.Where(e => IsSpellcaster(e) && e != plan.Called && !e.Controlled(w.Now) && !plan.CrowdControl.ContainsValue(e))
                .OrderByDescending(e => (e.Kit.Heals ? 1_000 : 0) + Score(e, group)).ToList();
            foreach (SimAgent member in plan.Interrupts.Keys.ToList())
            {
                SimAgent subject = plan.Interrupts[member];
                if (!member.Alive || !casters.Contains(subject) || member.DistanceTo(subject) > 400 || member.Controlled(w.Now))
                    plan.Interrupts.Remove(member);
            }
            int cap = Math.Max(1, heads.Count / 2);
            foreach (SimAgent caster in casters)
            {
                if (plan.Interrupts.Count >= cap)
                    break;
                if (plan.Interrupts.ContainsValue(caster))
                    continue;
                SimAgent member = heads.Where(h => h.IsBot && h.Role is SimRole.Tank or SimRole.Melee && !plan.Interrupts.ContainsKey(h) &&
                                                   !plan.CrowdControl.ContainsKey(h) && !h.Controlled(w.Now) && h.DistanceTo(caster) <= 400)
                    .OrderBy(h => h.DistanceTo(caster)).FirstOrDefault();
                if (member != null)
                    plan.Interrupts[member] = caster;
            }
        }

        private SimAgent Best(IEnumerable<SimAgent> candidates, List<SimAgent> group, long seed, long now)
        {
            SimAgent best = null;
            double bestScore = double.MinValue;
            foreach (SimAgent e in candidates)
            {
                double score = Score(e, group) * (1 + Wobble * (2 * Roll(seed ^ e.Id, now, 0x41535349) - 1));
                if (score > bestScore)
                {
                    best = e;
                    bestScore = score;
                }
            }
            return best;
        }

        private static Vec2 Centre(List<SimAgent> heads) =>
            new(heads.Average(h => h.Position.X), heads.Average(h => h.Position.Y));

        // ---- the member ----------------------------------------------------------------------------------------------

        public override void Think(SimWorld w, SimAgent a)
        {
            if (!_salted)
            {
                _salted = true;
                _worldSalt = (ulong)w.Random.Next() << 21 ^ 0x5550313200000000UL;
            }
            GroupPlan plan = PlanOf(a);
            Board(w, a, plan);

            // Noticed enemies reach this member after its own reaction delay; an enemy that hits it is known at once.
            foreach (var pair in plan.NoticedAt)
                if (pair.Key.Alive && w.Now >= pair.Value + ReactionDelay(a, pair.Key) && a.DistanceTo(pair.Key) <= SimWorld.MaxAggroDistance &&
                    !a.Aggro.ContainsKey(pair.Key) && CanAggro(w, a, pair.Key))
                    a.Aggro[pair.Key] = 0.5;

            if (plan.Stance == Stance.Retreat)
            {
                var near = w.Enemies(a).Where(e => e.DistanceTo(a) <= SimWorld.MaxAggroDistance).ToList();
                if (near.Count > 0 && a.Casting == null)
                {
                    Vec2 threat = new(near.Average(e => e.Position.X), near.Average(e => e.Position.Y));
                    a.Target = null;
                    a.AttackingNow = false;
                    double d = Math.Max(1, threat.DistanceTo(a.Position));
                    a.MoveGoal = new Vec2(a.Position.X + (a.Position.X - threat.X) / d * 1_500, a.Position.Y + (a.Position.Y - threat.Y) / d * 1_500);
                    a.Activity = SimActivity.Fleeing;
                    a.Status = "upstream: group retreat";
                    return;
                }
            }

            if (plan.Stance == Stance.Observe && !(a.LastAttacker is { Alive: true } && w.Now - a.LastAttackedAt <= 5_000))
            {
                // Watching: no target is taken until the enemy comes to the group or the watch ends (a hit bot fights back).
                a.AttackingNow = false;
                if (!(a.Kit.Heals && TryHeal(w, a)))
                    StayWithLeader(w, a, "upstream: observing the enemy group");
                return;
            }

            base.Think(w, a);
        }

        /// <summary>RvrTacticsTarget: an interrupter goes for its caster; a member follows the call after its delay unless it tunnels.</summary>
        protected override SimAgent ChooseTarget(SimWorld w, SimAgent a, out string why)
        {
            GroupPlan plan = PlanOf(a);
            if (plan.Stance == Stance.Engage)
            {
                if (plan.Interrupts.TryGetValue(a, out SimAgent caster) && caster.Alive)
                {
                    if (!plan.NextLook.TryGetValue(a, out long next) || w.Now >= next)
                    {
                        (long min, long max) = LookRange;
                        plan.NextLook[a] = w.Now + min + (long)Math.Round(Roll(a.Id, w.Now, 0x4c4f4f4b) * (max - min));
                        plan.LookedCasting[a] = caster.Casting != null;
                    }
                    if (plan.LookedCasting.TryGetValue(a, out bool casting) && casting)
                    {
                        why = "";
                        return caster;
                    }
                }
                SimAgent called = plan.Called;
                bool tunnels = Roll(a.Id, plan.FightId, 0x54554e4e) < TunnelShare;
                (long dmin, long dmax) = FollowDelayRange;
                long delay = dmin + (long)Math.Round(Roll(a.Id, plan.CalledAt, 0x44454c41) * (dmax - dmin));
                if (called is { Alive: true } && !tunnels && w.Now - plan.CalledAt >= delay && CanAggro(w, a, called) &&
                    a.DistanceTo(called) <= SimWorld.MaxAggroDistance)
                {
                    if (!a.Aggro.ContainsKey(called))
                        a.Aggro[called] = 1;
                    why = "";
                    return called;
                }
            }
            return base.ChooseTarget(w, a, out why);
        }

        /// <summary>TryPlannedCrowdControl: only the plan's subject; area mez first when it spares the kill target.</summary>
        protected override bool TryCrowdControl(SimWorld w, SimAgent a)
        {
            if (w.Now < a.InterruptedUntil)
                return false;
            GroupPlan plan = PlanOf(a);
            if (plan.Stance != Stance.Engage || !plan.CrowdControl.TryGetValue(a, out SimAgent subject))
                return false;
            if (!subject.Alive || subject.Controlled(w.Now) || w.ImmuneToMez(subject) || !w.LineOfSight(a, subject))
                return false;
            foreach (SimSpell spell in a.CcSpells.OrderByDescending(s => s.Radius))
            {
                if (a.DistanceTo(subject) > spell.Range)
                    continue;
                if (spell.Radius > 0 && plan.Called != null && plan.Called.Alive && plan.Called.DistanceTo(subject) <= spell.Radius + 50)
                    continue;
                if (w.StartCast(a, spell, subject, spell.CastMilliseconds))
                {
                    a.Activity = SimActivity.Casting;
                    a.Status = $"upstream: {spell.Name} on its subject {subject.Name}";
                    return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>Upstream has no caster kite rule (BotCasterSurvival is this fork's): stand, get in range, cast.</summary>
        protected override void CasterTurn(SimWorld w, SimAgent a, SimAgent target)
        {
            if (a.DistanceTo(target) > SimWorld.NukeRange)
            {
                a.AttackingNow = false;
                a.MoveGoal = target.Position.Toward(a.Position, SimWorld.NukeRange - 100);
                a.Activity = SimActivity.Approaching;
                a.Status = "walking into spell range";
                return;
            }
            if (LineOfSightTurn(w, a, target))
                return;
            a.MoveGoal = null;
            if (w.Now < a.InterruptedUntil)
            {
                a.Activity = SimActivity.Interrupted;
                a.Status = "interrupted";
                return;
            }
            if (w.StartCast(a, null, target, SimWorld.NukeCastMilliseconds))
            {
                a.Activity = SimActivity.Casting;
                a.Status = "nuking " + target.Name;
            }
        }
    }
}

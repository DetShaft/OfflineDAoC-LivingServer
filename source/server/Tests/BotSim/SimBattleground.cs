using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using DOL.GS;
using Activity = DOL.GS.AutonomousBattlegroundActivity.Activity;
using Tier = DOL.GS.AutonomousBattlegroundActivity.Tier;

namespace DOL.UnitTests.BotSim
{
    /// <summary>
    /// Battleground goal simulation (owner, 2026-11-21): the goal layer of gamebots on a battleground tour, which the
    /// fight scenarios do not model. Three realms of twelve bots (levels 15-19, the same in every realm) share a
    /// three-fold symmetric field: the central keep in the middle, each realm's portal keep 20,000 out, and eight mob
    /// camps per realm sector. The scripted player (level 16) grinds a camp 10,000 out on its realm's side for an hour
    /// and never starts a fight. Fights are a simple damage race; there is no terrain, line of sight is everywhere
    /// (the real field has hills), and no keep siege. Live rules call the real AutonomousBattlegroundActivity
    /// (tiers, phases, engage radius, hunter avoidance and scoring, grind camp sharing, lurk and hunt points).
    /// Simulation results are tests only.
    /// </summary>
    public static class SimBattleground
    {
        public const string Name = "battleground-goals";
        public const int BotsPerRealm = 12;
        public static readonly int[] Levels = { 15, 15, 16, 16, 16, 17, 17, 18, 18, 19, 19, 19 };
        public const int PlayerLevel = 16;
        public const float FieldRadius = 20_000f;
        public const float Speed = 191f;
        public const int Seconds = 3_600;
        public const float MeleeReach = 150f;
        public const int RespawnSeconds = 30;
        /// <summary>Seeds 1..3, or 1..BOT_SIM_SEEDS (at most 30), like the fight scenarios.</summary>
        public static int[] DefaultSeeds =>
            int.TryParse(Environment.GetEnvironmentVariable("BOT_SIM_SEEDS"), out int n) && n > 0
                ? Enumerable.Range(1, Math.Min(n, 30)).ToArray()
                : new[] { 1, 2, 3 };

        public enum Rules { Before, Live }

        public sealed class Agent
        {
            public int Id;
            public eRealm Realm;
            public int RealmIndex;
            public int Level;
            public long Key;
            public bool IsPlayer;
            public Vector2 Pos;
            public double Health = 100;
            public bool Alive = true;
            public int RespawnAt;
            public Agent Target;
            public Activity Activity = Activity.KeepFights;
            public int Camp = -1;
            public bool AtCamp;
            public Vector2? Roam;
            public int RoamUntil;
            public Vector2? Avoid;
            public Random Rng;
            // Metrics
            public int GrindSeconds, KeepSeconds, HuntSeconds, FightSeconds, DeadSeconds, SharedGrindSeconds;
            public int MobKills, Kills, CamperKills, Deaths, GroupEngagements, Engagements;
            public double MobProgress;
        }

        public sealed class Run
        {
            public Rules Rules;
            public int Seed, Rotation;
            public eRealm PlayerRealm;
            public List<Agent> Agents = new();
            public Agent Player;
            public int FirstEnemyAtPlayer = -1;
            public double EnemyNearPlayerSeconds;
            public int PlayerDeaths;
            public int PlayerMobKills;
            public int MaxAttackersOnPlayer;
        }

        private static readonly eRealm[] Realms = SimRoster.Realms;

        private static Vector2 Polar(double radius, double degrees) =>
            new((float)(radius * Math.Cos(degrees * Math.PI / 180)), (float)(radius * Math.Sin(degrees * Math.PI / 180)));

        private static double SectorAngle(int realmIndex) => 90 + realmIndex * 120;

        public static Vector2 Portal(int realmIndex) => Polar(FieldRadius, SectorAngle(realmIndex));

        // Eight camps per realm sector (offset from the sector's portal angle); the 60-degree ones lie between two realms.
        private static readonly (double Radius, double Offset)[] SectorCamps =
            { (6_000, -40), (6_000, 40), (10_000, 0), (10_000, -35), (10_000, 35), (14_000, -25), (14_000, 25), (12_000, 60) };

        public static Vector2[] Camps() =>
            Enumerable.Range(0, 3).SelectMany(r => SectorCamps.Select(c => Polar(c.Radius, SectorAngle(r) + c.Offset))).ToArray();

        public static int PlayerCamp(int realmIndex) => realmIndex * SectorCamps.Length + 2;

        public static List<Run> RunAll(int[] seeds = null)
        {
            var runs = new List<Run>();
            foreach (Rules rules in new[] { Rules.Before, Rules.Live })
                foreach (int seed in seeds ?? DefaultSeeds)
                    for (int rotation = 0; rotation < 3; rotation++)
                        foreach (eRealm player in Realms)
                            runs.Add(RunOne(rules, seed, rotation, player));
            return runs;
        }

        public static Run RunOne(Rules rules, int seed, int rotation, eRealm playerRealm)
        {
            var run = new Run { Rules = rules, Seed = seed, Rotation = rotation, PlayerRealm = playerRealm };
            Vector2[] camps = Camps();
            int id = 0;
            for (int r = 0; r < 3; r++)
                for (int slot = 0; slot < BotsPerRealm; slot++)
                {
                    // Key sets rotate between the realms so every realm plays every key set (realm evenness).
                    int keySet = (r + rotation) % 3;
                    long key = 1_000 + keySet * 100 + slot;
                    run.Agents.Add(new Agent
                    {
                        Id = id++, Realm = Realms[r], RealmIndex = r, Level = Levels[slot], Key = key,
                        Pos = Portal(r) * 0.95f, Rng = new Random(seed * 7919 + (int)key),
                    });
                }
            int playerIndex = Array.IndexOf(Realms, playerRealm);
            run.Player = new Agent
            {
                Id = id, Realm = playerRealm, RealmIndex = playerIndex, Level = PlayerLevel, Key = -1, IsPlayer = true,
                Pos = camps[PlayerCamp(playerIndex)], Camp = PlayerCamp(playerIndex), AtCamp = true, Activity = Activity.Grind,
                Rng = new Random(seed),
            };
            run.Agents.Add(run.Player);
            var fightRng = new Random(seed * 31 + rotation);
            long startMs = seed * 7L * 60_000;

            for (int t = 0; t < Seconds; t++)
                Step(run, camps, t, startMs + t * 1_000L, fightRng);
            return run;
        }

        private static IEnumerable<Agent> Enemies(Run run, Agent a) => run.Agents.Where(o => o.Alive && o.Realm != a.Realm);

        private static bool FightingMonster(Agent a) => a.AtCamp && a.Target == null;

        private static void Step(Run run, Vector2[] camps, int t, long nowMs, Random fightRng)
        {
            var agents = run.Agents;
            // Respawns.
            foreach (Agent a in agents.Where(x => !x.Alive))
            {
                a.DeadSeconds++;
                if (t < a.RespawnAt) continue;
                a.Alive = true;
                a.Health = 100;
                a.Pos = Portal(a.RealmIndex) * 0.95f;
                a.Target = null;
                a.AtCamp = false;
                a.Roam = null;
                if (!a.IsPlayer) a.Camp = -1;
            }

            foreach (Agent a in agents.Where(x => x.Alive))
            {
                if (a.Target != null && (!a.Target.Alive)) a.Target = null;
                if (!a.IsPlayer) Decide(run, a, camps, t, nowMs);
                else if (a.Target == null)
                {
                    // The player fights back at whoever hits it, else walks back to its camp and grinds.
                    Agent hitter = Enemies(run, a).Where(o => o.Target == a).OrderBy(o => Vector2.Distance(o.Pos, a.Pos)).FirstOrDefault();
                    a.Target = hitter;
                    Vector2 camp = camps[a.Camp];
                    if (a.Target == null && Vector2.Distance(a.Pos, camp) > 300) { a.AtCamp = false; a.Pos = Toward(a.Pos, camp, Speed); }
                    else if (a.Target == null) a.AtCamp = true;
                }

                if (a.Target != null)
                {
                    float d = Vector2.Distance(a.Pos, a.Target.Pos);
                    if (d > MeleeReach) a.Pos = Toward(a.Pos, a.Target.Pos, Speed);
                    a.FightSeconds++;
                    continue;
                }
                if (a.IsPlayer)
                {
                    if (a.AtCamp && (a.MobProgress += 1.0 / 30) >= 1) { a.MobProgress = 0; run.PlayerMobKills++; }
                    a.Health = Math.Min(100, a.Health + 1);
                    continue;
                }
                switch (a.Activity)
                {
                    case Activity.Grind: a.GrindSeconds++; break;
                    case Activity.KeepFights: a.KeepSeconds++; break;
                    default: a.HuntSeconds++; break;
                }
                if (a.AtCamp)
                {
                    if (agents.Any(o => o != a && o.Alive && !o.IsPlayer && o.Realm == a.Realm && o.Camp == a.Camp && o.AtCamp))
                        a.SharedGrindSeconds++;
                    // A mob every 30 s.
                    if ((a.MobProgress += 1.0 / 30) >= 1) { a.MobProgress = 0; a.MobKills++; }
                    a.Health = Math.Max(20, a.Health - 0.6);
                }
                else a.Health = Math.Min(100, a.Health + 2);
            }

            // Damage: each fighter in reach hits its target.
            foreach (Agent a in agents.Where(x => x.Alive && x.Target != null))
            {
                if (Vector2.Distance(a.Pos, a.Target.Pos) > MeleeReach) continue;
                double damage = (5 + (a.Level - 15) * 0.6) * (0.8 + fightRng.NextDouble() * 0.4);
                Agent victim = a.Target;
                victim.Health -= damage;
                if (victim.Target == null) victim.Target = a; // fight back
                if (victim.Health > 0 || !victim.Alive) continue;
                victim.Alive = false;
                victim.Deaths++;
                victim.RespawnAt = t + RespawnSeconds;
                bool camper = victim.AtCamp;
                victim.AtCamp = false;
                victim.Target = null;
                a.Kills++;
                if (camper) a.CamperKills++;
                if (victim.IsPlayer) run.PlayerDeaths++;
            }

            // Player pressure.
            Agent p = run.Player;
            if (p.Alive)
            {
                int near = agents.Count(o => o.Alive && o.Realm != p.Realm && Vector2.Distance(o.Pos, p.Pos) <= 1_500);
                run.EnemyNearPlayerSeconds += near;
                if (near > 0 && run.FirstEnemyAtPlayer < 0) run.FirstEnemyAtPlayer = t;
                run.MaxAttackersOnPlayer = Math.Max(run.MaxAttackersOnPlayer, agents.Count(o => o.Alive && o.Target == p));
            }
        }

        private static Vector2 Toward(Vector2 from, Vector2 to, float step)
        {
            Vector2 delta = to - from;
            float length = delta.Length();
            return length <= step ? to : from + delta / length * step;
        }

        private static void Decide(Run run, Agent a, Vector2[] camps, int t, long nowMs)
        {
            bool live = run.Rules == Rules.Live;
            Activity activity = live ? AutonomousBattlegroundActivity.ChooseNow(a.Level, 15, 19, a.Key, nowMs) : Activity.KeepFights;
            if (activity != a.Activity)
            {
                a.Activity = activity;
                a.Camp = -1;
                a.AtCamp = false;
                a.Roam = null;
                a.Avoid = null;
            }
            // Chases end 6,000 out (the aggro list's drop distance).
            if (a.Target != null && Vector2.Distance(a.Pos, a.Target.Pos) > 6_000) a.Target = null;
            if (a.Target != null) return;

            // Threat scan (TryEngageFrontierThreat): before, every bot took the nearest enemy within 5,000.
            int radius = live ? AutonomousBattlegroundActivity.EngageRadius(activity) : 5_000;
            Agent[] scan = Enemies(run, a).Where(o => Vector2.Distance(o.Pos, a.Pos) <= 5_000).ToArray();
            bool FiringOnRealm(Agent o) => o.Target != null && o.Target.Realm == a.Realm && Vector2.Distance(o.Target.Pos, a.Pos) <= 1_000;
            IEnumerable<Agent> candidates = scan.Where(o => FiringOnRealm(o) || Vector2.Distance(o.Pos, a.Pos) <= radius);
            int EnemiesAround(Agent o) => scan.Count(x => x.Realm == o.Realm && Vector2.Distance(x.Pos, o.Pos) <= AutonomousBattlegroundActivity.GroupRadius);
            Agent pick;
            if (live && activity == Activity.OutskirtHunt)
            {
                int friends = run.Agents.Count(x => x.Alive && x.Realm == a.Realm &&
                    Vector2.Distance(x.Pos, a.Pos) <= AutonomousBattlegroundActivity.GroupRadius);
                Agent[] all = candidates.ToArray();
                Agent[] kept = all.Where(o => FiringOnRealm(o) || !AutonomousBattlegroundActivity.HunterAvoids(EnemiesAround(o), friends)).ToArray();
                if (kept.Length == 0 && all.Length > 0)
                {
                    a.Avoid = all[0].Pos;
                    a.Roam = null;
                }
                pick = kept.OrderBy(o => AutonomousBattlegroundActivity.HuntScore(Vector2.Distance(o.Pos, a.Pos), a.Level, o.Level,
                    FightingMonster(o), EnemiesAround(o))).FirstOrDefault();
            }
            else pick = candidates.OrderBy(o => Vector2.Distance(o.Pos, a.Pos)).FirstOrDefault();
            if (pick != null)
            {
                a.Target = pick;
                a.AtCamp = false;
                a.Engagements++;
                if (EnemiesAround(pick) >= 3) a.GroupEngagements++;
                return;
            }

            if (activity == Activity.Grind)
            {
                if (a.Camp < 0)
                {
                    int[] counts = camps.Select((_, i) => run.Agents.Count(o => o != a && !o.IsPlayer && o.Alive && o.Realm == a.Realm &&
                        o.Activity == Activity.Grind && o.Camp == i)).ToArray();
                    a.Camp = AutonomousBattlegroundActivity.PickGrindCamp(counts, a.Rng.NextDouble());
                    if (a.Camp < 0) { a.Activity = Activity.KeepFights; return; }
                }
                Vector2 camp = camps[a.Camp];
                if (Vector2.Distance(a.Pos, camp) > 300) { a.AtCamp = false; a.Pos = Toward(a.Pos, camp, Speed); }
                else a.AtCamp = true;
                return;
            }

            if (!a.Roam.HasValue || t >= a.RoamUntil || Vector2.Distance(a.Pos, a.Roam.Value) < 200)
            {
                Vector2[] enemyPortals = Enumerable.Range(0, 3).Where(r => r != a.RealmIndex).Select(Portal).ToArray();
                if (!live)
                {
                    // RoamBattleground before: the keep, or 40% of the way from the keep to an enemy portal.
                    Vector2[] anchors = enemyPortals.Select(p => Vector2.Lerp(p, Vector2.Zero, 0.6f)).Append(Vector2.Zero).ToArray();
                    a.Roam = anchors[a.Rng.Next(anchors.Length)] + new Vector2((float)(a.Rng.NextDouble() - 0.5) * 1_800, (float)(a.Rng.NextDouble() - 0.5) * 1_800);
                    a.RoamUntil = t + 120 + (int)(a.Key % 30);
                }
                else if (activity == Activity.KeepFights)
                {
                    a.Roam = AutonomousBattlegroundActivity.KeepLurkPoint(Vector2.Zero, a.Rng.NextDouble(), a.Rng.NextDouble());
                    a.RoamUntil = t + 120 + (int)(a.Key % 30);
                }
                else
                {
                    a.Roam = AutonomousBattlegroundActivity.HuntPoint(Vector2.Zero, enemyPortals, camps, FieldRadius, a.Avoid, a.Rng);
                    a.RoamUntil = t + 90 + (int)(a.Key % 30);
                }
            }
            a.Pos = Toward(a.Pos, a.Roam.Value, Speed);
        }

        // ---------------------------------------------------------------- report

        private static string P(double share) => (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        private static Tier TierOf(Agent a) => AutonomousBattlegroundActivity.TierOf(a.Level, 15, 19);

        public static List<SimFinding> Evaluate(List<Run> runs)
        {
            var findings = new List<SimFinding>();
            foreach (Rules rules in new[] { Rules.Before, Rules.Live })
            {
                var set = runs.Where(r => r.Rules == rules).ToList();
                var bots = set.SelectMany(r => r.Agents.Where(a => !a.IsPlayer)).ToList();
                string variant = rules == Rules.Live ? "live (tour phases)" : "before";
                bool isLive = rules == Rules.Live;
                void Add(string text, bool ok, string measured) =>
                    findings.Add(new SimFinding { Scenario = Name, Variant = variant, Expectation = text, Measured = measured, Ok = ok, Live = isLive });

                double GrindShare(Tier tier)
                {
                    var tierBots = bots.Where(b => TierOf(b) == tier).ToList();
                    double alive = tierBots.Sum(b => Seconds - b.DeadSeconds);
                    return alive == 0 ? 0 : tierBots.Sum(b => b.GrindSeconds) / alive;
                }
                double low = GrindShare(Tier.Low), high = GrindShare(Tier.High);
                Add("Low-tier bots (15-16) spend at least 40% of their time grinding", low >= 0.40, P(low));
                Add("High-tier bots (18-19) spend at most 20% of their time grinding", high <= 0.20, P(high));
                int engagements = bots.Sum(b => b.Engagements), group = bots.Sum(b => b.GroupEngagements);
                double groupShare = engagements == 0 ? 0 : (double)group / engagements;
                Add("Fewer than 35% of fights started against an enemy group of 3+", groupShare < 0.35, $"{P(groupShare)} of {engagements}");
                int camperKills = bots.Sum(b => b.CamperKills);
                Add("Bots kill campers (grinding enemies) at least once a run", camperKills >= set.Count, $"{F1((double)camperKills / set.Count)} per run");
                double grind = bots.Sum(b => b.GrindSeconds);
                double shared = grind == 0 ? 0 : bots.Sum(b => b.SharedGrindSeconds) / grind;
                Add("Grinders share a camp with a realm-mate at least 50% of grind time", shared >= 0.5, grind == 0 ? "no grinding" : P(shared));
                double firstContact = set.Average(r => r.FirstEnemyAtPlayer < 0 ? Seconds : r.FirstEnemyAtPlayer);
                Add("First enemy within 1,500 of the grinding player after 3+ minutes on average", firstContact >= 180, $"{firstContact:0} s");
            }
            return findings;
        }

        public static string Markdown(List<Run> runs, List<SimFinding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Battleground goals: grind, keep fights, outskirt hunt");
            sb.AppendLine();
            sb.AppendLine("Owner, 2026-11-21: bots ran straight across the battleground at the player. The tour now runs in 10-16 minute phases of grinding, keep fights or outskirt hunting by level tier (AutonomousBattlegroundActivity). " +
                          $"Three realms x {BotsPerRealm} bots (levels {string.Join(",", Levels)}, same in every realm), one scripted level {PlayerLevel} player grinding a camp 10,000 from the keep on its side, {Seconds / 60} simulated minutes. " +
                          "Flat field, line of sight everywhere, no siege, a simple damage race. Simulation results are tests only, not in-game evidence.");
            sb.AppendLine();
            int perVariant = runs.Count / 2;
            sb.AppendLine($"Runs: {perVariant} per variant (3 player realms x 3 key-set rotations x {runs.Select(r => r.Seed).Distinct().Count()} seeds).");
            sb.AppendLine();
            sb.AppendLine("Variants:");
            sb.AppendLine("- **before**: every bot takes the nearest enemy within 5,000 and roams between the keep and points 40% of the way to the enemy portals.");
            sb.AppendLine("- **live (tour phases)**: grind 1,800 engage, shared camps; keep fights 3,500 engage, lurk 2,000-4,000 round the keep; outskirt hunt 5,000 engage, avoids groups of 3+ that outnumber it, campers and lower levels first.");
            sb.AppendLine();
            sb.AppendLine("## Expectations");
            sb.AppendLine();
            sb.AppendLine("| Variant | Result | Expectation | Measured |");
            sb.AppendLine("|---|---|---|---|");
            foreach (SimFinding f in findings)
                sb.AppendLine($"| {f.Variant} | {(f.Ok ? "pass" : "**FINDING**")} | {f.Expectation} | {f.Measured} |");
            sb.AppendLine();

            sb.AppendLine("## The grinding player");
            sb.AppendLine();
            sb.AppendLine("| Variant | First enemy within 1,500 (mean) | Enemy-seconds within 1,500 / hour | Player deaths / hour | Most bots on the player at once | Player mob kills / hour |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var g in runs.GroupBy(r => r.Rules))
                sb.AppendLine($"| {Label(g.Key)} | {g.Average(r => r.FirstEnemyAtPlayer < 0 ? Seconds : r.FirstEnemyAtPlayer):0} s | {g.Average(r => r.EnemyNearPlayerSeconds):0} | {F1(g.Average(r => r.PlayerDeaths))} | {F1(g.Average(r => r.MaxAttackersOnPlayer))} | {F1(g.Average(r => r.PlayerMobKills))} |");
            sb.AppendLine();

            sb.AppendLine("## Bot time by level tier");
            sb.AppendLine();
            sb.AppendLine("Share of each bot's time alive. Fighting is time with an enemy target.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Tier | Grinding | Keep fights | Outskirt hunt | Fighting | Mob kills / bot / hour | Kills / bot | Camper kills / bot | Deaths / bot |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in runs.GroupBy(r => r.Rules))
                foreach (Tier tier in new[] { Tier.Low, Tier.Mid, Tier.High })
                {
                    var bots = g.SelectMany(r => r.Agents.Where(a => !a.IsPlayer && TierOf(a) == tier)).ToList();
                    double alive = Math.Max(1, bots.Sum(b => Seconds - b.DeadSeconds));
                    sb.AppendLine($"| {Label(g.Key)} | {tier} | {P(bots.Sum(b => b.GrindSeconds) / alive)} | {P(bots.Sum(b => b.KeepSeconds) / alive)} | {P(bots.Sum(b => b.HuntSeconds) / alive)} | {P(bots.Sum(b => b.FightSeconds) / alive)} | " +
                                  $"{F1(bots.Average(b => b.MobKills))} | {F1(bots.Average(b => b.Kills))} | {F1(bots.Average(b => b.CamperKills))} | {F1(bots.Average(b => b.Deaths))} |");
                }
            sb.AppendLine();

            sb.AppendLine("## Bots by realm");
            sb.AppendLine();
            sb.AppendLine("Every realm has the same levels, and the key sets rotate between the realms, so the rows should be close. They are not exact: fights depend on who meets whom first.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Realm | Grinding | Keep fights | Outskirt hunt | Mob kills / bot | Kills / bot | Deaths / bot | Fights vs groups of 3+ |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var g in runs.GroupBy(r => r.Rules))
                foreach (eRealm realm in Realms)
                {
                    var bots = g.SelectMany(r => r.Agents.Where(a => !a.IsPlayer && a.Realm == realm)).ToList();
                    double alive = Math.Max(1, bots.Sum(b => Seconds - b.DeadSeconds));
                    int eng = bots.Sum(b => b.Engagements);
                    sb.AppendLine($"| {Label(g.Key)} | {SimRoster.Short(realm)} | {P(bots.Sum(b => b.GrindSeconds) / alive)} | {P(bots.Sum(b => b.KeepSeconds) / alive)} | {P(bots.Sum(b => b.HuntSeconds) / alive)} | " +
                                  $"{F1(bots.Average(b => b.MobKills))} | {F1(bots.Average(b => b.Kills))} | {F1(bots.Average(b => b.Deaths))} | {(eng == 0 ? "-" : P((double)bots.Sum(b => b.GroupEngagements) / eng))} |");
                }
            sb.AppendLine();
            return sb.ToString();

            static string Label(Rules rules) => rules == Rules.Live ? "live (tour phases)" : "before";
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

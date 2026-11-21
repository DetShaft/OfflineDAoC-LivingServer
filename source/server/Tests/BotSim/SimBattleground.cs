using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using DOL.GS;
using Activity = DOL.GS.AutonomousBattlegroundActivity.Activity;
using SiegeResponse = DOL.GS.AutonomousBattlegroundActivity.SiegeResponse;
using Tier = DOL.GS.AutonomousBattlegroundActivity.Tier;

namespace DOL.UnitTests.BotSim
{
    /// <summary>
    /// Battleground goal simulation (owner, 2026-11-21): the goal layer of gamebots on a battleground tour, which the
    /// fight scenarios do not model. Three realms of eighteen bots (levels 15-19, the same in every realm) share a
    /// three-fold symmetric field: the central keep in the middle (held by one realm, rotating between runs), each
    /// realm's portal keep 20,000 out, and eight mob camps per realm sector. The scripted player (level 16) grinds a
    /// camp 10,000 out on its realm's side for an hour and never starts a fight. Twice an hour a scripted raid of eight
    /// level 18 attackers of the next realm marches on the keep. Fights are a simple damage race; there is no terrain,
    /// line of sight is everywhere (the real field has hills), and the keep has no guards or doors: it falls after two
    /// minutes with three attackers and no defender at it. Rules call the real AutonomousBattlegroundActivity (tiers,
    /// phases, engage radius, hunter avoidance, scoring and camps of three, grind camp sharing, lurk and hunt points,
    /// siege responses and party forming). Simulation results are tests only.
    /// </summary>
    public static class SimBattleground
    {
        public const string Name = "battleground-goals";
        public static readonly int[] Levels = { 15, 15, 15, 16, 16, 16, 16, 17, 17, 17, 18, 18, 18, 18, 19, 19, 19, 19 };
        public static int BotsPerRealm => Levels.Length;
        public const int PlayerLevel = 16;
        public const float FieldRadius = 20_000f;
        public const float Speed = 191f;
        public const int Seconds = 3_600;
        public const float MeleeReach = 150f;
        public const int RespawnSeconds = 30;
        public const int RaidSize = 8, RaidLevel = 18;
        public static readonly int[] RaidStarts = { 600, 2_400 };
        public const float KeepReach = 900f, DefenderReach = 1_200f;
        public const int CaptureSeconds = 120;
        /// <summary>Seeds 1..3, or 1..BOT_SIM_SEEDS (at most 30), like the fight scenarios.</summary>
        public static int[] DefaultSeeds =>
            int.TryParse(Environment.GetEnvironmentVariable("BOT_SIM_SEEDS"), out int n) && n > 0
                ? Enumerable.Range(1, Math.Min(n, 30)).ToArray()
                : new[] { 1, 2, 3 };

        /// <summary>Before: one rule for every bot. Phases: PR #187. Live: #187 plus parties, siege responses and camps of three.</summary>
        public enum Rules { Before, Phases, Live }

        public static readonly Rules[] AllRules = { Rules.Before, Rules.Phases, Rules.Live };

        public static string Label(Rules rules) => rules switch
        {
            Rules.Before => "before",
            Rules.Phases => "phases (#187)",
            _ => "live (parties, sieges)",
        };

        public sealed class Agent
        {
            public int Id;
            public eRealm Realm;
            public int RealmIndex;
            public int Level;
            public long Key;
            public bool IsPlayer, IsRaider;
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
            public Agent Leader;
            public Activity PartyActivity;
            public int PartyCamp = -1;
            public SiegeResponse Response;
            // Metrics
            public int GrindSeconds, KeepSeconds, HuntSeconds, FightSeconds, DeadSeconds, SharedGrindSeconds, PartySeconds;
            public int MobKills, Kills, CamperKills, Deaths, GroupEngagements, Engagements, CampOfThreeEngagements, RaiderKills;
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
            // The central keep.
            public int StartHolder, Holder;
            public int LastPressure = -10_000;
            public int[] PressureBy = { -10_000, -10_000, -10_000 };
            public double CaptureProgress;
            public int Captures, RaidCaptures, SiegeSeconds;
            public double DefenderSecondsInSiege;
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
            foreach (Rules rules in AllRules)
                foreach (int seed in seeds ?? DefaultSeeds)
                    for (int rotation = 0; rotation < 3; rotation++)
                        foreach (eRealm player in Realms)
                            runs.Add(RunOne(rules, seed, rotation, player));
            return runs;
        }

        public static Run RunOne(Rules rules, int seed, int rotation, eRealm playerRealm)
        {
            var run = new Run { Rules = rules, Seed = seed, Rotation = rotation, PlayerRealm = playerRealm };
            run.StartHolder = run.Holder = rotation;
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
                Id = id++, Realm = playerRealm, RealmIndex = playerIndex, Level = PlayerLevel, Key = -1, IsPlayer = true,
                Pos = camps[PlayerCamp(playerIndex)], Camp = PlayerCamp(playerIndex), AtCamp = true, Activity = Activity.Grind,
                Rng = new Random(seed),
            };
            run.Agents.Add(run.Player);
            var fightRng = new Random(seed * 31 + rotation);
            long startMs = seed * 7L * 60_000;

            for (int t = 0; t < Seconds; t++)
            {
                if (RaidStarts.Contains(t))
                {
                    // The raid comes from the realm after the keep's current holder.
                    int raidRealm = (run.Holder + 1) % 3;
                    for (int i = 0; i < RaidSize; i++)
                        run.Agents.Add(new Agent
                        {
                            Id = id++, Realm = Realms[raidRealm], RealmIndex = raidRealm, Level = RaidLevel, Key = -100 - i,
                            IsRaider = true, Pos = Portal(raidRealm) * 0.9f + new Vector2(i * 60, 0), Rng = new Random(seed + i),
                        });
                }
                Step(run, camps, t, startMs + t * 1_000L, fightRng);
            }
            return run;
        }

        private static IEnumerable<Agent> Enemies(Run run, Agent a) => run.Agents.Where(o => o.Alive && o.Realm != a.Realm);

        private static bool FightingMonster(Agent a) => a.AtCamp && a.Target == null;

        private static bool IsBot(Agent a) => !a.IsPlayer && !a.IsRaider;

        private static void Step(Run run, Vector2[] camps, int t, long nowMs, Random fightRng)
        {
            var agents = run.Agents;
            // Respawns (raiders do not come back).
            foreach (Agent a in agents.Where(x => !x.Alive && !x.IsRaider))
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

            if (run.Rules == Rules.Live && t % 10 == 0)
                Parties(run, nowMs);

            foreach (Agent a in agents.Where(x => x.Alive))
            {
                if (a.Target != null && (!a.Target.Alive)) a.Target = null;
                if (a.IsRaider) { Raid(run, a); }
                else if (!a.IsPlayer) Decide(run, a, camps, t, nowMs);
                else if (a.Target == null)
                {
                    // The player fights back at whoever hits it, else walks back to its camp and grinds.
                    Agent hitter = Enemies(run, a).Where(o => o.Target == a).OrderBy(o => Vector2.Distance(o.Pos, a.Pos)).FirstOrDefault();
                    a.Target = hitter;
                    Vector2 camp = camps[a.Camp];
                    if (a.Target == null && Vector2.Distance(a.Pos, camp) > 300) { a.AtCamp = false; a.Pos = Toward(a.Pos, camp, Speed); }
                    else if (a.Target == null) a.AtCamp = true;
                }

                if (a.Leader != null && IsBot(a)) a.PartySeconds++;
                if (a.Target != null)
                {
                    float d = Vector2.Distance(a.Pos, a.Target.Pos);
                    if (d > MeleeReach) a.Pos = Toward(a.Pos, a.Target.Pos, Speed);
                    a.FightSeconds++;
                    continue;
                }
                if (a.IsRaider) continue;
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
                    if (agents.Any(o => o != a && o.Alive && IsBot(o) && o.Realm == a.Realm && o.Camp == a.Camp && o.AtCamp))
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
                // Party members come to a member's aid (group defense).
                if (run.Rules == Rules.Live && victim.Leader != null)
                    foreach (Agent mate in agents.Where(o => o.Alive && o.Leader == victim.Leader && o.Target == null &&
                                                             Vector2.Distance(o.Pos, victim.Pos) <= 2_000))
                        mate.Target = a;
                if (victim.Health > 0 || !victim.Alive) continue;
                victim.Alive = false;
                victim.Deaths++;
                victim.RespawnAt = t + RespawnSeconds;
                bool camper = victim.AtCamp;
                victim.AtCamp = false;
                victim.Target = null;
                a.Kills++;
                if (camper) a.CamperKills++;
                if (victim.IsRaider) a.RaiderKills++;
                if (victim.IsPlayer) run.PlayerDeaths++;
            }

            Keep(run, t);

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

        /// <summary>Siege pressure and capture: three attackers at the keep with no defender take it in two minutes.</summary>
        private static void Keep(Run run, int t)
        {
            var near = run.Agents.Where(o => o.Alive && !o.IsPlayer && o.RealmIndex != run.Holder && o.Pos.Length() <= KeepReach).ToArray();
            int defenders = run.Agents.Count(o => o.Alive && !o.IsPlayer && o.RealmIndex == run.Holder && o.Pos.Length() <= DefenderReach);
            if (near.Length == 0)
            {
                run.CaptureProgress = Math.Max(0, run.CaptureProgress - 1);
                return;
            }
            run.LastPressure = t;
            foreach (Agent attacker in near) run.PressureBy[attacker.RealmIndex] = t;
            run.SiegeSeconds++;
            run.DefenderSecondsInSiege += defenders;
            if (near.Length >= 3 && defenders == 0) run.CaptureProgress++;
            else if (defenders > 0) run.CaptureProgress = Math.Max(0, run.CaptureProgress - 2);
            if (run.CaptureProgress < CaptureSeconds) return;
            int taker = near.GroupBy(o => o.RealmIndex).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            if (near.Any(o => o.IsRaider && o.RealmIndex == taker)) run.RaidCaptures++;
            run.Holder = taker;
            run.Captures++;
            run.CaptureProgress = 0;
            run.LastPressure = -10_000;
            run.PressureBy = new[] { -10_000, -10_000, -10_000 };
        }

        /// <summary>The scripted raid marches on the keep and fights whatever stands within 1,000 of it.</summary>
        private static void Raid(Run run, Agent a)
        {
            if (a.Target != null) return;
            Agent pick = Enemies(run, a).Where(o => Vector2.Distance(o.Pos, a.Pos) <= 1_000).OrderBy(o => Vector2.Distance(o.Pos, a.Pos)).FirstOrDefault();
            if (pick != null) { a.Target = pick; return; }
            if (a.RealmIndex == run.Holder) return; // the raid's realm took the keep: it holds there
            if (a.Pos.Length() > 400) a.Pos = Toward(a.Pos, Vector2.Zero, Speed);
        }

        /// <summary>Battleground parties (AutonomousBattlegroundParties): prune members off the party's activity, form new ones.</summary>
        private static void Parties(Run run, long nowMs)
        {
            var bots = run.Agents.Where(IsBot).ToList();
            foreach (Agent a in bots.Where(b => b.Leader != null))
                if (a.Activity != a.PartyActivity || a.PartyActivity == Activity.Grind && a.Camp != a.PartyCamp ||
                    a.Leader.Leader != a.Leader)
                    a.Leader = null;
            foreach (var party in bots.Where(b => b.Leader != null).GroupBy(b => b.Leader).ToArray())
                if (party.Count() < 2)
                    foreach (Agent a in party) a.Leader = null;

            var free = bots.Where(b => b.Alive && b.Leader == null && b.Target == null &&
                                       (b.Activity != Activity.OutskirtHunt || AutonomousBattlegroundActivity.HuntsInParty(b.Key)) &&
                                       (b.Activity != Activity.Grind || b.Camp >= 0))
                .GroupBy(b => (b.RealmIndex, b.Activity, Camp: b.Activity == Activity.Grind ? b.Camp : -1));
            foreach (var cohort in free)
            {
                Agent[] members = cohort.OrderBy(b => b.Key).ToArray();
                foreach (int[] indexes in AutonomousBattlegroundActivity.FormParties(members.Select(b => b.Pos).ToArray(), cohort.Key.Activity))
                {
                    Agent leader = members[indexes[0]];
                    foreach (int i in indexes)
                    {
                        members[i].Leader = leader;
                        members[i].PartyActivity = cohort.Key.Activity;
                        members[i].PartyCamp = cohort.Key.Camp;
                    }
                }
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
            bool phases = run.Rules != Rules.Before;
            bool full = run.Rules == Rules.Live;
            Activity activity = phases ? AutonomousBattlegroundActivity.ChooseNow(a.Level, 15, 19, a.Key, nowMs) : Activity.KeepFights;
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

            // Siege of the central keep (live): RespondToSiege.
            bool own = a.RealmIndex == run.Holder;
            float toKeep = a.Pos.Length();
            a.Response = SiegeResponse.None;
            if (full)
            {
                bool active = own
                    ? t - run.LastPressure <= AutonomousBattlegroundActivity.SiegeActiveMilliseconds / 1000
                    : Enumerable.Range(0, 3).Any(r => r != a.RealmIndex && r != run.Holder &&
                                                      t - run.PressureBy[r] <= AutonomousBattlegroundActivity.SiegeActiveMilliseconds / 1000);
                if (active) a.Response = AutonomousBattlegroundActivity.RespondToSiege(activity, own, toKeep, a.Key);
                if (a.Response == SiegeResponse.Defend) { a.AtCamp = false; a.Camp = -1; }
            }
            if (a.Target != null) return;
            Activity effective = a.Response == SiegeResponse.Defend ? Activity.KeepFights : activity;

            // Threat scan (TryEngageFrontierThreat): before, every bot took the nearest enemy within 5,000.
            int radius = phases ? AutonomousBattlegroundActivity.EngageRadius(effective) : 5_000;
            Agent[] scan = Enemies(run, a).Where(o => Vector2.Distance(o.Pos, a.Pos) <= 5_000).ToArray();
            bool FiringOnRealm(Agent o) => o.Target != null && o.Target.Realm == a.Realm && Vector2.Distance(o.Target.Pos, a.Pos) <= 1_000;
            IEnumerable<Agent> candidates = scan.Where(o => FiringOnRealm(o) || Vector2.Distance(o.Pos, a.Pos) <= radius);
            Agent[] Around(Agent o) => scan.Where(x => x.Realm == o.Realm && Vector2.Distance(x.Pos, o.Pos) <= AutonomousBattlegroundActivity.GroupRadius).ToArray();
            Agent pick;
            if (phases && effective == Activity.OutskirtHunt)
            {
                int friends = run.Agents.Count(x => x.Alive && x.Realm == a.Realm &&
                    Vector2.Distance(x.Pos, a.Pos) <= AutonomousBattlegroundActivity.GroupRadius);
                Agent[] all = candidates.ToArray();
                bool Favorable(Agent o)
                {
                    if (!full) return false;
                    Agent[] campers = Around(o);
                    return AutonomousBattlegroundActivity.FavorableCamp(campers.Length, campers.Count(FightingMonster), friends,
                        a.Level - campers.Average(x => x.Level));
                }
                Agent[] kept = all.Where(o => FiringOnRealm(o) ||
                    !AutonomousBattlegroundActivity.HunterAvoids(Around(o).Length, friends, Favorable(o))).ToArray();
                if (kept.Length == 0 && all.Length > 0)
                {
                    a.Avoid = all[0].Pos;
                    a.Roam = null;
                }
                pick = kept.OrderBy(o => AutonomousBattlegroundActivity.HuntScore(Vector2.Distance(o.Pos, a.Pos), a.Level, o.Level,
                    FightingMonster(o), Around(o).Length)).FirstOrDefault();
                if (pick != null && Around(pick).Length == 3 && FightingMonster(pick)) a.CampOfThreeEngagements++;
            }
            else pick = candidates.OrderBy(o => Vector2.Distance(o.Pos, a.Pos)).FirstOrDefault();
            if (pick != null)
            {
                a.Target = pick;
                a.AtCamp = false;
                a.Engagements++;
                if (Around(pick).Length >= 3) a.GroupEngagements++;
                return;
            }

            // Defend the realm's keep (live siege response), or join a third realm's siege from its edges.
            if (a.Response == SiegeResponse.Defend)
            {
                a.Pos = Toward(a.Pos, Vector2.Zero, Speed);
                if (toKeep < 400) a.Pos = Vector2.Normalize(a.Pos + new Vector2(0.01f, 0)) * 400;
                return;
            }
            if (a.Response == SiegeResponse.ThirdParty && toKeep > 3_000)
            {
                a.Pos = Toward(a.Pos, Vector2.Zero, Speed);
                return;
            }

            if (effective == Activity.Grind)
            {
                if (a.Camp < 0)
                {
                    int[] counts = camps.Select((_, i) => run.Agents.Count(o => o != a && IsBot(o) && o.Alive && o.Realm == a.Realm &&
                        o.Activity == Activity.Grind && o.Camp == i)).ToArray();
                    a.Camp = AutonomousBattlegroundActivity.PickGrindCamp(counts, a.Rng.NextDouble());
                    if (a.Camp < 0) { a.Activity = Activity.KeepFights; return; }
                }
                Vector2 camp = camps[a.Camp];
                if (Vector2.Distance(a.Pos, camp) > 300) { a.AtCamp = false; a.Pos = Toward(a.Pos, camp, Speed); }
                else a.AtCamp = true;
                return;
            }

            // Keep fighters not of the holder assault with a wave of eight (AutonomousBattlegroundWave.MinimumWave).
            bool assault = !own && effective == Activity.KeepFights &&
                run.Agents.Count(o => o.Alive && IsBot(o) && o.Realm == a.Realm && o.Activity == Activity.KeepFights) >= 8;
            if (assault)
            {
                if (toKeep > 500) a.Pos = Toward(a.Pos, Vector2.Zero, Speed);
                return;
            }
            // A third of the holder's keep fighters hold its walls (key % 3 == 0, as DatabaseID % 3).
            if (own && effective == Activity.KeepFights && a.Key % 3 == 0)
            {
                if (toKeep > 400) a.Pos = Toward(a.Pos, Vector2.Zero, Speed);
                return;
            }
            // Party members follow their leader (live).
            if (full && a.Leader != null && a.Leader != a && a.Leader.Alive)
            {
                if (Vector2.Distance(a.Pos, a.Leader.Pos) > 300) a.Pos = Toward(a.Pos, a.Leader.Pos, Speed);
                return;
            }

            if (!a.Roam.HasValue || t >= a.RoamUntil || Vector2.Distance(a.Pos, a.Roam.Value) < 200)
            {
                Vector2[] enemyPortals = Enumerable.Range(0, 3).Where(r => r != a.RealmIndex).Select(Portal).ToArray();
                if (!phases)
                {
                    // RoamBattleground before: the keep, or 40% of the way from the keep to an enemy portal.
                    Vector2[] anchors = enemyPortals.Select(p => Vector2.Lerp(p, Vector2.Zero, 0.6f)).Append(Vector2.Zero).ToArray();
                    a.Roam = anchors[a.Rng.Next(anchors.Length)] + new Vector2((float)(a.Rng.NextDouble() - 0.5) * 1_800, (float)(a.Rng.NextDouble() - 0.5) * 1_800);
                    a.RoamUntil = t + 120 + (int)(a.Key % 30);
                }
                else if (effective == Activity.KeepFights || a.Response == SiegeResponse.ThirdParty)
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

        private static double DefendersPerSiegeSecond(IEnumerable<Run> runs)
        {
            double seconds = runs.Sum(r => r.SiegeSeconds);
            return seconds == 0 ? 0 : runs.Sum(r => r.DefenderSecondsInSiege) / seconds;
        }

        public static List<SimFinding> Evaluate(List<Run> runs)
        {
            var findings = new List<SimFinding>();
            double phasesDefenders = DefendersPerSiegeSecond(runs.Where(r => r.Rules == Rules.Phases));
            foreach (Rules rules in AllRules)
            {
                var set = runs.Where(r => r.Rules == rules).ToList();
                var bots = set.SelectMany(r => r.Agents.Where(IsBot)).ToList();
                string variant = Label(rules);
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
                double alive = Math.Max(1, bots.Sum(b => Seconds - b.DeadSeconds));
                double party = bots.Sum(b => b.PartySeconds) / alive;
                Add("Bots spend at least 30% of their time in a battleground party", party >= 0.30, P(party));
                double defenders = DefendersPerSiegeSecond(set);
                Add("More holder bots at the keep during sieges than with the #187 phases alone", defenders > phasesDefenders,
                    $"{F1(defenders)} per siege second (phases: {F1(phasesDefenders)})");
            }
            return findings;
        }

        public static string Markdown(List<Run> runs, List<SimFinding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Battleground goals: phases, parties and sieges");
            sb.AppendLine();
            sb.AppendLine("Owner, 2026-11-21: bots ran straight across the battleground at the player (#187: tour phases of grinding, keep fights or outskirt hunting by level tier), " +
                          "then: battleground groups, respond to keep sieges (own realm first), hunters may jump a camp of three when it is good action. " +
                          $"Three realms x {BotsPerRealm} bots (levels {string.Join(",", Levels)}, same in every realm), one scripted level {PlayerLevel} player grinding a camp 10,000 from the keep on its side, " +
                          $"the central keep held by one realm (rotating), and a scripted raid of {RaidSize} level {RaidLevel} attackers of the next realm at minutes {string.Join(" and ", RaidStarts.Select(s => s / 60))}; {Seconds / 60} simulated minutes. " +
                          "Flat field, line of sight everywhere, no guards or doors, a simple damage race. Simulation results are tests only, not in-game evidence.");
            sb.AppendLine();
            int perVariant = runs.Count / AllRules.Length;
            sb.AppendLine($"Runs: {perVariant} per variant (3 player realms x 3 keep holders / key-set rotations x {runs.Select(r => r.Seed).Distinct().Count()} seeds).");
            sb.AppendLine();
            sb.AppendLine("Variants:");
            sb.AppendLine("- **before**: every bot takes the nearest enemy within 5,000 and roams between the keep and points 40% of the way to the enemy portals; a third of the holder's bots hold the keep.");
            sb.AppendLine("- **phases (#187)**: grind 1,800 engage, shared camps; keep fights 3,500 engage, lurk 2,000-4,000 round the keep, waves of 8 keep fighters; outskirt hunt 5,000 engage, avoids groups of 3+ that outnumber it, campers and lower levels first.");
            sb.AppendLine("- **live (parties, sieges)**: #187 plus battleground parties (keep fighters 3-8, half the hunters in 2-3, grinders on one camp 2-4; members follow the leader and come to each other's aid), " +
                          "siege responses (own keep: keep fighters, hunters and grinders within 6,000 defend; half the hunters join a third realm's siege), and hunters jumping a busy camp of three with a partner or a 3-level edge.");
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

            sb.AppendLine("## The central keep");
            sb.AppendLine();
            sb.AppendLine("Siege seconds: seconds with an enemy of the holder within 900 of the keep. Defenders: holder bots within 1,200 then.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Siege minutes / run | Holder defenders per siege second | Captures / run | Raid captures / run | Start holder still holds at the end | Raiders killed / run |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var g in runs.GroupBy(r => r.Rules))
                sb.AppendLine($"| {Label(g.Key)} | {F1(g.Average(r => r.SiegeSeconds) / 60)} | {F1(DefendersPerSiegeSecond(g))} | {F1(g.Average(r => r.Captures))} | {F1(g.Average(r => r.RaidCaptures))} | " +
                              $"{P(g.Count(r => r.Holder == r.StartHolder) / (double)g.Count())} | {F1(g.Average(r => r.Agents.Where(IsBot).Sum(b => b.RaiderKills)))} |");
            sb.AppendLine();

            sb.AppendLine("## Bot time by level tier");
            sb.AppendLine();
            sb.AppendLine("Share of each bot's time alive. Fighting is time with an enemy target; in a party counts any time with a party.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Tier | Grinding | Keep fights | Outskirt hunt | Fighting | In a party | Mob kills / bot / hour | Kills / bot | Camper kills / bot | Camp-of-3 jumps / bot | Deaths / bot |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in runs.GroupBy(r => r.Rules))
                foreach (Tier tier in new[] { Tier.Low, Tier.Mid, Tier.High })
                {
                    var bots = g.SelectMany(r => r.Agents.Where(a => IsBot(a) && TierOf(a) == tier)).ToList();
                    double alive = Math.Max(1, bots.Sum(b => Seconds - b.DeadSeconds));
                    sb.AppendLine($"| {Label(g.Key)} | {tier} | {P(bots.Sum(b => b.GrindSeconds) / alive)} | {P(bots.Sum(b => b.KeepSeconds) / alive)} | {P(bots.Sum(b => b.HuntSeconds) / alive)} | {P(bots.Sum(b => b.FightSeconds) / alive)} | {P(bots.Sum(b => b.PartySeconds) / alive)} | " +
                                  $"{F1(bots.Average(b => b.MobKills))} | {F1(bots.Average(b => b.Kills))} | {F1(bots.Average(b => b.CamperKills))} | {F1(bots.Average(b => b.CampOfThreeEngagements))} | {F1(bots.Average(b => b.Deaths))} |");
                }
            sb.AppendLine();

            sb.AppendLine("## Bots by realm");
            sb.AppendLine();
            sb.AppendLine("Every realm has the same levels, and the key sets and the keep rotate between the realms, so the rows should be close. They are not exact: fights depend on who meets whom first.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Realm | Grinding | Keep fights | Outskirt hunt | In a party | Mob kills / bot | Kills / bot | Deaths / bot | Fights vs groups of 3+ |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in runs.GroupBy(r => r.Rules))
                foreach (eRealm realm in Realms)
                {
                    var bots = g.SelectMany(r => r.Agents.Where(a => IsBot(a) && a.Realm == realm)).ToList();
                    double alive = Math.Max(1, bots.Sum(b => Seconds - b.DeadSeconds));
                    int eng = bots.Sum(b => b.Engagements);
                    sb.AppendLine($"| {Label(g.Key)} | {SimRoster.Short(realm)} | {P(bots.Sum(b => b.GrindSeconds) / alive)} | {P(bots.Sum(b => b.KeepSeconds) / alive)} | {P(bots.Sum(b => b.HuntSeconds) / alive)} | {P(bots.Sum(b => b.PartySeconds) / alive)} | " +
                                  $"{F1(bots.Average(b => b.MobKills))} | {F1(bots.Average(b => b.Kills))} | {F1(bots.Average(b => b.Deaths))} | {(eng == 0 ? "-" : P((double)bots.Sum(b => b.GroupEngagements) / eng))} |");
                }
            sb.AppendLine();
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

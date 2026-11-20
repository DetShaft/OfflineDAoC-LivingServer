using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Keeps;

namespace DOL.UnitTests.BotSim
{
    public sealed class SimSetup
    {
        public eRealm Side0, Side1;
        public int Seed;
        public SimVariant Variant;
    }

    /// <summary>
    /// A way of running side 0's bots. "current" is the live rules. Add a variant to compare a new rule set (for example
    /// active fight mode) against them; every scenario runs every variant it lists.
    /// </summary>
    public sealed class SimVariant
    {
        public string Name;
        public string Description;
        public Func<ISimTactics> Side0Bots;
        /// <summary>The enemy side's bots; null: <see cref="AutonomousEnemy"/>.</summary>
        public Func<ISimTactics> Side1Bots;
        public ISimTactics Enemy() => Side1Bots?.Invoke() ?? AutonomousEnemy();
        public HashSet<string> Tags = new();
        public bool Has(string tag) => Tags.Contains(tag);

        /// <summary>The variant whose findings CI warns about and BOT_SIM_STRICT fails on: what ships today.</summary>
        public bool Live;

        public static SimVariant Current() => new()
        {
            Name = "fight mode off",
            Description = "The rules before #165 (server setting bots / group_fight_mode = False): bots assist the player's attack, share the companion group focus (the player's target, else the enemy that last hit a groupmate) and defend themselves.",
            Side0Bots = () => new CurrentBotTactics(),
        };

        public static SimVariant FightMode() => new()
        {
            Name = "fight mode (#175-#177)", Live = true,
            Description = "The live rules: fight mode (#170) plus the #175-#177 fixes. #175: in fight mode an enemy pet that hits a bot does not take it off its pick. #176: an enemy behind a ridge stays a fight-mode candidate for 6 s after the player last saw it, and a cast that failed its line of sight check keeps the bot walking toward the target until it is in sight (every bot, enemies too). #177: front-line bots also drop a target past the chase distance from the group's nearest caster or healer (the support leash), and ranged casters not under attack close up to 900 units of the nearest groupmate fighting in melee once they are past 1,200.",
            Side0Bots = () => new FightModeTactics(),
        };

        /// <summary>Fight mode as shipped in v1.1.24 (#170 and its follow-ups), both sides without the #175-#177 fixes.</summary>
        public static SimVariant FightMode170() => new()
        {
            Name = "fight mode (#170)",
            Description = "Comparison: fight mode as shipped in v1.1.24, before #175-#177, on both sides (the line of sight walk, #176, changes every bot). Fight mode with the #170 fixes: it keeps running when the player dies (group anchor), a better-role enemy replaces the focus, busy bots switch to it, no cast-time CC while a hit could land before the cast ends, a mezzed attacker is left alone, a leash that holds after fight mode ends and on its own picks, and no companion focus while fight mode is on. Picks the player's target while they attack it, a tank peel, or the shared focus (healers, then casters, then lowest health), keeps a 1,500-unit chase leash, and keeps CC off the focus.",
            Side0Bots = () => new FightModeTactics { EdgeCaseFixes = false, Label = "fight mode (#170)" },
            Side1Bots = () => new CurrentBotTactics { SharedFocus = false, CompanionRecall = false, ProximityAggroRadius = 1_500, DirectAttackerFirst = false, EdgeCaseFixes = false, Label = "autonomous enemy (before #176)" },
        };

        /// <summary>The rules shipped in PR #168, on both sides (the CC fixes in #170 apply to every bot, enemies too).</summary>
        public static SimVariant FightModeBefore170() => new()
        {
            Name = "fight mode (#165), before #170",
            Description = "Comparison: fight mode as merged in PR #168, with both sides on the rules before #170 (the CC and mez-protection fixes change every bot, enemy gamebots included).",
            Side0Bots = () => new FightModeTactics { Fixes170 = false, EdgeCaseFixes = false, SelectedTargetFirst = true, Label = "fight mode (#165), before #170" },
            Side1Bots = () => new CurrentBotTactics { SharedFocus = false, CompanionRecall = false, ProximityAggroRadius = 1_500, DirectAttackerFirst = false, Fixes170 = false, EdgeCaseFixes = false, Label = "autonomous enemy (before #170)" },
        };

        /// <summary>The enemy side: an autonomous gamebot group (no companion focus or recall; attacks enemies within 1,500 on sight).</summary>
        public static ISimTactics AutonomousEnemy() =>
            new CurrentBotTactics { SharedFocus = false, CompanionRecall = false, ProximityAggroRadius = 1_500, DirectAttackerFirst = false, Label = "autonomous enemy" };

        public static SimVariant NoSharedFocus() => new()
        {
            Name = "no shared focus",
            Description = "Live rules without CompanionPvpEngagement's group focus: bots fight only the leader's target and enemies that hit them. Roughly how a group without the companion focus behaves (autonomous gamebot groups are not modelled exactly).",
            Side0Bots = () => new CurrentBotTactics { SharedFocus = false, Label = "no shared focus" },
        };
    }

    public sealed class SimExpectation
    {
        public string Text;
        public Func<IReadOnlyList<SimRunResult>, (bool Ok, string Detail)> Check;
    }

    public sealed class SimScenario
    {
        public string Name;
        public string Title;
        public string Why;
        public string Issues = "";
        public int Seconds = 120;
        public int[] Seeds = { 1, 2, 3 };
        public Func<SimSetup, SimWorld> Build;
        public List<SimVariant> Variants = new() { SimVariant.Current(), SimVariant.FightModeBefore170(), SimVariant.FightMode170(), SimVariant.FightMode() };
        public List<SimExpectation> Expectations = new();
    }

    public static class SimRunner
    {
        public static IEnumerable<(eRealm, eRealm)> RealmPairs() =>
            from a in SimRoster.Realms from b in SimRoster.Realms where a != b select (a, b);

        public static List<SimRunResult> Run(SimScenario scenario, IEnumerable<SimVariant> variants = null)
        {
            var results = new List<SimRunResult>();
            foreach (SimVariant variant in variants ?? scenario.Variants)
                foreach ((eRealm side0, eRealm side1) in RealmPairs())
                    foreach (int seed in scenario.Seeds)
                        results.Add(RunOne(scenario, new SimSetup { Side0 = side0, Side1 = side1, Seed = seed, Variant = variant }));
            return results;
        }

        public static SimRunResult RunOne(SimScenario scenario, SimSetup setup)
        {
            SimWorld world = scenario.Build(setup);
            world.Run(scenario.Seconds);
            var result = new SimRunResult
            {
                Scenario = scenario.Name, Variant = setup.Variant.Name, Seed = setup.Seed,
                Realms = new SimRealmPair { Side0 = setup.Side0, Side1 = setup.Side1 },
                DurationMs = world.Now, Agents = world.Agents.ToList(), Timeline = world.Timeline.ToList(),
            };
            var sides = world.Agents.Select(a => a.Side).Distinct().ToList();
            var standing = sides.Where(s => world.Agents.Any(a => a.Side == s && a.Alive && (!a.Kit.IsNpc || a.Role == SimRole.Lord))).ToList();
            if (standing.Count == 1)
                result.WinnerSide = standing[0];
            return result;
        }
    }

    /// <summary>The scenario catalog. To add one, add a method here and list it in <see cref="All"/>.</summary>
    public static class SimScenarios
    {
        public const int Level = 50;

        public static readonly SimRole[] StandardGroup =
            { SimRole.Tank, SimRole.Melee, SimRole.Melee, SimRole.Caster, SimRole.Caster, SimRole.Mezzer, SimRole.Healer, SimRole.Healer };

        public static readonly SimRole[] CrowdControlGroup =
            { SimRole.Tank, SimRole.Melee, SimRole.Mezzer, SimRole.Mezzer, SimRole.Mezzer, SimRole.Caster, SimRole.Healer, SimRole.Healer };

        public static IReadOnlyList<SimScenario> All() => new[]
        {
            OpenFieldEightVsEight(), KeepLordAfterDoors(), CrowdControlHeavy(), LeaderMezzed(), ChaseLeash(),
            PetsEightVsEight(), LineOfSightRidge(), SplitKite(),
        };

        /// <summary>
        /// Adds a group facing +Y (side 0) or -Y (side 1). roles[0] is the leader: a scripted player unless
        /// <paramref name="leaderTactics"/> says otherwise. Melee stand in front, casters and healers 200 behind.
        /// </summary>
        public static List<SimAgent> AddGroup(SimWorld w, int side, eRealm realm, SimRole[] roles, Vec2 center,
            ISimTactics botTactics, ISimTactics leaderTactics, bool extraSlowMez = false)
        {
            double facing = side == 0 ? 1 : -1;
            var group = new List<SimAgent>();
            int front = 0, back = 0;
            for (int i = 0; i < roles.Length; i++)
            {
                SimRole role = roles[i];
                bool frontLine = role is SimRole.Tank or SimRole.Melee;
                int slot = frontLine ? front++ : back++;
                double x = center.X + (slot - 1.5) * 140;
                double y = center.Y - (frontLine ? 0 : 200) * facing;
                bool isLeader = i == 0;
                string name = $"{SimRoster.Short(realm)}{i + 1}-{SimRoster.ClassName(realm, role)}";
                SimAgent agent = w.Add(isLeader ? name + "(player)" : name, realm, side, role, Level, new Vec2(x, y),
                    isLeader ? leaderTactics : botTactics);
                agent.IsLeader = isLeader;
                agent.IsBot = !isLeader;
                if (role == SimRole.Mezzer)
                {
                    agent.CcSpells.Add(SimSpells.AreaMez());
                    agent.CcSpells.Add(SimSpells.SingleMez());
                    if (extraSlowMez && group.All(g => g.Role != SimRole.Mezzer))
                        agent.CcSpells.Add(SimSpells.SlowAreaMez());
                }
                group.Add(agent);
            }
            foreach (SimAgent agent in group)
            {
                agent.Leader = group[0];
                agent.Group = group;
            }
            return group;
        }

        private static SimWorld TwoGroups(SimSetup s, SimRole[] roles, double gap, bool extraSlowMez = false)
        {
            var w = new SimWorld(s.Seed);
            AddGroup(w, 0, s.Side0, roles, new Vec2(0, -gap / 2), s.Variant.Side0Bots(), new ScriptedLeader(), extraSlowMez);
            // The enemy leader is scripted to charge (an autonomous group heading for the fight); its bots are autonomous.
            AddGroup(w, 1, s.Side1, roles, new Vec2(0, gap / 2), s.Variant.Enemy(), new ScriptedLeader(), extraSlowMez);
            return w;
        }

        // ---- shared checks

        public static IEnumerable<SimAgent> AllBots(IReadOnlyList<SimRunResult> runs) => runs.SelectMany(r => r.Bots());

        public static double MeanIdle(IReadOnlyList<SimRunResult> runs, Func<SimAgent, bool> filter = null)
        {
            var bots = AllBots(runs).Where(b => filter == null || filter(b)).ToList();
            long combat = bots.Sum(b => b.Metrics.InCombatMs);
            return combat == 0 ? 0 : 100.0 * bots.Sum(b => b.Metrics.IdleInCombatMs) / combat;
        }

        public static SimExpectation IdleBelow(double percent, string who = "side 0 bots (healers excluded)") => new()
        {
            Text = $"Combat idle time of {who} stays under {percent:0}%",
            Check = runs =>
            {
                double idle = MeanIdle(runs, b => b.Role != SimRole.Healer);
                return (idle < percent, $"{idle:0.0}% idle while their side was in combat");
            },
        };

        // ---- 1. open field

        public static SimScenario OpenFieldEightVsEight() => new()
        {
            Name = "open-field-8v8",
            Title = "8v8 open-field group fight",
            Why = "Baseline: the player's group (a scripted player plus 7 bots) meets a matched autonomous gamebot group (tank, 2 melee, 2 casters, mezzer, 2 healers each) on open ground. The player attacks the nearest enemy.",
            Build = s => TwoGroups(s, StandardGroup, 2_400),
            Variants =
            {
                new SimVariant
                {
                    Name = "mirror control",
                    Description = "Harness check, not a rule set: side 0's bots use the enemy's autonomous rules, so both sides are identical. It should win about half; if it does, the other variants' results come from their rules, not from the setup.",
                    Side0Bots = SimVariant.AutonomousEnemy,
                },
            },
            Expectations =
            {
                IdleBelow(20),
                new SimExpectation
                {
                    Text = "The player's group wins at least half of its fights against a matched autonomous group",
                    Check = runs =>
                    {
                        int side0 = runs.Count(r => r.WinnerSide == 0), side1 = runs.Count(r => r.WinnerSide == 1);
                        return (side0 * 2 >= runs.Count, $"side 0 won {side0}, side 1 won {side1}, {runs.Count - side0 - side1} timed out, of {runs.Count}");
                    },
                },
            },
        };

        // ---- 2. keep lord after the doors fall

        /// <summary>Dun Killaloe, 2026-11-19: the gate (Z 8768) open, the keep door (8770) broken, two gatehouse doors (9200) standing.</summary>
        public static readonly (int Z, bool Standing)[] KillaloeDoors = { (8768, false), (8770, false), (9200, true), (9200, true) };

        /// <summary>The live rule (#160): GuardLord.BlocksTheWayIn over the doors still standing.</summary>
        public static bool ShieldedNow((int Z, bool Standing)[] doors)
        {
            int groundZ = doors.Min(d => d.Z);
            return doors.Any(d => d.Standing && GuardLord.BlocksTheWayIn(d.Z, groundZ));
        }

        /// <summary>The rule before #160: any standing door shielded the lord.</summary>
        public static bool ShieldedBefore160((int Z, bool Standing)[] doors) => doors.Any(d => d.Standing);

        public static SimScenario KeepLordAfterDoors() => new()
        {
            Name = "keep-lord-after-doors",
            Title = "Group vs a keep lord after the doors fall (#157 / #160)",
            Issues = "#157 #160",
            Why = "A player-led group stands in the courtyard after the gate and keep door fell; the upper gatehouse doors still stand (Dun Killaloe, 2026-11-19). The player attacks the lord; two guards defend. Side 1 is the keep (NPCs).",
            Seconds = 150,
            Build = s =>
            {
                var w = new SimWorld(s.Seed);
                var doors = KillaloeDoors;
                bool shielded = s.Variant.Has("before-160") ? ShieldedBefore160(doors) : ShieldedNow(doors);
                SimAgent lord = w.Add($"Lord ({SimRoster.Short(s.Side1)})", s.Side1, 1, SimRole.Lord, 55, new Vec2(0, 0), new NpcGuardTactics { AggroRange = 500 });
                lord.ShieldedFromBots = _ => shielded;
                w.Add($"Guard A ({SimRoster.Short(s.Side1)})", s.Side1, 1, SimRole.Guard, 50, new Vec2(-200, -150), new NpcGuardTactics());
                w.Add($"Guard B ({SimRoster.Short(s.Side1)})", s.Side1, 1, SimRole.Guard, 50, new Vec2(200, -150), new NpcGuardTactics());
                w.Log(shielded ? "The lord is shielded from bots by a standing door." : "No standing door shields the lord.");
                AddGroup(w, 0, s.Side0, StandardGroup, new Vec2(0, -1_000), s.Variant.Side0Bots(),
                    new ScriptedLeader { PickTarget = (world, me) => world.Agents.FirstOrDefault(a => a.Role == SimRole.Lord && a.Alive) });
                return w;
            },
            Variants =
            {
                new SimVariant
                {
                    Name = "rule before #160", Description = "The old door rule (any standing door shields the lord), fight mode off.",
                    Side0Bots = () => new CurrentBotTactics(), Tags = { "before-160" },
                },
            },
            Expectations =
            {
                new SimExpectation
                {
                    Text = "Killaloe's door state leaves the lord unshielded under the live rule",
                    Check = _ => (!ShieldedNow(KillaloeDoors), ShieldedNow(KillaloeDoors) ? "shielded" : "not shielded"),
                },
                new SimExpectation
                {
                    Text = "Group bots do at least half of the damage on the lord",
                    Check = runs =>
                    {
                        var current = runs.ToList();
                        double bots = current.Sum(r => r.Bots().Sum(b => b.Metrics.DamageByTarget.Where(p => p.Key.StartsWith("Lord")).Sum(p => p.Value)));
                        double all = current.Sum(r => r.Side(0).Sum(b => b.Metrics.DamageByTarget.Where(p => p.Key.StartsWith("Lord")).Sum(p => p.Value)));
                        double share = all == 0 ? 0 : 100 * bots / all;
                        return (share >= 50, $"bots did {share:0}% of the damage on the lord");
                    },
                },
                new SimExpectation
                {
                    Text = "The lord dies in most runs",
                    Check = runs =>
                    {
                        var current = runs.ToList();
                        int dead = current.Count(r => r.Agents.Any(a => a.Role == SimRole.Lord && !a.Alive));
                        return (dead * 2 > current.Count, $"lord died in {dead} of {current.Count} runs");
                    },
                },
            },
        };

        // ---- 3. CC heavy

        public static SimScenario CrowdControlHeavy() => new()
        {
            Name = "cc-heavy",
            Title = "CC-heavy fight (#151 / #152)",
            Issues = "#151 #152",
            Why = "Both groups bring three mezzers (one also carries the slow backup area mez). Checks the PvP CC rules: area mez needs 2+ free enemies, a 60 s area mez hold, no mez on enemies groupmates hit, and claims that end when the cast stops.",
            Build = s => TwoGroups(s, CrowdControlGroup, 2_400, extraSlowMez: true),
            Expectations =
            {
                new SimExpectation
                {
                    Text = "Under 35% of side 0's CC casts are wasted",
                    Check = runs =>
                    {
                        var bots = AllBots(runs.ToList()).ToList();
                        int casts = bots.Sum(b => b.Metrics.CcCasts), wasted = bots.Sum(b => b.Metrics.WastedCc);
                        double pct = casts == 0 ? 0 : 100.0 * wasted / casts;
                        return (pct < 35, $"{wasted} of {casts} CC casts wasted ({pct:0}%)");
                    },
                },
                new SimExpectation
                {
                    Text = "Side 0 breaks its own mezzes fewer than once per run on average",
                    Check = runs =>
                    {
                        var current = runs.ToList();
                        double perRun = current.Count == 0 ? 0 : current.Sum(r => r.Bots().Sum(b => b.Metrics.BrokeOwnSideMez) + r.Side(0).Where(a => a.IsLeader).Sum(b => b.Metrics.BrokeOwnSideMez)) / (double)current.Count;
                        return (perRun < 1, $"{perRun:0.0} own-side mez breaks per run");
                    },
                },
                IdleBelow(25),
            },
        };

        // ---- 4. leader mezzed

        public static SimScenario LeaderMezzed() => new()
        {
            Name = "leader-mezzed",
            Title = "Leader mezzed while enemies attack",
            Why = "The player leading side 0 is mezzed 2 s in (before contact) for 25 s, while side 1 charges. With the leader not attacking, live bots wait until an enemy hits a groupmate (the shared group focus) or themselves. This is the case that motivates active fight mode.",
            Build = s =>
            {
                SimWorld w = TwoGroups(s, StandardGroup, 2_000);
                SimAgent leader = w.Agents.First(a => a.Side == 0 && a.IsLeader);
                w.Agents.First(a => a.Side == 0 && a.IsLeader).Tactics = new MezzedLeader(leader.Tactics, 2_000, 25_000);
                return w;
            },
            Variants =
            {
                new SimVariant
                {
                    Name = "fight mode, selected target first (old rule)",
                    Description = "Comparison, no longer shipped: as the live fight mode, but the player's selected target comes first even while the player is mezzed (the rule before the owner's decision of 2026-11-20).",
                    Side0Bots = () => new FightModeTactics { SelectedTargetFirst = true, Label = "fight mode, selected target first (old rule)" },
                },
            },
            Expectations =
            {
                IdleBelow(25),
                new SimExpectation
                {
                    Text = "Every side 0 damage dealer hits someone within 15 s of contact",
                    Check = runs =>
                    {
                        var current = runs.ToList();
                        var dealers = current.SelectMany(r => r.Bots().Where(b => b.Role is not SimRole.Healer).Select(b => (r, b))).ToList();
                        int late = dealers.Count(p => p.b.Metrics.TargetsChosen.Count == 0 || p.b.Metrics.TargetsChosen[0].Time > 20_000);
                        return (late == 0, $"{late} of {dealers.Count} bot-runs had no target by 20 s");
                    },
                },
            },
        };

        /// <summary>Wraps the scripted player: mezzed (unbreakable) from <c>at</c> for <c>duration</c> milliseconds.</summary>
        public sealed class MezzedLeader : ISimTactics
        {
            private readonly ISimTactics _inner;
            private readonly long _at, _duration;
            private bool _applied;
            public MezzedLeader(ISimTactics inner, long at, long duration) { _inner = inner; _at = at; _duration = duration; }
            public string Name => _inner.Name + " (mezzed)";
            public void Think(SimWorld w, SimAgent a)
            {
                if (!_applied && w.Now >= _at)
                {
                    _applied = true;
                    a.MezzedUntil = w.Now + _duration;
                    a.Unbreakable = true;
                    a.AttackingNow = false;
                    w.Log($"{a.Name} is mezzed for {_duration / 1000} s (scripted)");
                    return;
                }
                if (w.Now >= a.MezzedUntil)
                    a.Unbreakable = false;
                _inner.Think(w, a);
            }
        }

        // ---- 5. chase / leash

        public const int LeashLimit = 2_500;

        public static SimScenario ChaseLeash() => new()
        {
            Name = "chase-leash",
            Title = "Chase / leash distance",
            Why = "Two enemy melee harass side 0 for 8 s, then run straight away at the same speed. The player never moves or attacks. Measures how far bots chase away from their leader.",
            Seconds = 60,
            Build = s =>
            {
                var w = new SimWorld(s.Seed);
                List<SimAgent> group = AddGroup(w, 0, s.Side0, StandardGroup, new Vec2(0, 0), s.Variant.Side0Bots(), new ScriptedLeader { Passive = true });
                Vec2 from = group[0].Position;
                for (int i = 0; i < 2; i++)
                {
                    SimAgent runner = w.Add($"{SimRoster.Short(s.Side1)}{i + 1}-{SimRoster.ClassName(s.Side1, SimRole.Melee)}", s.Side1, 1, SimRole.Melee, Level,
                        new Vec2(-150 + 300 * i, 1_200), new RunnerTactics { RunFrom = from });
                    runner.MaxHealth = runner.Health = runner.MaxHealth * 4; // they must survive the harassment to draw the chase
                }
                return w;
            },
            Expectations =
            {
                new SimExpectation
                {
                    Text = $"No side 0 bot ends up more than {LeashLimit} units from its leader",
                    Check = runs =>
                    {
                        var bots = AllBots(runs.ToList()).ToList();
                        double max = bots.Count == 0 ? 0 : bots.Max(b => b.Metrics.MaxDistanceFromLeader);
                        int over = bots.Count(b => b.Metrics.MaxDistanceFromLeader > LeashLimit);
                        return (over == 0, $"farthest {max:0} units; {over} of {bots.Count} bot-runs went past {LeashLimit}");
                    },
                },
            },
        };

        // ---- 6. pets (#175)

        public static SimExpectation WinsHalf() => new()
        {
            Text = "The player's group wins at least half of its fights",
            Check = runs =>
            {
                int side0 = runs.Count(r => r.WinnerSide == 0), side1 = runs.Count(r => r.WinnerSide == 1);
                return (side0 * 2 >= runs.Count, $"side 0 won {side0}, side 1 won {side1}, {runs.Count - side0 - side1} timed out, of {runs.Count}");
            },
        };

        /// <summary>Gives each caster of the group a pet, placed 300 units in front of it.</summary>
        public static void AddPets(SimWorld w, IEnumerable<SimAgent> group, int side)
        {
            double facing = side == 0 ? 1 : -1;
            foreach (SimAgent caster in group.Where(g => g.Role == SimRole.Caster).ToList())
                w.AddPet(caster, new Vec2(caster.Position.X, caster.Position.Y + 300 * facing));
        }

        public static SimScenario PetsEightVsEight() => new()
        {
            Name = "pets-8v8",
            Title = "8v8 with pets on the front line (#175)",
            Issues = "#175",
            Why = "The open-field 8v8, with a pet for each of the two casters on both sides (Bonedancer, Theurgist, Cabalist or Animist style pets), standing in front of the group. Pets go for the nearest enemy, so they meet the enemy melee first. Measures how many of side 0's melee hits land on pets while an enemy player stands within 1,000 units.",
            Build = s =>
            {
                SimWorld w = TwoGroups(s, StandardGroup, 2_400);
                AddPets(w, w.Agents.Where(a => a.Side == 0).ToList(), 0);
                AddPets(w, w.Agents.Where(a => a.Side == 1 && !a.Kit.IsPet).ToList(), 1);
                return w;
            },
            Expectations =
            {
                new SimExpectation
                {
                    Text = "Side 0 melee bots put under 15% of their melee hits on pets while an enemy player is within 1,000 units",
                    Check = runs =>
                    {
                        var melee = AllBots(runs).Where(b => b.Role == SimRole.Melee).ToList();
                        int hits = melee.Sum(b => b.Metrics.MeleeHits), onPets = melee.Sum(b => b.Metrics.MeleeHitsOnPetsPlayerNear);
                        double pct = hits == 0 ? 0 : 100.0 * onPets / hits;
                        return (pct < 15, $"{onPets} of {hits} melee hits on pets with a player near ({pct:0.0}%), {melee.Sum(b => b.Metrics.MeleeHitsOnPets)} on pets in all");
                    },
                },
                WinsHalf(),
                IdleBelow(20),
            },
        };

        // ---- 7. line of sight (#176)

        public static SimScenario LineOfSightRidge() => new()
        {
            Name = "los-ridge",
            Title = "A ridge between the groups (#176)",
            Issues = "#176",
            Why = "The open-field 8v8 with a 1,400-unit ridge across the middle that blocks sight (casts, heals, the fight-mode scan) but not movement. Melee walk over it; casters, mezzers and healers must move to see. Measures the longest idle stretch and the idle time of side 0's bots while their side is in combat.",
            Build = s =>
            {
                SimWorld w = TwoGroups(s, StandardGroup, 2_400);
                w.Walls.Add((new Vec2(-700, 0), new Vec2(700, 0)));
                w.Log("A ridge from (-700,0) to (700,0) blocks sight between the groups.");
                return w;
            },
            Expectations =
            {
                IdleBelow(20),
                new SimExpectation
                {
                    Text = "No side 0 caster or mezzer stands idle in combat for more than 3 s at a stretch",
                    Check = runs =>
                    {
                        var casters = AllBots(runs).Where(b => b.Role is SimRole.Caster or SimRole.Mezzer).ToList();
                        long longest = casters.Count == 0 ? 0 : casters.Max(b => b.Metrics.LongestIdleMs);
                        int over = casters.Count(b => b.Metrics.LongestIdleMs > 3_000);
                        double meanLongest = casters.Count == 0 ? 0 : casters.Average(b => b.Metrics.LongestIdleMs) / 1000.0;
                        return (over == 0, $"longest {longest / 1000.0:0.0} s, mean longest {meanLongest:0.0} s; {over} of {casters.Count} bot-runs over 3 s");
                    },
                },
                WinsHalf(),
            },
        };

        // ---- 8. split by kiting enemies (#177)

        public const int BackLineLimit = 2_000;

        public static SimScenario SplitKite() => new()
        {
            Name = "split-kite",
            Title = "Enemy melee bait the group's melee away from its casters (#177)",
            Issues = "#177",
            Why = "Side 0 holds the middle: the player (a melee character) stands still. The enemy's three melee come in from the east, fight for 7 s, break off toward a bait point 1,400 units east of the player for 7 s, and come back; the enemy's casters, mezzer and healers hold the west flank (the enemy leader is a caster that holds its spot). Measures how far side 0's melee bots get from the nearest friendly caster or healer.",
            Build = s =>
            {
                var w = new SimWorld(s.Seed);
                AddGroup(w, 0, s.Side0, StandardGroup, new Vec2(0, 0), s.Variant.Side0Bots(), new ScriptedLeader { Passive = true });
                SimRole[] enemy = { SimRole.Caster, SimRole.Tank, SimRole.Melee, SimRole.Melee, SimRole.Caster, SimRole.Mezzer, SimRole.Healer, SimRole.Healer };
                List<SimAgent> side1 = AddGroup(w, 1, s.Side1, enemy, new Vec2(-1_000, 1_000), s.Variant.Enemy(), new HoldAndNukeTactics());
                var bait = new Vec2(1_400, 300);
                int i = 0;
                foreach (SimAgent m in side1.Where(a => a.Role is SimRole.Tank or SimRole.Melee))
                {
                    m.Tactics = new BaiterTactics { BaitPoint = new Vec2(bait.X, bait.Y + 120 * i) };
                    m.Position = m.Home = new Vec2(1_000 + 120 * i, 900);
                    i++;
                }
                return w;
            },
            Expectations =
            {
                new SimExpectation
                {
                    Text = $"No side 0 melee bot gets more than {BackLineLimit} units from the nearest friendly caster or healer",
                    Check = runs =>
                    {
                        var front = AllBots(runs).Where(b => b.Role is SimRole.Melee or SimRole.Tank && b.Metrics.BackLineSamples > 0).ToList();
                        double max = front.Count == 0 ? 0 : front.Max(b => b.Metrics.MaxDistanceToBackLine);
                        double mean = front.Count == 0 ? 0 : front.Average(b => b.Metrics.MeanDistanceToBackLine);
                        int over = front.Count(b => b.Metrics.MaxDistanceToBackLine > BackLineLimit);
                        return (over == 0, $"farthest {max:0}, mean {mean:0}; {over} of {front.Count} bot-runs past {BackLineLimit}");
                    },
                },
                WinsHalf(),
                IdleBelow(25),
            },
        };
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.UnitTests.BotSim;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot AI simulation scenarios (#166). Each test runs one scenario in every realm pairing and writes
    /// bot-sim/&lt;scenario&gt;.md and .json (CI uploads them as the "bot-sim" artifact). Expectations that fail are
    /// reported as findings, not test failures, unless BOT_SIM_STRICT=1. Hard assertions cover the harness itself:
    /// determinism and realm evenness. See docs/BOT-AI-SIMULATION.md.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotSim
    {
        // Spell objects need a GameServer instance; same test double as UT_BotPvpCrowdControlPolicy.
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;

        [OneTimeSetUp]
        public void ClearFindings()
        {
            string file = Path.Combine(SimReport.OutputFolder(), "findings.tsv");
            if (File.Exists(file))
                File.Delete(file);
        }

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown] public void Cleanup() => GameServer.LoadTestDouble(_previous);

        private static bool Strict => Environment.GetEnvironmentVariable("BOT_SIM_STRICT") == "1";

        public static string[] ScenarioNames() => new[]
        {
            "open-field-8v8", "keep-lord-after-doors", "cc-heavy", "leader-mezzed", "chase-leash", "pets-8v8", "los-ridge", "split-kite",
        };

        [TestCaseSource(nameof(ScenarioNames))]
        public void Scenario(string name)
        {
            SimScenario scenario = SimScenarios.All().Single(s => s.Name == name);
            var results = SimRunner.Run(scenario);
            var findings = SimReport.Evaluate(scenario, results);
            SimReport.Write(scenario, results, findings);

            foreach (SimFinding f in findings)
                TestContext.Progress.WriteLine($"[bot-sim] {(f.Ok ? "pass   " : "FINDING")} {f.Scenario} / {f.Variant}: {f.Expectation} -> {f.Measured}");

            // The harness must produce a fight: someone on side 0 dealt or took damage.
            Assert.That(results.All(r => r.Side(0).Any(a => a.Metrics.DamageDealt + a.Metrics.DamageTaken > 0)), Is.True, "Every run has combat.");
            AssertRealmEven(results);

            if (Strict)
                Assert.That(findings.Where(f => f.Live && !f.Ok).Select(f => f.Expectation), Is.Empty,
                    "BOT_SIM_STRICT=1: the live rules miss these expectations.");
        }

        /// <summary>
        /// Realm evenness: role stats are identical in every realm, so a realm playing side 0 must get exactly the same
        /// numbers as any other realm with the same seeds. A difference means realm leaked into a rule.
        /// </summary>
        private static void AssertRealmEven(System.Collections.Generic.List<SimRunResult> results)
        {
            foreach (var variant in results.GroupBy(r => r.Variant))
            {
                var perRealm = SimRoster.Realms.Select(realm =>
                {
                    var bots = variant.Where(r => r.Realms.Side0 == realm).SelectMany(r => r.Bots()).ToList();
                    return (realm, dealt: Math.Round(bots.Sum(b => b.Metrics.DamageDealt)), taken: Math.Round(bots.Sum(b => b.Metrics.DamageTaken)),
                        idle: bots.Sum(b => b.Metrics.IdleInCombatMs), wins: variant.Count(r => r.Realms.Side0 == realm && r.WinnerSide == 0));
                }).ToList();
                foreach (var row in perRealm.Skip(1))
                    Assert.That((row.dealt, row.taken, row.idle, row.wins), Is.EqualTo((perRealm[0].dealt, perRealm[0].taken, perRealm[0].idle, perRealm[0].wins)),
                        $"{variant.Key}: {row.realm} as side 0 differs from {perRealm[0].realm}.");
            }
        }

        /// <summary>
        /// The battleground goal layer (owner, 2026-11-21; SimBattleground): tour phases by level tier against the old
        /// "everyone charges anything within 5,000". Findings are reported like the fight scenarios'.
        /// </summary>
        [Test]
        public void BattlegroundGoals()
        {
            var runs = SimBattleground.RunAll();
            var findings = SimBattleground.Evaluate(runs);
            SimBattleground.Write(runs, findings);
            foreach (SimFinding f in findings)
                TestContext.Progress.WriteLine($"[bot-sim] {(f.Ok ? "pass   " : "FINDING")} {f.Scenario} / {f.Variant}: {f.Expectation} -> {f.Measured}");
            Assert.That(runs.All(r => r.Agents.Any(a => a.Kills > 0)), Is.True, "Every run has fights.");
            var again = SimBattleground.RunOne(SimBattleground.Rules.Live, 2, 1, eRealm.Midgard);
            var first = runs.Single(r => r.Rules == SimBattleground.Rules.Live && r.Seed == 2 && r.Rotation == 1 && r.PlayerRealm == eRealm.Midgard);
            Assert.That(again.Agents.Select(a => (a.Kills, a.Deaths, a.MobKills, a.GrindSeconds)),
                Is.EqualTo(first.Agents.Select(a => (a.Kills, a.Deaths, a.MobKills, a.GrindSeconds))), "The same seed gives the same run.");
            if (Strict)
                Assert.That(findings.Where(f => f.Live && !f.Ok).Select(f => f.Expectation), Is.Empty,
                    "BOT_SIM_STRICT=1: the live rules miss these expectations.");
        }

        [Test]
        public void SameSeedGivesTheSameRun()
        {
            SimScenario scenario = SimScenarios.CrowdControlHeavy();
            var setup = new SimSetup { Side0 = eRealm.Albion, Side1 = eRealm.Hibernia, Seed = 7, Variant = SimVariant.FightMode() };
            string first = SimReport.Json(new[] { SimRunner.RunOne(scenario, setup) }, Array.Empty<SimFinding>());
            string second = SimReport.Json(new[] { SimRunner.RunOne(scenario, setup) }, Array.Empty<SimFinding>());
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void ScenarioCatalogMatchesTheTestCases()
        {
            Assert.That(SimScenarios.All().Select(s => s.Name), Is.EquivalentTo(ScenarioNames()));
            Assert.That(SimScenarios.All().Select(s => s.Name).Distinct().Count(), Is.EqualTo(SimScenarios.All().Count));
        }

        [Test]
        public void KillaloeDoorsNoLongerShieldTheLord()
        {
            // #160 regression through the real GuardLord.BlocksTheWayIn.
            Assert.That(SimScenarios.ShieldedNow(SimScenarios.KillaloeDoors), Is.False);
            Assert.That(SimScenarios.ShieldedBefore160(SimScenarios.KillaloeDoors), Is.True);
            Assert.That(SimScenarios.ShieldedNow(new[] { (8768, true), (9200, true) }), Is.True, "A standing gate still shields.");
        }

        [Test]
        public void MezzerSpellsGoThroughTheRealOrdering()
        {
            var spells = new[] { SimSpells.SlowAreaMez(), SimSpells.SingleMez(), SimSpells.AreaMez() };
            var ordered = BotPvpCrowdControlPolicy.Order(spells.Select(s => s.Spell)).ToList();
            Assert.That(ordered.First().Name, Is.EqualTo("area mez"));
            Assert.That(ordered.Last().Name, Is.EqualTo("slow area mez"));
            Assert.That(spells.All(s => s.Kind == BotCasterSurvival.ControlKind.Mez), Is.True);
        }
    }
}

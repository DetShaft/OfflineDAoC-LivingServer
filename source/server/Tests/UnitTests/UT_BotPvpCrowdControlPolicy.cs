using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // #152 (area mez cast too often) and #151 (group bots idle beside reserved enemies).
    // Rules are by spell data and role, so they apply the same way in every realm.
    [TestFixture, NonParallelizable]
    public class UT_BotPvpCrowdControlPolicy
    {
        private static Spell S(string type, int radius = 0, double castSeconds = 0, double value = 0, int level = 1) =>
            new(new DbSpell { Type = type, Target = "Enemy", Radius = radius, CastTime = castSeconds, Value = value, Duration = 20 }, level);

        // Shipped spell data (runtime DB, read 2026-11-19): Minstrel Lullaby 1,500 range, 250 radius, 5 s cast.
        private static Spell Lullaby() => S("Mesmerize", 250, 5.0, level: 15);
        // Primary area mezzes (Sorcerer, Bard, Healer) cast in 3 s.
        private static Spell PrimaryAreaMez() => S("Mesmerize", 350, 3.0, level: 20);
        private static Spell SingleMez() => S("Mesmerize", 0, 3.0, level: 10);
        private static Spell Stun() => S("Stun", 0, 0, level: 12);
        private static Spell Root() => S("SpeedDecrease", 0, 2.0, 99, level: 8);

        [Test]
        public void SlowBackupAreaMezIsTriedLast()
        {
            Spell lullaby = Lullaby(), single = SingleMez(), stun = Stun(), root = Root();
            var order = BotPvpCrowdControlPolicy.Order(new[] { lullaby, root, stun, single }).ToList();
            Assert.That(order.Last(), Is.SameAs(lullaby));
            Assert.That(order.First(), Is.SameAs(single), "Single-target mez still comes before other control.");
        }

        [Test]
        public void PrimaryAreaMezKeepsItsOpenerPlace()
        {
            Spell area = PrimaryAreaMez(), single = SingleMez(), root = Root();
            var order = BotPvpCrowdControlPolicy.Order(new[] { root, single, area }).ToList();
            Assert.That(order.First(), Is.SameAs(area));
            Assert.That(BotPvpCrowdControlPolicy.IsSlowAreaMez(area), Is.False);
            Assert.That(BotPvpCrowdControlPolicy.IsSlowAreaMez(Lullaby()), Is.True);
        }

        [Test]
        public void AreaMezNeedsTwoFreeEnemies()
        {
            Assert.That(BotPvpCrowdControlPolicy.AreaClusterWorthCasting(0), Is.False);
            Assert.That(BotPvpCrowdControlPolicy.AreaClusterWorthCasting(1), Is.False);
            Assert.That(BotPvpCrowdControlPolicy.AreaClusterWorthCasting(2), Is.True);
        }

        [Test]
        public void AreaMezIsHeldForTheImmunityLengthAfterAnAttempt()
        {
            Assert.That(BotPvpCrowdControlPolicy.AreaMezReady(5_000, null), Is.True, "Never tried: ready.");
            Assert.That(BotPvpCrowdControlPolicy.AreaMezReady(100_000, 50_000), Is.False);
            Assert.That(BotPvpCrowdControlPolicy.AreaMezReady(110_000, 50_000), Is.True);
        }

        [Test]
        public void OnlyMezAndRootAvoidEnemiesUnderAttack()
        {
            Assert.That(BotPvpCrowdControlPolicy.BreaksOnDamage(BotCasterSurvival.ControlKind.Mez), Is.True);
            Assert.That(BotPvpCrowdControlPolicy.BreaksOnDamage(BotCasterSurvival.ControlKind.Root), Is.True);
            Assert.That(BotPvpCrowdControlPolicy.BreaksOnDamage(BotCasterSurvival.ControlKind.Stun), Is.False);
            Assert.That(BotPvpCrowdControlPolicy.BreaksOnDamage(BotCasterSurvival.ControlKind.Snare), Is.False);
        }

        [Test]
        public void ClaimLivesThroughStartGraceThenOnlyWhileItsCastRuns()
        {
            Assert.That(BotPvpCrowdControl.ClaimCastAlive(1_000, 42, 1_500, null), Is.True, "Start grace.");
            Assert.That(BotPvpCrowdControl.ClaimCastAlive(1_000, 42, 4_000, 42), Is.True, "Cast still running.");
            Assert.That(BotPvpCrowdControl.ClaimCastAlive(1_000, 42, 4_000, null), Is.False, "Cast ended or failed.");
            Assert.That(BotPvpCrowdControl.ClaimCastAlive(1_000, 42, 4_000, 7), Is.False, "Caster moved on to another spell.");
            Assert.That(BotPvpCrowdControl.ClaimCastAlive(1_000, 0, 4_000, 0), Is.False, "A claim without a spell has only the grace.");
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
        }

        private static void SetLoopTime(long value) =>
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime), BindingFlags.Public | BindingFlags.Static)!
                .GetSetMethod(true)!.Invoke(null, new object[] { value });

        [Test]
        public void AStaleClaimNoLongerBlocksAnotherCaster()
        {
            long before = GameLoop.GameLoopTime;
            try
            {
                var first = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
                var second = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
                var enemy = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
                first.Realm = second.Realm = eRealm.Albion;
                enemy.Realm = eRealm.Midgard;
                SetLoopTime(100_000);
                Assert.That(BotPvpCrowdControl.Reserve(first, enemy, 7_000, 42, BotCasterSurvival.ControlKind.Mez), Is.True);
                Assert.That(BotPvpCrowdControl.Reserve(second, enemy, 7_000, 43, BotCasterSurvival.ControlKind.Mez), Is.False,
                    "Inside the start grace the first caster keeps its claim.");
                // The first caster's cast never started or already ended (it is not casting): the claim is dead.
                SetLoopTime(100_000 + BotPvpCrowdControl.ClaimStartGraceMilliseconds + 500);
                Assert.That(BotPvpCrowdControl.Reserve(second, enemy, 7_000, 43, BotCasterSurvival.ControlKind.Mez), Is.True);
                BotPvpCrowdControl.Release(second, enemy);
            }
            finally
            {
                SetLoopTime(before);
            }
        }
    }
}

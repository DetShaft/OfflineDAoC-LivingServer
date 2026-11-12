using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Issue #32: Paladins recast chants over and over, even while moving.</summary>
    [TestFixture]
    public class UT_PaladinChantHold
    {
        private const int Hold = BotSongTwistPolicy.PaladinChantHoldMilliseconds;
        private const int Guard = BotSongTwistPolicy.TankChantRecastGuardMilliseconds;

        [Test]
        public void GroupedPaladinsHoldOneSituationalChant()
        {
            Assert.That(BotSongTwistPolicy.GroupPaladinAnchor(false, true), Is.EqualTo(eSpellType.EnduranceRegenBuff));
            Assert.That(BotSongTwistPolicy.GroupPaladinAnchor(true, true), Is.EqualTo(eSpellType.CombatHeal));
            Assert.That(BotSongTwistPolicy.GroupPaladinAnchor(true, false), Is.EqualTo(eSpellType.DamageAdd));
        }

        [Test]
        public void NothingRunningStartsTheChosenChant() =>
            Assert.That(BotSongTwistPolicy.MayStartTankChant(2, 0, 0, 0, 0, true, true, Hold), Is.True);

        [Test]
        public void TheRunningChantIsNeverCastAgain() =>
            Assert.That(BotSongTwistPolicy.MayStartTankChant(2, 2, 0, 0, 100_000, false, true, Hold), Is.False);

        [Test]
        public void AChantCastMomentsAgoIsNotCastAgainWhileItsPulseIsNotVisible()
        {
            Assert.That(BotSongTwistPolicy.MayStartTankChant(2, 0, 2, 10_000, 10_000 + Guard - 1, false, true, Hold), Is.False);
            Assert.That(BotSongTwistPolicy.MayStartTankChant(2, 0, 2, 10_000, 10_000 + Guard, false, true, Hold), Is.True,
                "after the guard a chant that really ended is started again");
        }

        [Test]
        public void APaladinKeepsItsChantWhileMoving() =>
            Assert.That(BotSongTwistPolicy.MayStartTankChant(3, 2, 2, 0, 100_000, true, true, Hold), Is.False);

        [Test]
        public void APaladinHoldsAChantBeforeSwitching()
        {
            Assert.That(BotSongTwistPolicy.MayStartTankChant(3, 2, 2, 50_000, 50_000 + Hold - 1, false, true, Hold), Is.False);
            Assert.That(BotSongTwistPolicy.MayStartTankChant(3, 2, 2, 50_000, 50_000 + Hold, false, true, Hold), Is.True);
        }

        [Test]
        public void AChantStartedBeforeThisBrainCastAnyIsReplacedAtOnce() =>
            Assert.That(BotSongTwistPolicy.MayStartTankChant(3, 2, 0, 0, 0, false, true, Hold), Is.True);

        [Test]
        public void AWardenStillSwitchesAsSoonAsItsAnchorChanges() =>
            Assert.That(BotSongTwistPolicy.MayStartTankChant(3, 2, 2, 50_000, 50_001, true, false, 0), Is.True);
    }
}

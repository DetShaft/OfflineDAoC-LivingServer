using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Gamebot kills pay a real player kill's experience (owner, 2026-11-21, option A), and battleground
    /// monster kills get battleground_mob_xp_rate. Unit tests of the formulas only; not an in-game check.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_GamebotKillExperience
    {
        private double _battlegroundRate;

        [SetUp]
        public void Save() => _battlegroundRate = Properties.BATTLEGROUND_MOB_XP_RATE;

        [TearDown]
        public void Restore() => Properties.BATTLEGROUND_MOB_XP_RATE = _battlegroundRate;

        // XPForLiving: level 15 = 28980, 17 = 57930, 19 = 115850.
        [Test]
        public void SoloSameLevelKillIsFourMobs()
        {
            Assert.That(AutonomousBotRealmPointRewards.CalculatePlayerKillExperience(57930, 57930, 1, 1.0, 125),
                Is.EqualTo(4 * 57930));
        }

        [Test]
        public void HigherVictimIsCappedAt125PercentOfTheAwardersOwnValue()
        {
            // A level 19 gamebot is worth 4 x 115850; a level 15 player's cap is 4 x 28980 x 125%.
            Assert.That(AutonomousBotRealmPointRewards.CalculatePlayerKillExperience(115850, 28980, 1, 1.0, 125),
                Is.EqualTo(4L * 28980 * 125 / 100));
        }

        [Test]
        public void GroupSplitsByContributorsAndDamageShare()
        {
            // Two group members did half of the damage: (4 x 57930 / 2) x 0.5.
            Assert.That(AutonomousBotRealmPointRewards.CalculatePlayerKillExperience(57930, 57930, 2, 0.5, 125),
                Is.EqualTo(4L * 57930 / 2 / 2));
        }

        [TestCase(0, 57930, 1, 1.0, 125)]
        [TestCase(57930, 57930, 0, 1.0, 125)]
        [TestCase(57930, 57930, 1, 0.0, 125)]
        [TestCase(57930, 57930, 1, 1.0, 0)]
        public void NothingToShareGivesNothing(long victim, long awarder, int count, double damage, int cap)
        {
            Assert.That(AutonomousBotRealmPointRewards.CalculatePlayerKillExperience(victim, awarder, count, damage, cap),
                Is.EqualTo(0));
        }

        [Test]
        public void BattlegroundRateOnlyForMonsterKillsInsideABattleground()
        {
            Properties.BATTLEGROUND_MOB_XP_RATE = 2.0;
            Assert.That(LevelRangeXpRates.BattlegroundMobRate(true, eXPSource.NPC), Is.EqualTo(2.0));
            Assert.That(LevelRangeXpRates.BattlegroundMobRate(false, eXPSource.NPC), Is.EqualTo(1.0));
            Assert.That(LevelRangeXpRates.BattlegroundMobRate(true, eXPSource.Player), Is.EqualTo(1.0));
            Assert.That(LevelRangeXpRates.BattlegroundMobRate(true, eXPSource.Quest), Is.EqualTo(1.0));
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(5000.0)]
        public void BadBattlegroundRateFallsBackToOne(double rate)
        {
            Properties.BATTLEGROUND_MOB_XP_RATE = rate;
            Assert.That(LevelRangeXpRates.BattlegroundMobRate(true, eXPSource.NPC), Is.EqualTo(1.0));
        }
    }
}

using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_CompanionLevelSync
    {
        [TestCase(20, 12, 20)]
        [TestCase(50, 49, 50)]
        [TestCase(21, 20, 21)]
        public void CompanionCatchesUpToItsOwner(int owner, int bot, int expected)
        {
            Assert.That(CompanionLevelSync.TargetLevel(owner, bot, 50), Is.EqualTo(expected));
        }

        [TestCase(10, 30)]
        [TestCase(20, 20)]
        public void CompanionNeverLevelsDown(int owner, int bot)
        {
            Assert.That(CompanionLevelSync.TargetLevel(owner, bot, 50), Is.EqualTo(bot));
        }

        [Test]
        public void TargetLevelStaysWithinTheLevelCap()
        {
            Assert.That(CompanionLevelSync.TargetLevel(60, 40, 50), Is.EqualTo(50));
            Assert.That(CompanionLevelSync.TargetLevel(0, 0, 50), Is.EqualTo(1));
        }

        [Test]
        public void OnlyACompanionInItsOwnersGroupFollowsTheOwner()
        {
            object group = new object();
            Assert.That(CompanionLevelSync.InOwnersGroup(group, group), Is.True);
            Assert.That(CompanionLevelSync.InOwnersGroup(new object(), group), Is.False);
            Assert.That(CompanionLevelSync.InOwnersGroup(null, null), Is.False);
            Assert.That(CompanionLevelSync.InOwnersGroup(group, null), Is.False);
        }

        [Test]
        public void HelperKitRollsInItsSpawnBandAndNeverGetsWorse()
        {
            for (int level = 1; level < 50; level++)
            {
                for (int roll = 0; roll < 100; roll++)
                {
                    int gear = CompanionLevelSync.GearLevel(true, level, 0, roll / 100d);
                    Assert.That(gear, Is.InRange(System.Math.Max(1, level - 10), level));
                    Assert.That(CompanionLevelSync.GearLevel(true, level, gear, roll / 100d), Is.EqualTo(gear));
                }
                Assert.That(CompanionLevelSync.GearLevel(true, level, level - 1, 0), Is.EqualTo(System.Math.Max(1, level - 1)));
            }
            Assert.That(CompanionLevelSync.GearLevel(true, 30, 0, 1.0), Is.EqualTo(30));
        }

        [Test]
        public void EndgameHelpersAndSavedCompanionsGetKitAtTheirLevel()
        {
            Assert.That(CompanionLevelSync.GearLevel(true, 50, 41, 0), Is.EqualTo(50));
            Assert.That(CompanionLevelSync.GearLevel(false, 27, 10, 0), Is.EqualTo(27));
        }
    }
}

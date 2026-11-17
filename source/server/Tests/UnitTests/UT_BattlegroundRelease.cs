using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BattlegroundRelease
    {
        [Test]
        public void NormalReleaseInsideKillaloeGoesToTheBattlegroundPortalKeep()
        {
            Assert.That(BattlegroundRelease.ChooseNormalRelease(253, zoneIsBattleground: true),
                Is.EqualTo(eReleaseType.Battleground));
        }

        [Test]
        public void ReleaseBindIsNotRewrittenByTheBattlegroundRule()
        {
            Assert.That(eReleaseType.Bind, Is.Not.EqualTo(eReleaseType.Battleground));
            Assert.That(BattlegroundRelease.ChooseNormalRelease(1, zoneIsBattleground: false),
                Is.EqualTo(eReleaseType.Bind));
        }

        [Test]
        public void NewFrontiersStillUsesTheBorderKeepRelease()
        {
            Assert.That(BattlegroundRelease.ChooseNormalRelease(163, zoneIsBattleground: false),
                Is.EqualTo(eReleaseType.NewFrontiers));
        }

        [Test]
        public void AlbionPortalKeepMatchesAlbionAndIgnoresDunKillaloe()
        {
            Assert.That(BattlegroundRelease.IsFriendlyPortalKeep(true, "Albion Portal Keep", eRealm.Albion, eRealm.Albion),
                Is.True);
            Assert.That(BattlegroundRelease.IsFriendlyPortalKeep(false, "Dun Killaloe", eRealm.Midgard, eRealm.Albion),
                Is.False);
            Assert.That(BattlegroundRelease.IsFriendlyPortalKeep(true, "Midgard Portal Keep", eRealm.Midgard, eRealm.Albion),
                Is.False);
        }
    }
}

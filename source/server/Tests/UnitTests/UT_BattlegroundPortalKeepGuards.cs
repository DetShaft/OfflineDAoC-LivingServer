using DOL.GS;
using DOL.GS.Keeps;
using NUnit.Framework;
using PortalKeep = DOL.GS.Keeps.BattlegroundPortalKeepGuards.PortalKeep;

namespace DOL.UnitTests
{
    /// <summary>
    /// Issue #75: the Abermenai and Murdaigean portal keeps had no guards. A guardless battleground portal keep takes the
    /// guards of its realm's portal keep in another battleground at the same spot. Keep rows as in the world (KeepID,
    /// region, realm, X, Y, Z, heading).
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundPortalKeepGuards
    {
        private static readonly PortalKeep ThidrankiHib = new(12, 252, eRealm.Hibernia, 18362, 18257, 4320, 3517, 11);
        private static readonly PortalKeep ThidrankiMid = new(13, 252, eRealm.Midgard, 54053, 24680, 4320, 346, 11);
        private static readonly PortalKeep CaledoniaHib = new(38, 250, eRealm.Hibernia, 18362, 18257, 4320, 3517, 11);
        private static readonly PortalKeep AbermenaiHib = new(35, 253, eRealm.Hibernia, 18362, 18257, 4320, 3517, 0);
        private static readonly PortalKeep AbermenaiMid = new(36, 253, eRealm.Midgard, 54053, 24680, 4320, 346, 0);
        private static readonly PortalKeep MurdaigeanHib = new(41, 251, eRealm.Hibernia, 18362, 18257, 4320, 3517, 0);

        private static readonly PortalKeep[] World = [ThidrankiHib, ThidrankiMid, CaledoniaHib, AbermenaiHib, AbermenaiMid, MurdaigeanHib];

        [Test]
        public void AbermenaiTakesItsRealmsGuardsFromAnotherBattleground()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib, World)?.KeepId, Is.EqualTo(12));
                Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiMid, World)?.KeepId, Is.EqualTo(13));
                Assert.That(BattlegroundPortalKeepGuards.Donor(MurdaigeanHib, World)?.KeepId, Is.EqualTo(12));
            });
        }

        [Test]
        public void TheBestGuardedKeepGivesItsGuards() =>
            Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib, [ThidrankiHib with { Guards = 9 }, CaledoniaHib])?.KeepId,
                Is.EqualTo(38));

        [Test]
        public void AKeepWithGuardsKeepsThem() =>
            Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib with { Guards = 3 }, World), Is.Null);

        [Test]
        public void OnlyTheSameRealmGivesGuards() =>
            Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib, [ThidrankiMid]), Is.Null);

        [Test]
        public void GuardlessKeepsGiveNothing() =>
            Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib, [AbermenaiHib, MurdaigeanHib]), Is.Null);

        [TestCase(65, 0, 0, (ushort)3517)]
        [TestCase(0, -65, 0, (ushort)3517)]
        [TestCase(0, 0, 65, (ushort)3517)]
        [TestCase(0, 0, 0, (ushort)3518)]
        public void AKeepElsewhereGivesNothing(int dx, int dy, int dz, ushort heading) =>
            Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib,
                [ThidrankiHib with { X = ThidrankiHib.X + dx, Y = ThidrankiHib.Y + dy, Z = ThidrankiHib.Z + dz, Heading = heading }]), Is.Null);

        [Test]
        public void ASmallOffsetStillCounts() =>
            Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib, [ThidrankiHib with { X = ThidrankiHib.X + 64, Z = ThidrankiHib.Z - 64 }])?.KeepId,
                Is.EqualTo(12));

        [Test]
        public void OnlyTheFourBattlegroundsTakePart()
        {
            // Cathal Valley (165) has portal keeps at its own spots and is not a battleground on this server.
            PortalKeep cathalHib = AbermenaiHib with { KeepId = 3, Region = 165 };
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundPortalKeepGuards.Donor(cathalHib, World), Is.Null);
                Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib, [cathalHib with { Guards = 11 }]), Is.Null);
            });
        }

        [Test]
        public void AKeepNeverTakesGuardsFromItsOwnBattleground() =>
            Assert.That(BattlegroundPortalKeepGuards.Donor(AbermenaiHib, [ThidrankiHib with { KeepId = 99, Region = 253 }]), Is.Null);
    }
}

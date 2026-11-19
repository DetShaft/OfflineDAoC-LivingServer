using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Battleground siege tokens: priced for the bracket, and their ids carry the engine and the region.</summary>
    [TestFixture]
    public class UT_BattlegroundSiegeTokens
    {
        [Test]
        public void PriceScalesWithTheBracketsTopLevel()
        {
            Assert.That(BattlegroundSiegeTokens.Price(19), Is.EqualTo(27_400), "Killaloe: 2g 74s, not the frontier's 50g.");
            Assert.That(BattlegroundSiegeTokens.Price(24), Is.EqualTo(55_300));
            Assert.That(BattlegroundSiegeTokens.Price(29), Is.EqualTo(97_600));
            Assert.That(BattlegroundSiegeTokens.Price(35), Is.EqualTo(171_500));
            Assert.That(BattlegroundSiegeTokens.Price(50), Is.EqualTo(BattlegroundSiegeTokens.FrontierKitPrice));
            Assert.That(BattlegroundSiegeTokens.Price(1), Is.EqualTo(100), "At least one silver.");
        }

        [Test]
        public void TokenIdsRoundTrip()
        {
            foreach (BotSiegeKind kind in BattlegroundSiegeTokens.Kinds)
            {
                string id = BattlegroundSiegeTokens.TokenId(kind, 253);
                Assert.That(BattlegroundSiegeTokens.TryParse(id, out BotSiegeKind parsed, out ushort region), Is.True, id);
                Assert.That(parsed, Is.EqualTo(kind));
                Assert.That(region, Is.EqualTo((ushort)253));
            }
            Assert.That(BattlegroundSiegeTokens.TryParse("deploy_siege_ram", out _, out _), Is.False);
            Assert.That(BattlegroundSiegeTokens.TryParse("offline_bg_siege_token_ram", out _, out _), Is.False);
        }

        [Test]
        public void EngineerKeepsRealmSpeedAndOutrunsAFasterPlayer()
        {
            Assert.That(SiegeEngineerPlan.RealmSpeed, Is.EqualTo((short)390), "191 x 2.04: a player's run at the top speed buff.");
            Assert.That(SiegeEngineerPlan.Speed(191), Is.EqualTo(SiegeEngineerPlan.RealmSpeed), "On foot: realm speed.");
            Assert.That(SiegeEngineerPlan.Speed(0), Is.EqualTo(SiegeEngineerPlan.RealmSpeed));
            Assert.That(SiegeEngineerPlan.Speed(400), Is.EqualTo((short)520), "A mounted player: a third faster than the player.");
            Assert.That(SiegeEngineerPlan.Speed(int.MaxValue), Is.EqualTo(short.MaxValue));
        }

        [Test]
        public void RamBandMatchesTheRamSpell()
        {
            Assert.That(SiegeEngineerPlan.InRamBand(199), Is.False, "Too close for the ram spell.");
            Assert.That(SiegeEngineerPlan.InRamBand(200), Is.True);
            Assert.That(SiegeEngineerPlan.InRamBand(SiegeEngineerPlan.RamStagingRange), Is.True);
            Assert.That(SiegeEngineerPlan.InRamBand(500), Is.True);
            Assert.That(SiegeEngineerPlan.InRamBand(501), Is.False, "Too far for the ram spell.");
        }

        [Test]
        public void RamStagingPointIsOnThePlayersSideOfTheDoor()
        {
            // Player 2000 units east of the door: the engineer waits 320 east of it.
            Assert.That(SiegeEngineerPlan.RamStagingPoint(1000, 1000, 3000, 1000, 0, 0), Is.EqualTo((1320, 1000)));
            // Player hugging the door (the screenshot in #148): use the side the engineer stands on, here south.
            Assert.That(SiegeEngineerPlan.RamStagingPoint(1000, 1000, 1010, 1000, 1000, 5000), Is.EqualTo((1000, 1320)));
            // Everyone on the door's origin: still a point in the band.
            (int x, int y) = SiegeEngineerPlan.RamStagingPoint(1000, 1000, 1000, 1000, 1000, 1000);
            double distance = System.Math.Sqrt((x - 1000) * (x - 1000) + (y - 1000) * (y - 1000));
            Assert.That(SiegeEngineerPlan.InRamBand(distance), Is.True);
        }
    }
}

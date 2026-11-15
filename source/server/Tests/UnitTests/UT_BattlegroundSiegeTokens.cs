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
    }
}

using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>#56: a resting bot party lights the client's stock campfire at an outdoor camp only, for its rest and a minute more.</summary>
    [TestFixture]
    public class UT_BotCampfire
    {
        [Test]
        public void OutdoorCampsOnly()
        {
            Assert.That(BotCampfire.Wanted(dungeonCamp: false, regionIsDungeon: false), Is.True);
            Assert.That(BotCampfire.Wanted(dungeonCamp: true, regionIsDungeon: false), Is.False);
            Assert.That(BotCampfire.Wanted(dungeonCamp: false, regionIsDungeon: true), Is.False);
        }

        [Test]
        public void BurnsThroughTheRestAndAMinuteMore()
        {
            Assert.That(BotCampfire.BurnMilliseconds, Is.EqualTo(CampAnchorPolicy.HoldMilliseconds + 60_000));
            Assert.That(BotCampfire.CampfireModel, Is.EqualTo((ushort)2656), "the client's stock Campfire object, no new art");
        }
    }
}

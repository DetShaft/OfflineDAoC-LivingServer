using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #114 (Killaloe, v1.1.20): bots in the player's group froze in keep gate passages while the player went on.
    [TestFixture]
    public class UT_PlayerBotKeepDoorPassage
    {
        // Heading 0 faces +Y (GetPointFromHeading: x - sin, y + cos).
        private static readonly Vector2 Door = new(1000, 1000);

        [Test]
        public void InFront_FollowsTheDoorHeading()
        {
            Assert.That(PlayerBotKeepDoorPassage.InFront(Door, 0, new Vector2(1000, 1200)), Is.True);
            Assert.That(PlayerBotKeepDoorPassage.InFront(Door, 0, new Vector2(1000, 800)), Is.False);
            // Heading 1024 (90 degrees) faces -X.
            Assert.That(PlayerBotKeepDoorPassage.InFront(Door, 1024, new Vector2(800, 1000)), Is.True);
            Assert.That(PlayerBotKeepDoorPassage.InFront(Door, 1024, new Vector2(1200, 1000)), Is.False);
        }

        [Test]
        public void ABotInThePassage_AndThePlayerOutside_AreSeparated()
        {
            // Killaloe 2026-11-17: bots ~550 inside the gate, the player out in the field.
            Vector2 bot = new(1000, 450), player = new(1050, 1600);
            Assert.That(PlayerBotKeepDoorPassage.Separates(Door, 0, bot, player), Is.True);
        }

        [Test]
        public void BothOnTheSameSide_AreNotSeparated()
        {
            Assert.That(PlayerBotKeepDoorPassage.Separates(Door, 0, new Vector2(1000, 600), new Vector2(1100, 300)), Is.False);
        }

        [Test]
        public void ADoorOffToTheSide_IsNotTheDoorBetweenThem()
        {
            // Opposite sides of the door's (endless) plane, but the line between them passes 1,500 units away from it.
            Vector2 bot = new(2500, 800), player = new(2500, 1300);
            Assert.That(PlayerBotKeepDoorPassage.Separates(Door, 0, bot, player), Is.False);
        }

        [Test]
        public void ABotFarFromTheDoor_IsLeftToItsOwnRoute()
        {
            Vector2 bot = new(1000, 1000 - PlayerBotKeepDoorPassage.SearchRadius - 100), player = new(1000, 1400);
            Assert.That(PlayerBotKeepDoorPassage.Separates(Door, 0, bot, player), Is.False);
        }

        [Test]
        public void Decide_ClosedOwnDoor_PassesAtOnceFromClose_ButNotFromFar()
        {
            Assert.That(PlayerBotKeepDoorPassage.Decide(ownRealmDoor: true, doorOpen: false, botToDoor: 200, stalled: false),
                Is.EqualTo(PlayerBotKeepDoorPassage.Reason.ClosedOwnDoor));
            Assert.That(PlayerBotKeepDoorPassage.Decide(true, false, 700, stalled: false),
                Is.EqualTo(PlayerBotKeepDoorPassage.Reason.None), "walks closer first");
            Assert.That(PlayerBotKeepDoorPassage.Decide(true, false, 700, stalled: true),
                Is.EqualTo(PlayerBotKeepDoorPassage.Reason.Stalled), "frozen in the passage");
        }

        [Test]
        public void Decide_AnOpenDoor_OnlyWhenStalled()
        {
            Assert.That(PlayerBotKeepDoorPassage.Decide(false, doorOpen: true, 100, stalled: false),
                Is.EqualTo(PlayerBotKeepDoorPassage.Reason.None), "an open door is walked through");
            Assert.That(PlayerBotKeepDoorPassage.Decide(false, doorOpen: true, 100, stalled: true),
                Is.EqualTo(PlayerBotKeepDoorPassage.Reason.Stalled), "a rammed door the bot cannot get past (#127)");
        }

        [Test]
        public void Decide_AClosedEnemyDoor_IsNeverPassed()
        {
            Assert.That(PlayerBotKeepDoorPassage.Decide(false, false, 50, stalled: true), Is.EqualTo(PlayerBotKeepDoorPassage.Reason.None));
        }

        [Test]
        public void ABotStandingStillOnPurpose_IsNotPassed()
        {
            // Squire review on PR #129: a bot told to stay, casting, holding a healer post or fighting is not stalled.
            Assert.That(PlayerBotKeepDoorPassage.HoldsPosition(false, false, false, false), Is.False);
            Assert.That(PlayerBotKeepDoorPassage.HoldsPosition(toldToStay: true, false, false, false), Is.True);
            Assert.That(PlayerBotKeepDoorPassage.HoldsPosition(false, casting: true, false, false), Is.True);
            Assert.That(PlayerBotKeepDoorPassage.HoldsPosition(false, false, holdingHealerPost: true, false), Is.True);
            Assert.That(PlayerBotKeepDoorPassage.HoldsPosition(false, false, false, fighting: true), Is.True);
        }

        [Test]
        public void DistanceToSegment()
        {
            Assert.That(PlayerBotKeepDoorPassage.DistanceToSegment(new Vector2(0, 5), new Vector2(-10, 0), new Vector2(10, 0)), Is.EqualTo(5).Within(0.01));
            Assert.That(PlayerBotKeepDoorPassage.DistanceToSegment(new Vector2(20, 0), new Vector2(-10, 0), new Vector2(10, 0)), Is.EqualTo(10).Within(0.01));
        }
    }
}

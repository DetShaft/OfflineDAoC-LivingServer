using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #128 (Jay, v1.1.20): bots in the player's group sprint alongside the player and to chase.
    [TestFixture]
    public class UT_BotSprint
    {
        [Test]
        public void Speed_IsTheSprintFactor_WhenSprinting()
        {
            Assert.That(BotSprint.Speed(191, 191, sprinting: true), Is.EqualTo(248));
            Assert.That(BotSprint.Speed(191, 191, sprinting: false), Is.EqualTo(191));
        }

        [Test]
        public void Speed_NeverLowersAFasterPace()
        {
            Assert.That(BotSprint.Speed(191, 300, sprinting: true), Is.EqualTo(300));
            Assert.That(BotSprint.Speed(0, 0, sprinting: true), Is.EqualTo(0));
        }

        [Test]
        public void ChaseWindow_SprintsForTheMaximum_ThenRests()
        {
            long since = 0, cooldown = 0;
            Assert.That(BotSprint.ChaseWindow(true, 1_000, ref since, ref cooldown), Is.True);
            Assert.That(BotSprint.ChaseWindow(true, 1_000 + BotSprint.ChaseMaxMilliseconds, ref since, ref cooldown), Is.True);
            Assert.That(BotSprint.ChaseWindow(true, 2_000 + BotSprint.ChaseMaxMilliseconds, ref since, ref cooldown), Is.False);
            long rested = 2_000 + BotSprint.ChaseMaxMilliseconds + BotSprint.ChaseCooldownMilliseconds;
            Assert.That(BotSprint.ChaseWindow(true, rested - 1, ref since, ref cooldown), Is.False, "still resting");
            Assert.That(BotSprint.ChaseWindow(true, rested, ref since, ref cooldown), Is.True);
        }

        [Test]
        public void ChaseWindow_NoChase_NoSprint_AndAShortRestAfterwards()
        {
            long since = 0, cooldown = 0;
            Assert.That(BotSprint.ChaseWindow(false, 1_000, ref since, ref cooldown), Is.False);
            Assert.That(cooldown, Is.EqualTo(0));
            BotSprint.ChaseWindow(true, 1_000, ref since, ref cooldown);
            BotSprint.ChaseWindow(false, 2_000, ref since, ref cooldown);
            Assert.That(cooldown, Is.EqualTo(2_000 + BotSprint.ChaseCooldownMilliseconds / 3));
        }
    }
}

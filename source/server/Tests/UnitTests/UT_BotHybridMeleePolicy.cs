using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #126: a Reaver cast an instant spell every turn and each one stopped its swing.
    [TestFixture]
    public class UT_BotHybridMeleePolicy
    {
        [Test]
        public void ReaverAndClericAreMeleeBots_NotRangedCasters()
        {
            Assert.That(BotBrain.PrefersSpellRange(new ClassReaver(), true, true), Is.False);
            Assert.That(BotBrain.PrefersSpellRange(new ClassCleric(), true, true), Is.False);
        }

        [TestCase("Lifedrain", 0, true)]
        [TestCase("DamageOverTime", 0, true)]
        [TestCase("DirectDamage", 0, true)]
        [TestCase("DirectDamage", 3, false)]
        public void OnlyInstantSpellsLetTheMeleeSwingGoOn(string type, double castTime, bool expected)
        {
            var spell = new Spell(new DbSpell { Type = type, Target = "Enemy", CastTime = castTime }, 1);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, null, true, spell), Is.EqualTo(expected));
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, spell, false, null), Is.EqualTo(expected));
        }

        [Test]
        public void RangedCastersAndNoSpellActionAreUnchanged()
        {
            var instant = new Spell(new DbSpell { Type = "Lifedrain", Target = "Enemy", CastTime = 0 }, 1);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(false, true, false, null, true, instant), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, false, false, null, false, null), Is.False);
        }

        [Test]
        public void UnknownRunningOrQueuedCastsStillStopTheSwing()
        {
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, null, true, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, true, null, false, null), Is.False);
            Assert.That(BotHybridMeleePolicy.ContinueMeleeAfterInstant(true, true, false, null, false, null), Is.True);
        }
    }
}

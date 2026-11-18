using System;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #136: casters kite player-shaped melee instead of meleeing; melee peels for healers, then casters.
    [TestFixture]
    public class UT_BotCasterSurvival
    {
        private static Spell S(string type, double value = 0, double castTime = 0) =>
            new(new DbSpell { Type = type, Target = "Enemy", Value = value, CastTime = castTime, Duration = 9 }, 1);

        [Test]
        public void NoThreatIsTheOrdinaryTurn()
        {
            Assert.That(BotCasterSurvival.Decide(false, false, true, true, true, true, true), Is.EqualTo(BotKiteAction.None));
        }

        [Test]
        public void ControlComesFirst_InstantBeforeQuickcast()
        {
            Assert.That(BotCasterSurvival.Decide(true, false, true, true, true, true, true), Is.EqualTo(BotKiteAction.InstantControl));
            Assert.That(BotCasterSurvival.Decide(true, false, false, true, true, true, true), Is.EqualTo(BotKiteAction.QuickcastControl));
        }

        [Test]
        public void WithoutControlTheCasterRuns()
        {
            Assert.That(BotCasterSurvival.Decide(true, false, false, false, true, true, true), Is.EqualTo(BotKiteAction.Run));
        }

        [Test]
        public void AHeldAttackerIsRunFromWithoutSpendingControl()
        {
            // Already stunned/mezzed/rooted: no second control spell, run (legs from a held attacker are free).
            Assert.That(BotCasterSurvival.Decide(true, true, true, true, true, false, true), Is.EqualTo(BotKiteAction.Run));
            // Cannot run (rooted itself): cast from where it stands, never melee a held attacker.
            Assert.That(BotCasterSurvival.Decide(true, true, true, true, false, false, false), Is.EqualTo(BotKiteAction.HoldAndCast));
        }

        [Test]
        public void MeleeIsTheLastResort()
        {
            Assert.That(BotCasterSurvival.Decide(true, false, false, false, true, false, true), Is.EqualTo(BotKiteAction.QuickcastNuke));
            Assert.That(BotCasterSurvival.Decide(true, false, false, false, false, true, false), Is.EqualTo(BotKiteAction.Melee));
            Assert.That(BotCasterSurvival.Decide(true, false, false, false, true, false, false), Is.EqualTo(BotKiteAction.Melee));
        }

        [TestCase("Stun", 0, BotCasterSurvival.ControlKind.Stun)]
        [TestCase("Mesmerize", 0, BotCasterSurvival.ControlKind.Mez)]
        [TestCase("SpeedDecrease", 99, BotCasterSurvival.ControlKind.Root)]
        [TestCase("SpeedDecrease", 40, BotCasterSurvival.ControlKind.Snare)]
        [TestCase("DamageSpeedDecrease", 35, BotCasterSurvival.ControlKind.Snare)]
        [TestCase("SpeedDecrease", 10, BotCasterSurvival.ControlKind.None)]
        [TestCase("DirectDamage", 0, BotCasterSurvival.ControlKind.None)]
        public void ControlKinds(string type, double value, BotCasterSurvival.ControlKind expected)
        {
            Assert.That(BotCasterSurvival.KindOf(S(type, value)), Is.EqualTo(expected));
        }

        [Test]
        public void ControlOrder_StunMezRootSnare()
        {
            Assert.That(BotCasterSurvival.ControlKind.Stun, Is.LessThan(BotCasterSurvival.ControlKind.Mez));
            Assert.That(BotCasterSurvival.ControlKind.Mez, Is.LessThan(BotCasterSurvival.ControlKind.Root));
            Assert.That(BotCasterSurvival.ControlKind.Root, Is.LessThan(BotCasterSurvival.ControlKind.Snare));
        }

        [Test]
        public void LegBudgetCountsOnlyTheWindow()
        {
            long now = 100_000;
            Assert.That(BotCasterSurvival.LegsLeft(new long[3], now), Is.True);
            Assert.That(BotCasterSurvival.LegsLeft(new long[] { now - 1_000, now - 2_000, now - 3_000 }, now), Is.False);
            Assert.That(BotCasterSurvival.LegsLeft(new long[] { now - 1_000, now - 2_000, now - 30_000 }, now), Is.True);
        }

        [Test]
        public void AwayPointRunsStraightAwayFromTheAttacker()
        {
            (int x, int y) = BotCasterSurvival.AwayPoint(1000, 1000, 900, 1000, 550, 0);
            Assert.That(x, Is.EqualTo(1550));
            Assert.That(y, Is.EqualTo(1000));
            (x, y) = BotCasterSurvival.AwayPoint(1000, 1000, 1000, 900, 550, 90);
            Assert.That(x, Is.EqualTo(450));
            Assert.That(y, Is.EqualTo(1000));
        }

        // The same rule in every realm: tanks and heavy melee peel, anyone with a shield peels, casters,
        // back-line supports and archers do not.
        [TestCase(eCharacterClass.Armsman, false, true)]
        [TestCase(eCharacterClass.Warrior, false, true)]
        [TestCase(eCharacterClass.Hero, false, true)]
        [TestCase(eCharacterClass.Mercenary, false, true)]
        [TestCase(eCharacterClass.Berserker, false, true)]
        [TestCase(eCharacterClass.Blademaster, false, true)]
        [TestCase(eCharacterClass.Friar, true, true)]
        [TestCase(eCharacterClass.Skald, true, true)]
        [TestCase(eCharacterClass.Warden, true, true)]
        [TestCase(eCharacterClass.Friar, false, false)]
        [TestCase(eCharacterClass.Skald, false, false)]
        [TestCase(eCharacterClass.Warden, false, false)]
        [TestCase(eCharacterClass.Cleric, true, false)]
        [TestCase(eCharacterClass.Healer, true, false)]
        [TestCase(eCharacterClass.Druid, true, false)]
        [TestCase(eCharacterClass.Scout, true, false)]
        [TestCase(eCharacterClass.Infiltrator, false, false)]
        public void Peelers(eCharacterClass characterClass, bool shield, bool expected)
        {
            Assert.That(BotCasterSurvival.IsPeeler(characterClass, shield, false), Is.EqualTo(expected));
        }

        [Test]
        public void RangedCastersNeverPeel()
        {
            Assert.That(BotCasterSurvival.IsPeeler(eCharacterClass.Valewalker, false, true), Is.False);
        }

        [Test]
        public void HealersArePeeledForFirst_ThenCasters()
        {
            Assert.That(BotCasterSurvival.PeelPriority(true, false), Is.LessThan(BotCasterSurvival.PeelPriority(false, true)));
            Assert.That(BotCasterSurvival.PeelPriority(false, false), Is.EqualTo(-1));
            foreach (eCharacterClass healer in new[] { eCharacterClass.Cleric, eCharacterClass.Healer, eCharacterClass.Druid })
                Assert.That(BotCasterSurvival.IsPeelHealer(healer), Is.True, healer.ToString());
        }

        [Test]
        public void EachRealmsPureCastersArePeeledForAsCasters()
        {
            foreach (ICharacterClass caster in new ICharacterClass[]
                     { new ClassWizard(), new ClassRunemaster(), new ClassEldritch(), new ClassEnchanter(), new ClassSpiritmaster(), new ClassCabalist() })
                Assert.That(BotCasterSurvival.IsPeelCaster(caster), Is.True, caster.Name);
            Assert.That(BotCasterSurvival.IsPeelCaster(new ClassArmsman()), Is.False);
        }
    }
}

using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #35 (owner, 2026-11-17): the level-40 Bone Magus, Keeper and Captain, and Bonedancer pet spell accuracy.
    [TestFixture]
    public class UT_BonedancerSpecialtyCommanders
    {
        [Test]
        public void EachSpecLine_TeachesItsCommanderAtForty()
        {
            var byLine = BonedancerSpecialtyCommanders.Commanders.ToDictionary(c => c.Line);
            Assert.That(byLine["Bone Mystics"].Name, Is.EqualTo("Summon Bone Magus"));
            Assert.That(byLine["Bone Guardians"].Name, Is.EqualTo("Summon Bone Keeper"));
            Assert.That(byLine["Bone Warriors"].Name, Is.EqualTo("Summon Bone Captain"));
            Assert.That(byLine["Bone Mystics"].TemplateId, Is.EqualTo(212), "dread lich");
            Assert.That(byLine["Bone Guardians"].TemplateId, Is.EqualTo(213), "dread guardian");
            Assert.That(byLine["Bone Warriors"].TemplateId, Is.EqualTo(211), "dread archer");
            Assert.That(BonedancerSpecialtyCommanders.LineLevel, Is.EqualTo(40));
            Assert.That(BonedancerSpecialtyCommanders.Commanders.Select(c => c.SpellId).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void TheKeeper_GetsItsSuppressSpells()
        {
            var keeper = BonedancerSpecialtyCommanders.Commanders.Single(c => c.Name == "Summon Bone Keeper");
            Assert.That(keeper.TemplateSpells.Split(';'), Is.EquivalentTo(new[] { "60119", "10085", "2658" }));
        }

        [Test]
        public void TheSummonRow_IsEightyFivePercentForTwentyPercentPower()
        {
            var commander = BonedancerSpecialtyCommanders.Commanders[0];
            var row = new DbSpell();
            Assert.That(BonedancerSpecialtyCommanders.Matches(row, commander), Is.False);
            BonedancerSpecialtyCommanders.Fill(row, commander);
            Assert.That(row.Damage, Is.EqualTo(-85));
            Assert.That(row.Power, Is.EqualTo(-20));
            Assert.That(row.CastTime, Is.EqualTo(6.0));
            Assert.That(row.Type, Is.EqualTo("SummonCommander"));
            Assert.That(row.LifeDrainReturn, Is.EqualTo(212));
            Assert.That(row.Description, Does.Contain("3 additional pets"));
            Assert.That(BonedancerSpecialtyCommanders.Matches(row, commander), Is.True, "a second start writes nothing");
        }

        [Test]
        public void ACommander_LandsSpellsLikeItsOwner_AMinionHalfway()
        {
            // To-hit against a level-50 target: 87.5 + (caster - 50) / 2 + (caster - 50) for an NPC caster.
            static double HitVsFifty(int petLevel, double bonus) => 87.5 + (petLevel - 50) / 2.0 + (petLevel - 50) + bonus;

            double commander = HitVsFifty(42, BonedancerPetAccuracy.Bonus(true, 50, 42));
            double minion = HitVsFifty(37, BonedancerPetAccuracy.Bonus(false, 50, 37));
            Assert.That(commander, Is.EqualTo(87.5), "a level-42 commander of a level-50 Bonedancer is resisted 12.5%, like a player");
            Assert.That(minion, Is.EqualTo(77.75), "a level-37 minion still misses more than its commander");
            Assert.That(minion, Is.GreaterThan(HitVsFifty(37, 0)));
            Assert.That(BonedancerPetAccuracy.Bonus(true, 40, 45), Is.EqualTo(0), "no bonus above the owner's level");
        }
    }
}

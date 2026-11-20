using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>#178: Cleric bots smite single targets only, in PvE and PvP.</summary>
    [TestFixture]
    public class UT_ClericBotSpellPolicy
    {
        [Test]
        public void ClericsSkipAreaSmites()
        {
            Assert.That(ClericBotSpellPolicy.AllowsOffense(eCharacterClass.Cleric, 0), Is.True, "Single-target smite.");
            Assert.That(ClericBotSpellPolicy.AllowsOffense(eCharacterClass.Cleric, 350), Is.False, "Area smite.");
        }

        [Test]
        public void OtherClassesKeepTheirAreaSpells()
        {
            Assert.That(ClericBotSpellPolicy.AllowsOffense(eCharacterClass.Wizard, 350), Is.True);
            Assert.That(ClericBotSpellPolicy.AllowsOffense(eCharacterClass.Friar, 350), Is.True);
            Assert.That(ClericBotSpellPolicy.AllowsOffense(eCharacterClass.Druid, 350), Is.True);
        }
    }
}

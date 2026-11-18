using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #115 (hand a siege token in by name), #133 (which group bots hit an enemy keep door). The door rule reads class
    /// types only, so each realm's tanks, melee and archers help and each realm's casters and healers hold back
    /// (Bards and Shamans hold the back line per #48; Minstrels and Skalds melee).
    /// </summary>
    [TestFixture]
    public class UT_SiegeDoorGroupHelp
    {
        [TestCase("siege ram", BotSiegeKind.Ram)]
        [TestCase(" Ram ", BotSiegeKind.Ram)]
        [TestCase("ballista", BotSiegeKind.Ballista)]
        [TestCase("Field Ballista", BotSiegeKind.Ballista)]
        [TestCase("catapult", BotSiegeKind.Catapult)]
        [TestCase("TREBUCHET", BotSiegeKind.Trebuchet)]
        public void SiegeTokenKeywordsNameTheirEngine(string text, BotSiegeKind expected)
        {
            Assert.That(BattlegroundSiegeTokens.TryKeyword(text, out BotSiegeKind kind), Is.True, text);
            Assert.That(kind, Is.EqualTo(expected));
        }

        [TestCase("deploy")]
        [TestCase("")]
        [TestCase(null)]
        public void OtherWordsAreNotSiegeTokenKeywords(string text) =>
            Assert.That(BattlegroundSiegeTokens.TryKeyword(text, out _), Is.False);

        [TestCase(eCharacterClass.Armsman, eClassType.PureTank)]
        [TestCase(eCharacterClass.Warrior, eClassType.PureTank)]
        [TestCase(eCharacterClass.Hero, eClassType.PureTank)]
        [TestCase(eCharacterClass.Mercenary, eClassType.PureTank)]
        [TestCase(eCharacterClass.Berserker, eClassType.PureTank)]
        [TestCase(eCharacterClass.Blademaster, eClassType.PureTank)]
        [TestCase(eCharacterClass.Scout, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Hunter, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Ranger, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Friar, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Thane, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Warden, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Valewalker, eClassType.ListCaster)]
        [TestCase(eCharacterClass.Minstrel, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Skald, eClassType.Hybrid)]
        public void WeaponFightersOfEveryRealmHitDoors(eCharacterClass cls, eClassType type) =>
            Assert.That(BotKeepDoorAssist.HitsDoors(cls, type), Is.True, cls.ToString());

        [TestCase(eCharacterClass.Wizard, eClassType.ListCaster)]
        [TestCase(eCharacterClass.Runemaster, eClassType.ListCaster)]
        [TestCase(eCharacterClass.Eldritch, eClassType.ListCaster)]
        [TestCase(eCharacterClass.Cleric, eClassType.ListCaster)]
        [TestCase(eCharacterClass.Healer, eClassType.ListCaster)]
        [TestCase(eCharacterClass.Druid, eClassType.ListCaster)]
        [TestCase(eCharacterClass.Bard, eClassType.Hybrid)]
        [TestCase(eCharacterClass.Shaman, eClassType.ListCaster)]
        public void CastersAndBackLineSupportsDoNotHitDoors(eCharacterClass cls, eClassType type) =>
            Assert.That(BotKeepDoorAssist.HitsDoors(cls, type), Is.False, cls.ToString());
    }
}

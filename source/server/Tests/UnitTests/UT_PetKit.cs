using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;
using P = DOL.GS.CommanderPet.eCommanderPreferredSpell;
using T = DOL.GS.CommanderPet.eCommanderType;
using W = DOL.GS.PetOrders.Weapon;

namespace DOL.UnitTests
{
    // Issue #35 step 2 (owner, 2026-11-17): pet weapons, the taunt shield, and the commanders' spell choice.
    [TestFixture]
    public class UT_PetKit
    {
        [Test]
        public void EveryKitWeapon_HasAModelThatReadsBackAsThatWeapon()
        {
            foreach (W weapon in new[] { W.Sword, W.Axe, W.Hammer, W.Blade, W.Mace, W.Dagger })
            {
                Assert.That(PetKit.Model(weapon), Is.GreaterThan(0), weapon.ToString());
                Assert.That(PetKit.WeaponOfModel(PetKit.Model(weapon)), Is.EqualTo(weapon), weapon.ToString());
            }
        }

        [Test]
        public void TheSpiritPetsTwoHandedModels_AreNotOneHandedWeapons()
        {
            // The spirit pets' template rolls 311-324 and carries a spear (328): two-handers read as None, so the
            // pet is given a one-handed weapon of its kit instead.
            foreach (int twoHanded in new[] { 314, 317, 318, 328 })
                Assert.That(PetKit.WeaponOfModel(twoHanded), Is.EqualTo(W.None), twoHanded.ToString());
            Assert.That(PetKit.WeaponOfModel(311), Is.EqualTo(W.Sword));
            Assert.That(PetKit.WeaponOfModel(316), Is.EqualTo(W.Axe));
            Assert.That(PetKit.WeaponOfModel(320), Is.EqualTo(W.Hammer));
        }

        [Test]
        public void ShieldStyles_AreTheRealmFightersUpToThePetsLevel()
        {
            var asked = new List<(string Spec, int Class)>();
            List<Style> Source(string spec, int classId)
            {
                asked.Add((spec, classId));
                return new List<Style> { StyleOf("Numb", 1, 4), StyleOf("Stun", 2, 12), StyleOf("Slam", 3, 40) };
            }

            List<Style> styles = PetKit.ShieldStyles(20, eRealm.Midgard, Source);
            Assert.That(styles.Select(style => style.Name), Is.EqualTo(new[] { "Numb", "Stun" }));
            Assert.That(asked.Single(), Is.EqualTo((Specs.Shields, (int)eCharacterClass.Warrior)));
            Assert.That(PetKit.ShieldTeacher(eRealm.Hibernia), Is.EqualTo(eCharacterClass.Hero));
            Assert.That(PetKit.ShieldTeacher(eRealm.Albion), Is.EqualTo(eCharacterClass.Armsman));
        }

        [Test]
        public void AShieldStyle_FitsACommanderOnlyWithItsShieldUp()
        {
            var sword = new GameInventoryItem(new DOL.Database.DbItemTemplate { Object_Type = (int)eObjectType.Sword });
            var shield = new GameInventoryItem(new DOL.Database.DbItemTemplate { Object_Type = (int)eObjectType.Shield });
            Style numb = StyleOf("Numb", 1, 4, eObjectType.Shield);
            Assert.That(CommanderPet.StyleFits(numb, sword), Is.False);
            Assert.That(CommanderPet.StyleFits(numb, sword, shield), Is.True);
            Assert.That(CommanderPet.StylesMatching(new[] { numb }, sword, shield).Single().Name, Is.EqualTo("Numb"));
        }

        [Test]
        public void TheBoneMagus_ChoosesSnareDebuffOrDamage()
        {
            Assert.That(CommanderPet.SpellChoices(T.DreadLich, true, true, true), Is.EqualTo(new[] { P.Other, P.Debuff, P.Damage }));
            string line = CommanderPet.SpellPriorityLine(T.DreadLich, P.Debuff, true, true, true);
            Assert.That(line, Is.EqualTo("I cast my debuff spells first. I can favor [snare] or [damage] spells instead."));
            Assert.That(CommanderPet.ParseSpellChoice(T.DreadLich, "snare", true, true, true), Is.EqualTo(P.Other));
            Assert.That(CommanderPet.ParseSpellChoice(T.DreadLich, "damage", true, true, true), Is.EqualTo(P.Damage));
            Assert.That(CommanderPet.ParseSpellChoice(T.DreadLich, "lifedrain", true, true, true), Is.Null);
            Assert.That(CommanderPet.ParseSpellChoice(T.DreadLich, "snare", true, true, hasOther: false), Is.Null,
                "a kind it has no spell for is not offered");
        }

        [Test]
        public void TheBoneKeeper_ChoosesLifedrainOrSuppress()
        {
            Assert.That(CommanderPet.SpellChoices(T.DreadGuardian, true, true, true), Is.EqualTo(new[] { P.Other, P.Debuff }));
            Assert.That(CommanderPet.SpellPriorityLine(T.DreadGuardian, P.Other, false, true, true),
                Is.EqualTo("I cast my lifedrain spells first. I can favor [suppress] spells instead."));
            Assert.That(CommanderPet.ParseSpellChoice(T.DreadGuardian, "suppress", false, true, true), Is.EqualTo(P.Debuff));
            Assert.That(CommanderPet.ParseSpellChoice(T.DreadGuardian, "lifedrain", false, true, true), Is.EqualTo(P.Other));
        }

        [TestCase(T.DreadArcher)]
        [TestCase(T.BoneCommander)]
        public void OtherCommanders_HaveNoSpellChoice(T type) =>
            Assert.That(CommanderPet.SpellPriorityLine(type, P.None, true, true, true), Is.Null);

        [Test]
        public void SpellsSortIntoTheChoices()
        {
            // The Bone Magus template: Crawl (snare), Exacerbate Pain (debuff), Extinguish Lifeforce (lifedrain: damage).
            Assert.That(CommanderPet.KindOfSpell(T.DreadLich, eSpellType.SpeedDecrease, "Crawl"), Is.EqualTo(P.Other));
            Assert.That(CommanderPet.KindOfSpell(T.DreadLich, eSpellType.ConstitutionDebuff, "Exacerbate Pain"), Is.EqualTo(P.Debuff));
            Assert.That(CommanderPet.KindOfSpell(T.DreadLich, eSpellType.Lifedrain, "Extinguish Lifeforce"), Is.EqualTo(P.Damage));
            Assert.That(CommanderPet.KindOfSpell(T.DreadLich, eSpellType.DamageShield, "Soul Sundering Aura"), Is.EqualTo(P.None));
            // The Bone Keeper: its Pet Lifetap is a lifedrain, a snare suppresses.
            Assert.That(CommanderPet.KindOfSpell(T.DreadGuardian, eSpellType.DirectDamage, "Pet Lifetap"), Is.EqualTo(P.Other));
            Assert.That(CommanderPet.KindOfSpell(T.DreadGuardian, eSpellType.Lifedrain, "Drain"), Is.EqualTo(P.Other));
            Assert.That(CommanderPet.KindOfSpell(T.DreadGuardian, eSpellType.SpeedDecrease, "Crawl"), Is.EqualTo(P.Debuff));
        }

        [Test]
        public void TheFavoredKindGoesFirst_ThenTheRest()
        {
            Assert.That(CommanderPet.SpellOrder(P.Damage), Is.EqualTo(new[] { P.Damage, P.Debuff, P.Other }));
            Assert.That(CommanderPet.SpellOrder(P.Other), Is.EqualTo(new[] { P.Other, P.Debuff, P.Damage }));
            Assert.That(CommanderPet.SpellOrder(P.None), Is.EqualTo(new[] { P.Debuff, P.Other, P.Damage }));
        }

        [Test]
        public void NecromancerServants_FightWithTheTooltipsWeaponLine()
        {
            // Issue #117: Call Reanimated Servant says "Thrust & Shield Styles", Call Necroservant "Slash & Shield Styles".
            Assert.That(PetKit.ServantWeaponSpec("reanimated servant"), Is.EqualTo(Specs.Thrust));
            Assert.That(PetKit.ServantWeaponSpec("Necroservant"), Is.EqualTo(Specs.Slash));
            foreach (string other in new[] { "lesser zombie servant", "zombie servant", "greater necroservant", "abomination", null })
                Assert.That(PetKit.ServantWeaponSpec(other), Is.Null, other ?? "null");
            Assert.That(PetKit.ServantWeaponModel(Specs.Thrust), Is.EqualTo(PetKit.ServantThrustModel));
            Assert.That(PetKit.ServantWeaponModel(Specs.Slash), Is.EqualTo(PetKit.ServantSlashModel));
            // The servants' weapon models are not kit weapons, so taunting never swaps their hands.
            Assert.That(PetKit.WeaponOfModel(PetKit.ServantThrustModel), Is.EqualTo(W.None));
            Assert.That(PetKit.WeaponOfModel(PetKit.ServantSlashModel), Is.EqualTo(W.None));
        }

        [Test]
        public void NecromancerServantStyles_AreTheArmsmansLineAndShieldUpToTheirLevel()
        {
            var asked = new List<(string Spec, int Class)>();
            List<Style> Source(string spec, int classId)
            {
                asked.Add((spec, classId));
                return spec == Specs.Shields
                    ? new List<Style> { StyleOf("Numb", 1, 3), StyleOf("Stun", 2, 8), StyleOf("Bash", 3, 29) }
                    : new List<Style> { StyleOf("Lunge", 11, 2, eObjectType.ThrustWeapon), StyleOf("Thrust B", 12, 30, eObjectType.ThrustWeapon) };
            }

            List<Style> styles = PetKit.ServantStyles(Specs.Thrust, 11, Source);
            Assert.That(styles.Select(style => style.Name), Is.EqualTo(new[] { "Lunge", "Numb", "Stun" }));
            Assert.That(asked, Is.EquivalentTo(new[] { (Specs.Thrust, (int)eCharacterClass.Armsman), (Specs.Shields, (int)eCharacterClass.Armsman) }));
            Assert.That(PetKit.ServantStyles(null, 50, Source), Is.Empty);
        }

        [Test]
        public void TheAbomination_TowersOverTheOtherServants()
        {
            Assert.That(NecromancerPet.AbominationSize, Is.GreaterThan(50));
        }

        private static Style StyleOf(string name, int id, int level, eObjectType weapon = eObjectType.Shield)
        {
            var row = new DOL.Database.DbStyle
            {
                Name = name,
                ID = id,
                StyleID = id,
                SpecKeyName = Specs.Shields,
                SpecLevelRequirement = level,
                WeaponTypeRequirement = (int)weapon,
            };
            return new Style(row, null);
        }
    }
}

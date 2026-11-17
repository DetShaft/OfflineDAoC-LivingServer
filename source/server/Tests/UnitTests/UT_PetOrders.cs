using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;
using O = DOL.GS.PetOrders;

namespace DOL.UnitTests
{
    // Issue #35: an order dialog for every pet class other than the Bonedancer commander.
    [TestFixture]
    public class UT_PetOrders
    {
        [TestCase(eCharacterClass.Cabalist, O.Kind.Walker)]
        [TestCase(eCharacterClass.Spiritmaster, O.Kind.Walker)]
        [TestCase(eCharacterClass.Hunter, O.Kind.Walker)]
        [TestCase(eCharacterClass.Druid, O.Kind.Walker)]
        [TestCase(eCharacterClass.Enchanter, O.Kind.Walker)]
        [TestCase(eCharacterClass.Sorcerer, O.Kind.Walker)]
        [TestCase(eCharacterClass.Sluaghbinder, O.Kind.Walker)]
        [TestCase(eCharacterClass.Animist, O.Kind.Stationary)]
        [TestCase(eCharacterClass.Theurgist, O.Kind.Stationary)]
        [TestCase(eCharacterClass.Bonedancer, O.Kind.None)]
        [TestCase(eCharacterClass.Necromancer, O.Kind.TauntOnly)]
        public void EachClassGetsItsKindOfPet(eCharacterClass ownerClass, O.Kind kind) =>
            Assert.That(O.KindFor(ownerClass), Is.EqualTo(kind));

        [TestCase("aggressive", O.Order.Aggressive)]
        [TestCase(" Defensive ", O.Order.Defensive)]
        [TestCase("passive", O.Order.Passive)]
        [TestCase("assist", O.Order.Assist)]
        [TestCase("stop assisting", O.Order.StopAssist)]
        [TestCase("follow", O.Order.Follow)]
        [TestCase("stay", O.Order.Stay)]
        [TestCase("come", O.Order.ComeHere)]
        [TestCase("", O.Order.Menu)]
        [TestCase("pet", O.Order.Menu)]
        [TestCase("taunt", O.Order.Taunt)]
        [TestCase("stop taunting", O.Order.StopTaunting)]
        [TestCase("hello there", O.Order.None)]
        public void KeywordsParse(string text, O.Order order) => Assert.That(O.Parse(text), Is.EqualTo(order));

        [Test]
        public void TurretsAndElementals_TakeNoMovementOrders()
        {
            Assert.That(O.Allows(O.Kind.Stationary, O.Order.Follow), Is.False);
            Assert.That(O.Allows(O.Kind.Stationary, O.Order.Stay), Is.False);
            Assert.That(O.Allows(O.Kind.Stationary, O.Order.ComeHere), Is.False);
            Assert.That(O.Allows(O.Kind.Stationary, O.Order.Assist), Is.True);
            Assert.That(O.Allows(O.Kind.Walker, O.Order.Follow), Is.True);
            Assert.That(O.Allows(O.Kind.None, O.Order.Aggressive), Is.False);
        }

        [Test]
        public void TheMenu_OffersEveryClickableOrderForAWalker()
        {
            string menu = O.Menu(O.Kind.Walker, "Hi.", eAggressionState.Defensive, assisting: false, eWalkState.Follow);
            Assert.That(menu.Split('\n')[0], Is.EqualTo("Hi."));
            foreach (string keyword in new[] { "[assist]", "[aggressive]", "[passive]", "[stay]" })
                Assert.That(menu, Does.Contain(keyword));
            Assert.That(menu, Does.Not.Contain("[defensive]"), "the current stance is not offered again");
            foreach (string keyword in new[] { "[assist]", "[aggressive]", "[passive]", "[stay]" })
                Assert.That(O.Parse(keyword.Trim('[', ']')), Is.Not.EqualTo(O.Order.None), keyword + " must parse");
        }

        [Test]
        public void TheMenu_WhileAssisting_OffersToStop_AndAllStances()
        {
            string menu = O.Menu(O.Kind.Walker, "Hi.", eAggressionState.Defensive, assisting: true, eWalkState.Stay);
            Assert.That(menu, Does.Contain("[stop assisting]"));
            Assert.That(menu, Does.Contain("[defensive]"), "picking Defensive ends assist, so it is offered");
            Assert.That(menu, Does.Contain("[follow]"));
            Assert.That(O.Parse("stop assisting"), Is.EqualTo(O.Order.StopAssist));
        }

        [Test]
        public void AStationaryPet_HasNoMovementLine()
        {
            string menu = O.Menu(O.Kind.Stationary, "Hi.", eAggressionState.Aggressive, assisting: false, eWalkState.Stay);
            Assert.That(menu, Does.Not.Contain("[follow]").And.Not.Contain("[stay]").And.Not.Contain("[come]"));
        }

        [Test]
        public void GreetingsAreClassFlavoured()
        {
            Assert.That(O.Greeting(eCharacterClass.Hunter, false), Is.Not.EqualTo(O.Greeting(eCharacterClass.Spiritmaster, false)));
            Assert.That(O.Greeting(eCharacterClass.Hunter, charmed: true), Does.Contain("bound"));
        }

        // Step 2 (owner, 2026-11-17): per-class kits.
        [Test]
        public void SpiritmasterPets_ChooseAOneHandedWeapon_AndTaunt()
        {
            O.Kit kit = O.KitFor(eCharacterClass.Spiritmaster, "spirit warrior");
            Assert.That(kit.Weapons, Is.EqualTo(new[] { O.Weapon.Sword, O.Weapon.Axe, O.Weapon.Hammer }));
            Assert.That(kit.Taunts, Is.True);
        }

        [TestCase("underhill ally", true, true)]
        [TestCase("underhill compatriot", true, true)]
        [TestCase("underhill friend", false, true)]
        [TestCase("underhill companion", false, false)]
        [TestCase("underhill zealot", false, false)]
        public void EnchanterPets_OnlyAllyAndCompatriotTakeWeapons(string pet, bool weapons, bool taunts)
        {
            O.Kit kit = O.KitFor(eCharacterClass.Enchanter, pet);
            Assert.That(kit.Weapons.Count > 0, Is.EqualTo(weapons));
            if (weapons)
                Assert.That(kit.Weapons, Is.EqualTo(new[] { O.Weapon.Blade, O.Weapon.Mace, O.Weapon.Dagger }));
            Assert.That(kit.Taunts, Is.EqualTo(taunts));
        }

        [TestCase(eCharacterClass.Cabalist, "ruby simulacrum")]
        [TestCase(eCharacterClass.Necromancer, "abomination")]
        public void CabalistAndNecromancerPets_TauntOnly_NoWeapons(eCharacterClass owner, string pet)
        {
            O.Kit kit = O.KitFor(owner, pet);
            Assert.That(kit.Weapons, Is.Empty);
            Assert.That(kit.Taunts, Is.True);
        }

        [TestCase(eCharacterClass.Hunter)]
        [TestCase(eCharacterClass.Druid)]
        [TestCase(eCharacterClass.Theurgist)]
        [TestCase(eCharacterClass.Animist)]
        public void OtherPets_KeepThePlainDialog(eCharacterClass owner)
        {
            O.Kit kit = O.KitFor(owner, "pet");
            Assert.That(kit.Weapons, Is.Empty);
            Assert.That(kit.Taunts, Is.False);
            Assert.That(O.Allows(O.KindFor(owner), kit, O.Order.Taunt), Is.False);
        }

        [Test]
        public void TheNecromancerPet_TakesTauntAndNothingElse()
        {
            O.Kit kit = O.KitFor(eCharacterClass.Necromancer, "necroservant");
            Assert.That(O.Allows(O.Kind.TauntOnly, kit, O.Order.Taunt), Is.True);
            Assert.That(O.Allows(O.Kind.TauntOnly, kit, O.Order.StopTaunting), Is.True);
            foreach (O.Order order in new[] { O.Order.Aggressive, O.Order.Assist, O.Order.Follow, O.Order.Stay })
                Assert.That(O.Allows(O.Kind.TauntOnly, kit, order), Is.False, order.ToString());

            string menu = O.Menu(O.Kind.TauntOnly, "Hi.", eAggressionState.Defensive, false, eWalkState.Follow, kit, default);
            Assert.That(menu, Is.EqualTo("Hi.\nI know no weapon styles.\nShall I [taunt] the enemy away from you?"));

            string armed = O.Menu(O.Kind.TauntOnly, "Hi.", eAggressionState.Defensive, false, eWalkState.Follow, kit, default,
                new[] { "Cut", "Numb" });
            Assert.That(armed, Is.EqualTo("Hi.\nIn a fight I use Cut and Numb.\nShall I [taunt] the enemy away from you?"));
        }

        [Test]
        public void TheStylesLine_SpeaksForFightersOnly()
        {
            // Issue #118: Spiritmaster and Enchanter melee pets say which styles they know, or that they know none.
            O.Kit spirit = O.KitFor(eCharacterClass.Spiritmaster, "spirit warrior");
            Assert.That(O.StylesLine(spirit, new[] { "Numb", "Stun", "Disable" }), Is.EqualTo("In a fight I use Numb, Stun and Disable."));
            Assert.That(O.StylesLine(O.KitFor(eCharacterClass.Enchanter, "underhill ally"), null), Is.EqualTo("I know no weapon styles."));
            Assert.That(O.StylesLine(O.KitFor(eCharacterClass.Enchanter, "underhill compatriot"), new string[0]), Is.EqualTo("I know no weapon styles."));
            // A caster pet says nothing about styles.
            Assert.That(O.StylesLine(O.KitFor(eCharacterClass.Enchanter, "underhill zealot"), null), Is.Null);
            Assert.That(O.StylesLine(O.KitFor(eCharacterClass.Theurgist, "earth elemental"), null), Is.Null);

            string menu = O.Menu(O.Kind.Walker, "Hi.", eAggressionState.Defensive, false, eWalkState.Follow, spirit,
                new O.KitState(O.Weapon.Axe, false, false), new[] { "Numb" });
            Assert.That(menu, Does.Contain("In a fight I use Numb."));
        }

        [Test]
        public void StyleNames_AreLowestLevelFirstAndDistinct()
        {
            DOL.GS.Styles.Style Make(string name, int id, int level) => new(new DOL.Database.DbStyle
            {
                Name = name, ID = id, StyleID = id, SpecKeyName = Specs.Shields, SpecLevelRequirement = level,
            }, null);
            var names = O.StyleNames(new[] { Make("Stun", 2, 8), Make("Numb", 1, 3), null, Make("numb", 1, 3) });
            Assert.That(names, Is.EqualTo(new[] { "Numb", "Stun" }));
        }

        [TestCase("sword", O.Weapon.Sword)]
        [TestCase(" Axe ", O.Weapon.Axe)]
        [TestCase("hammer", O.Weapon.Hammer)]
        [TestCase("blade", O.Weapon.Blade)]
        [TestCase("blunt", O.Weapon.Mace)]
        [TestCase("mace", O.Weapon.Mace)]
        [TestCase("piercing", O.Weapon.Dagger)]
        [TestCase("dagger", O.Weapon.Dagger)]
        [TestCase("taunt", O.Weapon.None)]
        public void WeaponKeywordsParse(string text, O.Weapon weapon) => Assert.That(O.ParseWeapon(text), Is.EqualTo(weapon));

        [Test]
        public void TheMenu_OffersTheOtherWeapons_AndTaunt()
        {
            O.Kit kit = O.KitFor(eCharacterClass.Spiritmaster, "spirit soldier");
            string menu = O.Menu(O.Kind.Walker, "Hi.", eAggressionState.Defensive, false, eWalkState.Follow, kit,
                new O.KitState(O.Weapon.Axe, false, false));
            Assert.That(menu, Does.Contain("I hold an axe. I can take up [sword] or [hammer] instead."));
            Assert.That(menu, Does.Contain("[taunt]"));
            foreach (string keyword in new[] { "sword", "hammer" })
                Assert.That(O.ParseWeapon(keyword), Is.Not.EqualTo(O.Weapon.None));

            string taunting = O.Menu(O.Kind.Walker, "Hi.", eAggressionState.Defensive, false, eWalkState.Follow, kit,
                new O.KitState(O.Weapon.Axe, true, true));
            Assert.That(taunting, Does.Contain("behind my shield"));
            Assert.That(taunting, Does.Contain("[stop taunting]"));
            Assert.That(taunting, Does.Not.Contain("[taunt]"));
        }

        [Test]
        public void APlainPet_HasNoWeaponOrTauntLine()
        {
            string menu = O.Menu(O.Kind.Walker, "Hi.", eAggressionState.Defensive, false, eWalkState.Follow);
            Assert.That(menu, Does.Not.Contain("taunt").And.Not.Contain("I hold"));
        }
    }
}

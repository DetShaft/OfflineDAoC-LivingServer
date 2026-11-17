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
        [TestCase(eCharacterClass.Necromancer, O.Kind.None)]
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
    }
}

using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_PlayerStaticParty
    {
        [Test]
        public void ParseNames_TrimsDedupesAndCapsAtEight()
        {
            string[] names = PlayerStaticParty.ParseNames(" Ada, bob;ADA , Cara, Dan, Eve, Fay, Gus, Hal, Ivy, Joe ");
            Assert.That(names, Is.EqualTo(new[] { "Ada", "bob", "Cara", "Dan", "Eve", "Fay", "Gus", "Hal" }));
            Assert.That(names.Length, Is.EqualTo(PlayerStaticParty.MaxSlots));
        }

        [Test]
        public void ParseNames_EmptyOrWhitespace_IsEmpty()
        {
            Assert.That(PlayerStaticParty.ParseNames(null), Is.Empty);
            Assert.That(PlayerStaticParty.ParseNames("  , ; "), Is.Empty);
        }

        [Test]
        public void SerializeNames_RoundTrips()
        {
            string raw = PlayerStaticParty.SerializeNames(["Haralhelm", "Brenna", "Dowie"]);
            Assert.That(raw, Is.EqualTo("Haralhelm,Brenna,Dowie"));
            Assert.That(PlayerStaticParty.ParseNames(raw), Is.EqualTo(new[] { "Haralhelm", "Brenna", "Dowie" }));
        }

        [Test]
        public void Capacity_IsFourToEightFavoriteSlots()
        {
            Assert.That(PlayerStaticParty.MinSlots, Is.EqualTo(4));
            Assert.That(PlayerStaticParty.MaxSlots, Is.EqualTo(8));
        }
    }
}

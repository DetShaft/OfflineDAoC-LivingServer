using System;
using DOL.GS;
using NUnit.Framework;
using A = DOL.GS.RealmAltSharing.AltGuild;

namespace DOL.UnitTests
{
    // Issue #116 (owner, 2026-11-17): a player's characters on one realm share a guild and a friends list.
    [TestFixture]
    public class UT_RealmAltSharing
    {
        private static readonly DateTime Day = new(2026, 11, 17);

        [Test]
        public void AGuildlessAlt_JoinsTheMostRecentlyPlayedAltsGuild_AtItsRank()
        {
            var picked = RealmAltSharing.PickGuildSource(new[]
            {
                new A("Old", "guild-old", 9, Day.AddDays(-5), false),
                new A("Main", "guild-main", 0, Day, false),
                new A("Fresh", null, 0, Day.AddHours(1), false),
            });
            Assert.That(picked, Is.Not.Null);
            Assert.That(picked.Value.Name, Is.EqualTo("Main"));
            Assert.That(picked.Value.GuildID, Is.EqualTo("guild-main"));
            Assert.That(picked.Value.RankLevel, Is.EqualTo(0), "a guildmaster stays guildmaster on every character");
        }

        [Test]
        public void TheStarterGuild_IsNotAGuildToShare()
        {
            var picked = RealmAltSharing.PickGuildSource(new[]
            {
                new A("Newbie", "starter", 8, Day, true),
                new A("Main", "guild-main", 2, Day.AddDays(-1), false),
            });
            Assert.That(picked?.Name, Is.EqualTo("Main"));
            Assert.That(RealmAltSharing.PickGuildSource(new[] { new A("Newbie", "starter", 8, Day, true) }), Is.Null);
            Assert.That(RealmAltSharing.PickGuildSource(Array.Empty<A>()), Is.Null);
            Assert.That(RealmAltSharing.PickGuildSource(null), Is.Null);
        }

        [Test]
        public void FriendsLists_MergeOnceEach_InFirstSeenOrder()
        {
            string[] merged = RealmAltSharing.MergeFriends(new[] { "Ann", "bob" },
                new[] { new[] { "Bob", " Cara " }, null, new[] { "", "Dax", "ann" } });
            Assert.That(merged, Is.EqualTo(new[] { "Ann", "bob", "Cara", "Dax" }));
        }

        [Test]
        public void ACharacter_NeverListsItself()
        {
            Assert.That(RealmAltSharing.ListFor("Main", new[] { "Ann", "main", "Alt" }), Is.EqualTo(new[] { "Ann", "Alt" }));
            Assert.That(RealmAltSharing.Split(" Ann, ,Bob,"), Is.EqualTo(new[] { "Ann", "Bob" }));
            Assert.That(RealmAltSharing.Split(null), Is.Empty);
        }
    }
}

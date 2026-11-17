using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using A = DOL.GS.AutonomousBotAlts;

namespace DOL.UnitTests
{
    // Alts for autonomous gamebots (issue #57): one character per bot player at a time, the leveling alt most of the
    // time, a level-50 in a share of the blocks, a new alt only once every character is 50, and a shared personality.
    [TestFixture, NonParallelizable]
    public class UT_AutonomousBotAlts
    {
        [TearDown]
        public void ClearIndex() => A.Refresh([]);

        private static A.Character C(long id, int level) => new(id, level);

        [Test]
        public void OneCharacter_AlwaysPlays()
        {
            for (long block = 0; block < 50; block++)
                Assert.That(A.Playing(7, [C(7, 50)], block, 0.25), Is.EqualTo(7));
        }

        [Test]
        public void TheLevelingAlt_PlaysMostBlocks_AndTheFiftyTheShare()
        {
            A.Character[] characters = [C(10, 50), C(42, 17)];
            int fifty = 0;
            const int blocks = 4_000;
            for (long block = 0; block < blocks; block++)
            {
                long playing = A.Playing(10, characters, block, 0.25);
                Assert.That(playing, Is.EqualTo(10).Or.EqualTo(42));
                if (playing == 10)
                    fifty++;
            }
            Assert.That(fifty * 1.0 / blocks, Is.EqualTo(0.25).Within(0.03));
        }

        [Test]
        public void TheNewestCharacterBelowFifty_IsTheLevelingOne()
        {
            A.Character[] characters = [C(10, 50), C(20, 50), C(30, 12)];
            Assert.That(A.Playing(10, characters, 3, 0.0), Is.EqualTo(30));
        }

        [Test]
        public void AllFifties_TakeTurns()
        {
            A.Character[] characters = [C(10, 50), C(20, 50), C(30, 50)];
            var played = Enumerable.Range(0, 200).Select(block => A.Playing(10, characters, block, 0.25)).ToHashSet();
            Assert.That(played, Is.EquivalentTo(new long[] { 10, 20, 30 }));
        }

        [Test]
        public void ThePick_IsStableWithinABlock()
        {
            A.Character[] characters = [C(10, 50), C(42, 17)];
            var local = new DateTime(2026, 11, 16, 20, 5, 0);
            Assert.That(A.BlockOf(local), Is.EqualTo(A.BlockOf(local.AddMinutes(100))));
            Assert.That(A.BlockOf(local), Is.Not.EqualTo(A.BlockOf(local.AddHours(2))));
            long block = A.BlockOf(local);
            Assert.That(A.Playing(10, characters, block, 0.25), Is.EqualTo(A.Playing(10, characters, block, 0.25)));
        }

        [Test]
        public void ANewAlt_OnlyOnceEveryCharacterIsFifty()
        {
            Assert.That(A.WantsNewAlt([C(1, 50)], true), Is.True);
            Assert.That(A.WantsNewAlt([C(1, 49)], true), Is.False);
            Assert.That(A.WantsNewAlt([C(1, 50), C(2, 30)], true), Is.False);
            Assert.That(A.WantsNewAlt([C(1, 50), C(2, 50)], true), Is.True);
            Assert.That(A.WantsNewAlt([C(1, 50)], false), Is.False, "off");
            Assert.That(A.WantsNewAlt(Enumerable.Range(1, A.MaxCharactersPerPlayer).Select(i => C(i, 50)).ToArray(), true), Is.False,
                "a full character list");
        }

        [Test]
        public void TheIndex_GroupsAltsUnderTheirPlayer()
        {
            A.Refresh(
            [
                new OfflineWorldBotRecord { BotId = 5, Level = 50 },
                new OfflineWorldBotRecord { BotId = 9, Level = 20, PlayerId = 5 },
                new OfflineWorldBotRecord { BotId = 11, Level = 3 },
                new OfflineWorldBotRecord { BotId = 12, Level = 3, PlayerId = 5, IsRetired = true },
            ]);
            Assert.That(A.PlayerOf(9), Is.EqualTo(5));
            Assert.That(A.PlayerOf(5), Is.EqualTo(5));
            Assert.That(A.PlayerOf(11), Is.EqualTo(11));
            Assert.That(A.PlayerOf(999), Is.EqualTo(999), "not on the roster");
            Assert.That(A.CharactersOf(5).Select(c => c.BotId), Is.EqualTo(new long[] { 5, 9 }), "a retired alt is not a character");
            Assert.That(A.IsPlaying(11, DateTime.UtcNow), Is.True);
            bool five = A.IsPlaying(5, DateTime.UtcNow), nine = A.IsPlaying(9, DateTime.UtcNow);
            Assert.That(five ^ nine, Is.True, "exactly one of the player's characters plays");
        }

        [Test]
        public void AnotherCharacterOnline_HoldsTheOtherBack()
        {
            A.Refresh(
            [
                new OfflineWorldBotRecord { BotId = 5, Level = 50 },
                new OfflineWorldBotRecord { BotId = 9, Level = 20, PlayerId = 5 },
            ]);
            var online = new HashSet<long> { 5 };
            Assert.That(A.AnotherCharacterOnline(9, online.Contains), Is.True);
            Assert.That(A.AnotherCharacterOnline(5, online.Contains), Is.False);
        }

        [Test]
        public void Alts_ShareTheirPlayersPersonality()
        {
            A.Refresh(
            [
                new OfflineWorldBotRecord { BotId = 5, Level = 50 },
                new OfflineWorldBotRecord { BotId = 9, Level = 20, PlayerId = 5 },
            ]);
            Assert.That(AutonomousBotPersonality.TypeFor(9), Is.EqualTo(AutonomousBotPersonality.TypeFor(5)));
            Assert.That(AutonomousBotPersonality.For(9), Is.EqualTo(AutonomousBotPersonality.For(5)));
        }

        [Test]
        public void ANewAlt_IsAFreshLevelOneRecordOfThePlayer()
        {
            var first = new OfflineWorldBotRecord { BotId = 5, Name = "Aldric", Realm = (int)eRealm.Albion, Level = 50 };
            OfflineWorldBotRecord alt = A.NewAlt(first, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Aldric" }, new Random(3));
            Assert.That(alt.PlayerId, Is.EqualTo(5));
            Assert.That(alt.Realm, Is.EqualTo((int)eRealm.Albion));
            Assert.That(alt.Name, Is.Not.EqualTo("Aldric").And.Not.Empty);
            Assert.That(AutonomousPopulationController.IsFreshUnplacedLevelOneRecord(alt), Is.True,
                "placed at a valid start on login, like any new gamebot");
            Assert.That(alt.MoneyCopper, Is.EqualTo(0));
        }
    }
}

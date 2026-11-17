using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #48 (Jay, 2026-11-16): in a fight a healer takes a spot out of danger within casting range of the group and
    // holds it, instead of running around behind the leader at a fixed offset.
    [TestFixture]
    public class UT_HealerPost
    {
        private static readonly Vector2[] NoEnemies = [];

        [Test]
        public void AGoodSpot_IsKept_TheHealerDoesNotMove()
        {
            Vector2 self = new(0, -700);
            Vector2[] members = [new(0, 0), new(80, 40)];
            Vector2[] enemies = [new(0, 150)];
            Assert.That(HealerPost.Pick(self, members, enemies, tight: false), Is.EqualTo(self));
        }

        [Test]
        public void AnEnemyOnTheHealer_MovesItOutOfDanger_StillInRangeOfEveryone()
        {
            Vector2 self = new(0, 100);
            Vector2[] members = [new(0, 0), new(-60, 30)];
            Vector2[] enemies = [new(0, 150)];
            Vector2? post = HealerPost.Pick(self, members, enemies, tight: false);
            Assert.That(post, Is.Not.Null);
            Assert.That(HealerPost.IsGood(post.Value, members, enemies, false), Is.True);
            foreach (Vector2 enemy in enemies)
                Assert.That(Vector2.Distance(post.Value, enemy), Is.GreaterThanOrEqualTo(HealerPost.OpenStandoff));
            foreach (Vector2 member in members)
                Assert.That(Vector2.Distance(post.Value, member), Is.LessThanOrEqualTo(HealerPost.OpenReach));
        }

        [Test]
        public void ThePost_IsTheNearestGoodPoint_OnTheHealersOwnSide()
        {
            Vector2 self = new(300, 0);
            Vector2[] members = [new(0, 0)];
            Vector2[] enemies = [new(0, 0)];
            Vector2? post = HealerPost.Pick(self, members, enemies, tight: false);
            Assert.That(post, Is.Not.Null);
            Assert.That(post.Value.X, Is.GreaterThan(0), "steps back away from the fight, not across it");
            Assert.That(Vector2.Distance(self, post.Value), Is.LessThan(400));
        }

        [Test]
        public void AGroupSpreadWiderThanOneHealerCanCover_GivesNoPost()
        {
            Vector2[] members = [new(-2000, 0), new(2000, 0)];
            Assert.That(HealerPost.Pick(new Vector2(0, 0), members, NoEnemies, tight: false), Is.Null);
        }

        [Test]
        public void NoOneToCover_GivesNoPost() =>
            Assert.That(HealerPost.Pick(new Vector2(0, 0), [], NoEnemies, tight: false), Is.Null);

        [Test]
        public void AHeldPost_SurvivesSmallDrift_ButNotAnEnemyOnIt()
        {
            Vector2 post = new(0, -600);
            Vector2[] members = [new(0, 0)];
            Assert.That(HealerPost.Keep(post, members, [new Vector2(0, -250)], false), Is.True,
                "an enemy at 350, a little inside the standoff, does not move the healer yet");
            Assert.That(HealerPost.Keep(post, members, [new Vector2(0, -450)], false), Is.False,
                "an enemy at 150 does");
            Assert.That(HealerPost.Keep(post, [new Vector2(0, 750)], NoEnemies, false), Is.True,
                "a member at 1350, a little past reach, does not either");
            Assert.That(HealerPost.Keep(post, [new Vector2(0, 1000)], NoEnemies, false), Is.False);
        }

        [Test]
        public void Dungeons_UseTighterDistances()
        {
            Assert.That(HealerPost.Standoff(true), Is.LessThan(HealerPost.Standoff(false)));
            Assert.That(HealerPost.Reach(true), Is.LessThan(HealerPost.Reach(false)));
            Vector2 self = new(0, 50);
            Vector2[] members = [new(0, 0)];
            Vector2[] enemies = [new(0, 100)];
            Vector2? post = HealerPost.Pick(self, members, enemies, tight: true);
            Assert.That(post, Is.Not.Null);
            Assert.That(Vector2.Distance(post.Value, members[0]), Is.LessThanOrEqualTo(HealerPost.TightReach));
        }

        [Test]
        public void BackLineClasses()
        {
            foreach (eCharacterClass backLine in new[] { eCharacterClass.Cleric, eCharacterClass.Druid, eCharacterClass.Healer,
                         eCharacterClass.Shaman, eCharacterClass.Bard })
                Assert.That(BotPartyRoles.HoldsBackLine(backLine), Is.True, backLine.ToString());
            foreach (eCharacterClass melee in new[] { eCharacterClass.Friar, eCharacterClass.Warden, eCharacterClass.Paladin,
                         eCharacterClass.Skald, eCharacterClass.Minstrel })
                Assert.That(BotPartyRoles.HoldsBackLine(melee), Is.False, melee.ToString());
            Assert.That(BotPartyRoles.AvoidsSupportMelee(eCharacterClass.Shaman), Is.True);
            Assert.That(BotPartyRoles.AvoidsSupportMelee(eCharacterClass.Bard), Is.True);
            Assert.That(BotPartyRoles.AvoidsSupportMelee(eCharacterClass.Cleric), Is.False);
        }
    }
}

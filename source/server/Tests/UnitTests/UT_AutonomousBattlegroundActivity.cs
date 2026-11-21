using System;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Activity = DOL.GS.AutonomousBattlegroundActivity.Activity;
using Tier = DOL.GS.AutonomousBattlegroundActivity.Tier;

namespace DOL.UnitTests
{
    /// <summary>Battleground tour phases (owner, 2026-11-21): grind, keep fights and outskirt hunt by level tier.</summary>
    [TestFixture]
    public class UT_AutonomousBattlegroundActivity
    {
        [TestCase(15, Tier.Low)] [TestCase(16, Tier.Low)] [TestCase(17, Tier.Mid)] [TestCase(18, Tier.High)] [TestCase(19, Tier.High)]
        public void KillaloeTiers(int level, Tier tier) =>
            Assert.That(AutonomousBattlegroundActivity.TierOf(level, 15, 19), Is.EqualTo(tier));

        [TestCase(30, Tier.Low)] [TestCase(31, Tier.Low)] [TestCase(32, Tier.Mid)] [TestCase(33, Tier.High)] [TestCase(35, Tier.High)]
        public void CaledoniaTiers(int level, Tier tier) =>
            Assert.That(AutonomousBattlegroundActivity.TierOf(level, 30, 35), Is.EqualTo(tier));

        [TestCase(Tier.Low)] [TestCase(Tier.Mid)] [TestCase(Tier.High)]
        public void SharesAddUpAndPhasesFollowThem(Tier tier)
        {
            var (grind, keep, hunt) = AutonomousBattlegroundActivity.Shares(tier);
            Assert.That(grind + keep + hunt, Is.EqualTo(100));
            int samples = 20_000;
            var picks = Enumerable.Range(0, samples).Select(i => AutonomousBattlegroundActivity.Choose(tier, 1_000 + i % 400, i / 400)).ToArray();
            Assert.That(picks.Count(a => a == Activity.Grind) * 100.0 / samples, Is.EqualTo(grind).Within(3));
            Assert.That(picks.Count(a => a == Activity.KeepFights) * 100.0 / samples, Is.EqualTo(keep).Within(3));
            Assert.That(picks.Count(a => a == Activity.OutskirtHunt) * 100.0 / samples, Is.EqualTo(hunt).Within(3));
        }

        [Test]
        public void LowLevelsGrindMoreThanHighLevels()
        {
            Assert.That(AutonomousBattlegroundActivity.Shares(Tier.Low).Grind, Is.GreaterThan(AutonomousBattlegroundActivity.Shares(Tier.Mid).Grind));
            Assert.That(AutonomousBattlegroundActivity.Shares(Tier.Mid).Grind, Is.GreaterThan(AutonomousBattlegroundActivity.Shares(Tier.High).Grind));
            var high = AutonomousBattlegroundActivity.Shares(Tier.High);
            Assert.That(high.KeepFights + high.Hunt, Is.GreaterThanOrEqualTo(80));
        }

        [Test]
        public void APhaseLastsTenToSixteenMinutesAndChanges()
        {
            const long key = 4242;
            long start = AutonomousBattlegroundActivity.PhaseIndex(0, key);
            long changeAt = Enumerable.Range(1, 20 * 60).Select(s => s * 1_000L)
                .First(ms => AutonomousBattlegroundActivity.PhaseIndex(ms, key) != start);
            long nextChange = Enumerable.Range(1, 20 * 60).Select(s => changeAt + s * 1_000L)
                .First(ms => AutonomousBattlegroundActivity.PhaseIndex(ms, key) != start + 1);
            Assert.That(nextChange - changeAt, Is.InRange(AutonomousBattlegroundActivity.PhaseMinMilliseconds,
                AutonomousBattlegroundActivity.PhaseMinMilliseconds + AutonomousBattlegroundActivity.PhaseSpreadMilliseconds));
        }

        [Test]
        public void EngageRadiusIsShorterForGrindersAndKeepFighters()
        {
            Assert.That(AutonomousBattlegroundActivity.EngageRadius(Activity.Grind), Is.LessThan(AutonomousBattlegroundActivity.EngageRadius(Activity.KeepFights)));
            Assert.That(AutonomousBattlegroundActivity.EngageRadius(Activity.KeepFights), Is.LessThan(AutonomousBattlegroundActivity.EngageRadius(Activity.OutskirtHunt)));
            Assert.That(AutonomousBattlegroundActivity.EngageRadius(Activity.OutskirtHunt), Is.EqualTo(5_000), "hunters keep the full scan");
        }

        [TestCase(1, 1, false)] [TestCase(2, 1, false)] [TestCase(3, 1, true)] [TestCase(3, 2, false)] [TestCase(4, 2, true)] [TestCase(5, 4, false)]
        public void HuntersAvoidOutnumberingGroups(int enemies, int friends, bool avoids) =>
            Assert.That(AutonomousBattlegroundActivity.HunterAvoids(enemies, friends), Is.EqualTo(avoids));

        [TestCase(3, 2, 1, 3.0, true)]    // solo hunter, three levels over two busy campers of three
        [TestCase(3, 2, 1, 2.0, false)]   // not enough edge alone
        [TestCase(3, 2, 2, 0.0, true)]    // a hunting pair
        [TestCase(3, 1, 2, 0.0, false)]   // the camp is not busy
        [TestCase(4, 4, 2, 4.0, false)]   // four is a group, not a camp of three
        public void HuntersJumpACampOfThreeOnlyWhenItIsGoodAction(int enemies, int busy, int friends, double edge, bool favorable)
        {
            Assert.That(AutonomousBattlegroundActivity.FavorableCamp(enemies, busy, friends, edge), Is.EqualTo(favorable));
            Assert.That(AutonomousBattlegroundActivity.HunterAvoids(enemies, friends, favorable), Is.EqualTo(!favorable && enemies > friends + 1));
        }

        [Test]
        public void OwnKeepSiegeIsDefendedAndThirdRealmSiegesDrawHalfTheHunters()
        {
            Assert.That(AutonomousBattlegroundActivity.RespondToSiege(Activity.KeepFights, true, 9_000, 1), Is.EqualTo(AutonomousBattlegroundActivity.SiegeResponse.Defend));
            Assert.That(AutonomousBattlegroundActivity.RespondToSiege(Activity.OutskirtHunt, true, 15_000, 1), Is.EqualTo(AutonomousBattlegroundActivity.SiegeResponse.Defend));
            Assert.That(AutonomousBattlegroundActivity.RespondToSiege(Activity.Grind, true, 5_000, 1), Is.EqualTo(AutonomousBattlegroundActivity.SiegeResponse.Defend));
            Assert.That(AutonomousBattlegroundActivity.RespondToSiege(Activity.Grind, true, 9_000, 1), Is.EqualTo(AutonomousBattlegroundActivity.SiegeResponse.None));
            Assert.That(AutonomousBattlegroundActivity.RespondToSiege(Activity.Grind, false, 1_000, 1), Is.EqualTo(AutonomousBattlegroundActivity.SiegeResponse.None));
            Assert.That(AutonomousBattlegroundActivity.RespondToSiege(Activity.KeepFights, false, 1_000, 1), Is.EqualTo(AutonomousBattlegroundActivity.SiegeResponse.None),
                "keep fighters already assault another realm's keep with their wave");
            int thirdParty = Enumerable.Range(0, 1_000).Count(key => AutonomousBattlegroundActivity.RespondToSiege(Activity.OutskirtHunt, false, 8_000, key) ==
                AutonomousBattlegroundActivity.SiegeResponse.ThirdParty);
            Assert.That(thirdParty, Is.InRange(420, 580));
            Assert.That(AutonomousBattlegroundActivity.SiegeActive(0, 10_000), Is.False);
            Assert.That(AutonomousBattlegroundActivity.SiegeActive(5_000, 5_000 + AutonomousBattlegroundActivity.SiegeActiveMilliseconds), Is.True);
            Assert.That(AutonomousBattlegroundActivity.SiegeActive(5_000, 5_001 + AutonomousBattlegroundActivity.SiegeActiveMilliseconds), Is.False);
        }

        [Test]
        public void PartiesFormFromNearbyBotsWithinTheActivitysSizes()
        {
            var together = Enumerable.Range(0, 10).Select(i => new Vector2(i * 100, 0)).ToArray();
            var keep = AutonomousBattlegroundActivity.FormParties(together, Activity.KeepFights);
            Assert.That(keep.Select(p => p.Length), Is.EqualTo(new[] { 8 }), "eight, and the last two are too few for a keep party");
            var hunt = AutonomousBattlegroundActivity.FormParties(together, Activity.OutskirtHunt);
            Assert.That(hunt.All(p => p.Length is 2 or 3), Is.True);
            Assert.That(hunt.Sum(p => p.Length), Is.EqualTo(9), "the tenth hunts alone");
            var apart = new[] { new Vector2(0, 0), new Vector2(10_000, 0), new Vector2(20_000, 0) };
            Assert.That(AutonomousBattlegroundActivity.FormParties(apart, Activity.OutskirtHunt), Is.Empty, "nobody near enough");
            Assert.That(keep[0][0], Is.EqualTo(0), "the first bot leads");
        }

        [Test]
        public void HuntersPreferCampersAndLowerLevels()
        {
            double camper = AutonomousBattlegroundActivity.HuntScore(3_000, 19, 16, true, 1);
            double nearSameLevel = AutonomousBattlegroundActivity.HuntScore(1_500, 19, 19, false, 1);
            double nearPair = AutonomousBattlegroundActivity.HuntScore(1_000, 19, 19, false, 2);
            Assert.That(camper, Is.LessThan(nearSameLevel));
            Assert.That(nearSameLevel, Is.LessThan(nearPair), "a lone enemy before a pair a little nearer");
        }

        [Test]
        public void GrindersJoinRealmMatesBeforeOpeningANewCamp()
        {
            Assert.That(AutonomousBattlegroundActivity.PickGrindCamp(new[] { 0, 2, 0, 4 }, 0.9), Is.EqualTo(1), "the camp with realm-mates");
            Assert.That(AutonomousBattlegroundActivity.PickGrindCamp(new[] { 0, 4, 0 }, 0.9), Is.EqualTo(2), "full camps are skipped");
            Assert.That(AutonomousBattlegroundActivity.PickGrindCamp(new[] { 4, 4 }, 0.5), Is.EqualTo(-1));
        }

        [Test]
        public void HuntPointsStayOffEnemyPortalsAndOutOfTheMiddle()
        {
            var portals = new[] { new Vector2(0, -20_000), new Vector2(17_320, 10_000) };
            var camps = new[] { new Vector2(0, -18_500), new Vector2(0, 2_000), new Vector2(-9_000, 6_000) };
            var random = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                Vector2 point = AutonomousBattlegroundActivity.HuntPoint(Vector2.Zero, portals, camps, 20_000, null, random);
                Assert.That(portals.Min(p => Vector2.Distance(p, point)), Is.GreaterThanOrEqualTo(AutonomousBattlegroundActivity.HuntPortalClearance));
                Assert.That(point.Length(), Is.GreaterThanOrEqualTo(20_000 * AutonomousBattlegroundActivity.OutskirtShare - 1));
            }
        }

        [Test]
        public void KeepLurkPointsRingTheKeep()
        {
            var keep = new Vector2(30_000, 30_000);
            for (int i = 0; i <= 10; i++)
            {
                float d = Vector2.Distance(keep, AutonomousBattlegroundActivity.KeepLurkPoint(keep, i / 10.0, i / 10.0));
                Assert.That(d, Is.InRange(AutonomousBattlegroundActivity.KeepLurkMin - 1, AutonomousBattlegroundActivity.KeepLurkMax + 1));
            }
        }

        [Test]
        public void GrindCampHoldsExpire()
        {
            AutonomousBattlegroundActivity.HoldCamp(91, 253, eRealm.Albion, "camp-a", 0);
            AutonomousBattlegroundActivity.HoldCamp(92, 253, eRealm.Albion, "camp-a", 0);
            AutonomousBattlegroundActivity.HoldCamp(93, 253, eRealm.Midgard, "camp-a", 0);
            Assert.That(AutonomousBattlegroundActivity.GrindersAt(253, eRealm.Albion, "camp-a", 91, 1_000), Is.EqualTo(1));
            Assert.That(AutonomousBattlegroundActivity.GrindersAt(253, eRealm.Albion, "camp-a", 0, AutonomousBattlegroundActivity.GrindHoldMilliseconds), Is.EqualTo(0));
            foreach (long key in new long[] { 91, 92, 93 }) AutonomousBattlegroundActivity.Forget(key);
            Assert.That(AutonomousBattlegroundActivity.JoinsKeepFights(91), Is.True, "a bot with no phase counts toward the wave");
            AutonomousBattlegroundActivity.Report(91, Activity.Grind);
            Assert.That(AutonomousBattlegroundActivity.JoinsKeepFights(91), Is.False);
            AutonomousBattlegroundActivity.Forget(91);
        }
    }
}

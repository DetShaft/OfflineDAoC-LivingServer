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

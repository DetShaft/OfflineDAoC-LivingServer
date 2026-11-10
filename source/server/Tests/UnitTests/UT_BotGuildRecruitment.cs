using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotGuildRecruitment
    {
        [Test]
        public void PolicyFor_LeansOpenOnSocialAndLeveling()
        {
            Assert.That(AutonomousBotGuildRecruitment.PolicyFor(BotGuildCharter.Social, 1), Is.EqualTo(BotGuildRecruitmentPolicy.Open));
            Assert.That(AutonomousBotGuildRecruitment.PolicyFor(BotGuildCharter.Leveling, 1), Is.EqualTo(BotGuildRecruitmentPolicy.Open));
            Assert.That(AutonomousBotGuildRecruitment.PolicyFor(BotGuildCharter.Crafting, 0), Is.EqualTo(BotGuildRecruitmentPolicy.Friendly));
        }

        [Test]
        public void PolicyFor_RvrAndRaiding_AreExclusive()
        {
            Assert.That(AutonomousBotGuildRecruitment.PolicyFor(BotGuildCharter.Rvr, 0), Is.EqualTo(BotGuildRecruitmentPolicy.Closed));
            Assert.That(AutonomousBotGuildRecruitment.PolicyFor(BotGuildCharter.Rvr, 1), Is.EqualTo(BotGuildRecruitmentPolicy.Application));
            Assert.That(AutonomousBotGuildRecruitment.PolicyFor(BotGuildCharter.Raiding, 0), Is.EqualTo(BotGuildRecruitmentPolicy.Closed));
            Assert.That(AutonomousBotGuildRecruitment.PolicyFor(BotGuildCharter.Raiding, 1), Is.EqualTo(BotGuildRecruitmentPolicy.Application));
        }

        [Test]
        public void StandingScore_WeightsRezzesAndInsults()
        {
            Assert.That(AutonomousBotGuildRecruitment.StandingScore(2, 1, 0), Is.EqualTo(4));
            Assert.That(AutonomousBotGuildRecruitment.StandingScore(1, 0, 1), Is.EqualTo(0));
            Assert.That(AutonomousBotGuildRecruitment.StandingScore(0, 0, 3), Is.EqualTo(0));
        }

        [TestCase(BotGuildRecruitmentPolicy.Open, 0, "neutral", 0, 0, true)]
        [TestCase(BotGuildRecruitmentPolicy.Friendly, 0, "neutral", 0, 0, false)]
        [TestCase(BotGuildRecruitmentPolicy.Friendly, 1, "neutral", 0, 0, true)]
        [TestCase(BotGuildRecruitmentPolicy.Friendly, 0, "friendly", 0, 0, true)]
        [TestCase(BotGuildRecruitmentPolicy.Friendly, 0, "neutral", 1, 0, true)]
        [TestCase(BotGuildRecruitmentPolicy.Application, 1, "friendly", 1, 0, false)]
        [TestCase(BotGuildRecruitmentPolicy.Application, 2, "neutral", 0, 0, true)]
        [TestCase(BotGuildRecruitmentPolicy.Application, 0, "neutral", 0, 1, true)]
        [TestCase(BotGuildRecruitmentPolicy.Closed, 3, "neutral", 0, 0, false)]
        [TestCase(BotGuildRecruitmentPolicy.Closed, 3, "friendly", 0, 0, true)]
        [TestCase(BotGuildRecruitmentPolicy.Closed, 2, "friendly", 0, 0, false)]
        public void AllowsInvite_MatchesPolicyThresholds(BotGuildRecruitmentPolicy policy, int standing,
            string mood, int friendly, int vouchers, bool expected)
        {
            Assert.That(AutonomousBotGuildRecruitment.AllowsInvite(policy, standing, mood, friendly, vouchers),
                Is.EqualTo(expected));
        }

        [Test]
        public void MayAdvertiseInFaction_OnlyOpenAndApplication()
        {
            Assert.That(AutonomousBotGuildRecruitment.MayAdvertiseInFaction(BotGuildRecruitmentPolicy.Open), Is.True);
            Assert.That(AutonomousBotGuildRecruitment.MayAdvertiseInFaction(BotGuildRecruitmentPolicy.Application), Is.True);
            Assert.That(AutonomousBotGuildRecruitment.MayAdvertiseInFaction(BotGuildRecruitmentPolicy.Friendly), Is.False);
            Assert.That(AutonomousBotGuildRecruitment.MayAdvertiseInFaction(BotGuildRecruitmentPolicy.Closed), Is.False);
        }
    }
}

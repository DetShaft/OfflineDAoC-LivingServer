using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// A bot in a real player's group runs none of its own goals, so its death line must say what it did in the group,
    /// not the last solo activity it stored (2026-11-19 Jarl Killaloe deaths read "Leaving the battleground").
    /// </summary>
    [TestFixture]
    public class UT_PlayerGroupDeathActivity
    {
        [Test]
        public void NamesTheFightAndTheLeader()
        {
            Assert.That(AutonomousDeathDiagnostics.PlayerGroupActivity("Ferac", "Jarl Killaloe"),
                Is.EqualTo("Fighting Jarl Killaloe in Ferac's group"));
        }

        [Test]
        public void WithoutATargetSaysItIsInTheGroup()
        {
            Assert.That(AutonomousDeathDiagnostics.PlayerGroupActivity("Ferac", null), Is.EqualTo("In Ferac's group"));
            Assert.That(AutonomousDeathDiagnostics.PlayerGroupActivity("Ferac", ""), Is.EqualTo("In Ferac's group"));
        }
    }
}

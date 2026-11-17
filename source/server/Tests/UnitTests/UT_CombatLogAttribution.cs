using DOL.GS;
using NUnit.Framework;
using static DOL.GS.AttackData;

namespace DOL.UnitTests
{
    /// <summary>A bot's combat lines name the bot on its owner's log, in others' combat (owner 2026-11-16).</summary>
    [TestFixture]
    public class UT_CombatLogAttribution
    {
        [TestCase("You hit the goblin for 50 damage!", "Aldwin hits the goblin for 50 damage!")]
        [TestCase("You critically hit for an additional 12 damage! (10%)", "Aldwin critically hits for an additional 12 damage! (10%)")]
        [TestCase("Your Aldwin hits the goblin for 40 (-3) damage!", "Aldwin hits the goblin for 40 (-3) damage!")]
        [TestCase("Your Searing Bolt hits the goblin for 30 damage!", "Aldwin's Searing Bolt hits the goblin for 30 damage!")]
        [TestCase("You heal Morgan for 120 hit points!", "Aldwin heals Morgan for 120 hit points!")]
        [TestCase("You heal yourself for 80 hit points.", "Aldwin heals himself for 80 hit points.")]
        [TestCase("You are fully healed.", "Aldwin is fully healed.")]
        [TestCase("You lose your concentration!", "Aldwin loses his concentration!")]
        [TestCase("You steal 20 hit points from the goblin!", "Aldwin steals 20 hit points from the goblin!")]
        [TestCase("You cast a spell on yourself.", "Aldwin casts a spell on himself.")]
        [TestCase("You already have that effect.", "Aldwin already has that effect.")]
        [TestCase("The goblin resists the effect!", "The goblin resists the effect! (Aldwin)")]
        [TestCase("Aldwin is fully healed.", "Aldwin is fully healed.")]
        public void FirstPersonLinesNameTheBot(string message, string expected) =>
            Assert.That(CombatLogAttribution.ThirdPerson(message, "Aldwin", "his", "him"), Is.EqualTo(expected));

        [Test]
        public void FemaleBotsGetTheirOwnPronouns() =>
            Assert.That(CombatLogAttribution.ThirdPerson("You heal yourself and your group.", "Brena", "her", "her"),
                Is.EqualTo("Brena heals herself and her group."));

        [TestCase("hit", "hits")]
        [TestCase("miss", "misses")]
        [TestCase("parry", "parries")]
        [TestCase("play", "plays")]
        [TestCase("are", "is")]
        [TestCase("do", "does")]
        [TestCase("can't", "can't")]
        public void Conjugates(string verb, string expected) =>
            Assert.That(CombatLogAttribution.Conjugate(verb), Is.EqualTo(expected));

        [Test]
        public void StyledHitNamesTheBotNotYou()
        {
            var lines = CombatLogAttribution.AttackerLines("Aldwin", "the goblin", eAttackResult.HitStyle, eAttackType.MeleeOneHand,
                85, -4, 10, 7, "Doublefrost", 30, 0.4, "Aldwin attacks the goblin with his sword!", 0);

            Assert.That(lines, Is.EqualTo(new[]
            {
                "Aldwin performs Doublefrost perfectly! (+30, GR: 0.4)",
                "Aldwin attacks the goblin and hits for 85 (-4) damage!",
                "Aldwin critically hits the goblin for an additional 10 damage! (7%)",
            }));
        }

        [Test]
        public void RangedMissUsesTheBroadcastLine()
        {
            Assert.That(CombatLogAttribution.AttackerLines("Aldwin", "the goblin", eAttackResult.HitUnstyled, eAttackType.Ranged,
                20, 0, 0, 0, null, 0, 0, null, 0), Is.EqualTo(new[] { "Aldwin shoots the goblin and hits for 20 damage!" }));
            Assert.That(CombatLogAttribution.AttackerLines("Aldwin", "the goblin", eAttackResult.Missed, eAttackType.Ranged,
                0, 0, 0, 0, null, 0, 0, "Aldwin attacks the goblin and misses!", 12.5),
                Is.EqualTo(new[] { "Aldwin attacks the goblin and misses! (12.5%)" }));
        }

        [Test]
        public void AttacksOnTheBotDoNotSayYour()
        {
            Assert.That(CombatLogAttribution.TargetLines("The goblin", "Aldwin", eAttackResult.Parried, eAttackType.MeleeOneHand, 0, 0, 0),
                Is.EqualTo(new[] { "The goblin attacks Aldwin and is parried!" }));
            Assert.That(CombatLogAttribution.TargetLines("The goblin", "Aldwin", eAttackResult.HitUnstyled, eAttackType.MeleeOneHand, 33, 2, 5),
                Is.EqualTo(new[] { "The goblin hits Aldwin for 33 (+2) damage.", "The goblin critically hits Aldwin for an additional 5 damage." }));
            Assert.That(CombatLogAttribution.TargetLines("The goblin", "Aldwin", eAttackResult.Missed, eAttackType.Spell, 0, 0, 0), Is.Empty);
        }

        [Test]
        public void ByDefaultOnlyTheLiveAreaLineShows()
        {
            Assert.That(CombatLogAttribution.AttackerLines("Aldwin", "the goblin", eAttackResult.HitStyle, eAttackType.MeleeOneHand,
                85, -4, 10, 7, "Doublefrost", 30, 0.4, "Aldwin attacks the goblin with his sword!", 0, details: false),
                Is.EqualTo(new[] { "Aldwin attacks the goblin with his sword!" }));
            Assert.That(CombatLogAttribution.AttackerLines("Aldwin", "the goblin", eAttackResult.Missed, eAttackType.MeleeOneHand,
                0, 0, 0, 0, null, 0, 0, "Aldwin attacks the goblin and misses!", 12.5, details: false),
                Is.EqualTo(new[] { "Aldwin attacks the goblin and misses!" }));
            Assert.That(CombatLogAttribution.TargetLines("The goblin", "Aldwin", eAttackResult.HitUnstyled, eAttackType.MeleeOneHand,
                33, 2, 5, "The goblin attacks Aldwin and hits!", details: false), Is.EqualTo(new[] { "The goblin attacks Aldwin and hits!" }));
            Assert.That(CombatLogAttribution.TargetLines("The goblin", "Aldwin", eAttackResult.HitUnstyled, eAttackType.Spell,
                40, 0, 0, null, details: false), Is.Empty);
        }

        [Test]
        public void DetailsAreOffByDefault() =>
            Assert.That(DOL.GS.ServerProperties.Properties.BOT_COMBAT_LOG_DETAILS, Is.False);

        [Test]
        public void ChannelIsOthersCombat() =>
            Assert.That(CombatLogAttribution.Channel, Is.EqualTo(DOL.GS.PacketHandler.eChatType.CT_OthersCombat));
    }
}

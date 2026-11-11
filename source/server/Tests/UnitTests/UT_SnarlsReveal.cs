using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Snarls (East Svealand) hides until three of its wolves die. Jay met it revealed but untargetable while it killed
    /// his two bots (2026-11-11). These cover the server half: the flags it hides and shows with, and the rule that a
    /// change of those flags makes every client build the NPC again.
    /// </summary>
    [TestFixture]
    public class UT_SnarlsReveal
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const ushort WolfModel = 450;

        // Not in a world (inactive), so flag and model changes send nothing to clients.
        private static GameNPC Body(GameNPC.eFlags flags)
        {
            GameNPC npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            typeof(GameNPC).GetField("m_flags", Hidden).SetValue(npc, flags);
            typeof(GameObject).GetField("m_model", Hidden).SetValue(npc, WolfModel);
            npc.ObjectState = GameObject.eObjectState.Inactive; // (0 would be Active)
            return npc;
        }

        [Test]
        public void HiddenSnarlsCannotBeTargetedOrFightAndRevealedSnarlsCan()
        {
            GameNPC body = Body(0);
            var brain = new SnarlsBrain { Body = body };

            brain.Hide();
            Assert.That(brain.IsHidden, Is.True);
            Assert.That(body.Flags & SnarlsBrain.HiddenFlags, Is.EqualTo(SnarlsBrain.HiddenFlags));
            Assert.That(body.Model, Is.EqualTo((ushort)1));

            brain.Reveal();
            Assert.That(brain.IsHidden, Is.False);
            Assert.That(body.Flags & SnarlsBrain.HiddenFlags, Is.EqualTo((GameNPC.eFlags)0), "targetable, named and able to be fought");
            Assert.That(body.Model, Is.EqualTo(WolfModel));
        }

        [Test]
        public void HidingSetsTheFlagsInsteadOfTogglingThem()
        {
            // The old script toggled (^=): a template that already had PEACE came out hidden but able to fight.
            GameNPC body = Body(GameNPC.eFlags.PEACE);
            var brain = new SnarlsBrain { Body = body };

            brain.Hide();
            Assert.That(body.Flags & SnarlsBrain.HiddenFlags, Is.EqualTo(SnarlsBrain.HiddenFlags));

            brain.Reveal();
            Assert.That(body.Flags & GameNPC.eFlags.CANTTARGET, Is.EqualTo((GameNPC.eFlags)0));
        }

        [Test]
        public void HideAndRevealAreSafeToRepeat()
        {
            GameNPC body = Body(GameNPC.eFlags.TORCH);
            var brain = new SnarlsBrain { Body = body };

            brain.Reveal();
            Assert.That(body.Flags, Is.EqualTo(GameNPC.eFlags.TORCH), "revealing a Snarls that was never hidden changes nothing");
            brain.Hide();
            brain.Hide();
            brain.Reveal();
            Assert.That(body.Flags, Is.EqualTo(GameNPC.eFlags.TORCH));
            Assert.That(body.Model, Is.EqualTo(WolfModel));
        }

        [Test]
        public void TargetingFlagChangesRebuildTheNpcOnTheClient()
        {
            GameNPC.eFlags plain = GameNPC.eFlags.TORCH;
            Assert.That(GameNPC.NeedsClientRecreate(plain | SnarlsBrain.HiddenFlags, plain), Is.True, "reveal");
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain | SnarlsBrain.HiddenFlags), Is.True, "hide");
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain | GameNPC.eFlags.CANTTARGET), Is.True);
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain | GameNPC.eFlags.DONTSHOWNAME), Is.True);
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain | GameNPC.eFlags.PEACE), Is.True);
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain | GameNPC.eFlags.GHOST), Is.True);

            // Only the four create-time flags count: stealth, flying, torch and swimming keep the plain re-send.
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain | GameNPC.eFlags.STEALTH), Is.False);
            Assert.That(GameNPC.NeedsClientRecreate(plain, GameNPC.eFlags.FLYING), Is.False);
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain | GameNPC.eFlags.SWIMMING), Is.False);
            Assert.That(GameNPC.NeedsClientRecreate(plain, plain), Is.False);
        }
    }
}

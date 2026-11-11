using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// The pet window's ASSIST mode (Beta 1.1.12): the player's main pet attacks the player's target as
/// soon as the player engages. These cover who the mode applies to; the engagement hooks
/// (AttackComponent, SpellHandler) and the pet's think call into the same Assists check.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_PetAssistMode
{
    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
    private GameServer _previousServer;

    private sealed class InertServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((InertServer)RuntimeHelpers.GetUninitializedObject(typeof(InertServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private sealed class Brain : ControlledMobBrain
    {
        public Brain(GameLiving owner) : base(owner) { }
        public void Force(eAggressionState state) => m_aggressionState = state;
    }

    private static GamePlayer Player(IControlledBrain mainPet = null)
    {
        var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        typeof(GameLiving).GetField("m_controlledBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(player, new IControlledBrain[] { mainPet });
        return player;
    }

    private static void SetMainPet(GamePlayer player, IControlledBrain brain) =>
        ((IControlledBrain[])typeof(GameLiving).GetField("m_controlledBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(player))[0] = brain;

    private static GameNPC Npc()
    {
        var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(npc, new ArrayList());
        return npc;
    }

    [Test]
    public void IsOffUntilThePlayerTurnsItOn()
    {
        GamePlayer player = Player();
        var brain = new Brain(player);
        SetMainPet(player, brain);

        Assert.That(PetAssistMode.IsOn(player), Is.False);
        Assert.That(PetAssistMode.Assists(brain), Is.False);
        Assert.That(PetAssistMode.CheckOwnerTarget(brain), Is.False, "off: the pet's think does nothing");

        PetAssistMode.Set(player, true);
        Assert.That(PetAssistMode.Assists(brain), Is.True, "a defensive main pet assists");

        PetAssistMode.Set(player, false);
        Assert.That(PetAssistMode.Assists(brain), Is.False);
    }

    [Test]
    public void TurningItOnPutsAPassiveOrAggressivePetInDefensive()
    {
        foreach (eAggressionState start in new[] { eAggressionState.Passive, eAggressionState.Aggressive })
        {
            GamePlayer player = Player();
            var brain = new Brain(player);
            brain.Force(start);
            SetMainPet(player, brain);

            PetAssistMode.Set(player, true);
            Assert.That(brain.AggressionState, Is.EqualTo(eAggressionState.Defensive), start.ToString());
            Assert.That(PetAssistMode.Assists(brain), Is.True, start.ToString());
        }
    }

    [Test]
    public void AnotherStanceStopsAssistingButTheChoiceStaysWithThePlayer()
    {
        GamePlayer player = Player();
        var brain = new Brain(player);
        SetMainPet(player, brain);
        PetAssistMode.Set(player, true);

        brain.Force(eAggressionState.Passive);
        Assert.That(PetAssistMode.Assists(brain), Is.False, "a passive pet never joins in");
        brain.Force(eAggressionState.Aggressive);
        Assert.That(PetAssistMode.Assists(brain), Is.False, "an aggressive pet picks its own fights");

        // A pet summoned later starts Defensive, and assists because the player still has it on.
        var next = new Brain(player);
        SetMainPet(player, next);
        Assert.That(PetAssistMode.Assists(next), Is.True);
    }

    [Test]
    public void OnlyThePlayersMainPetAssists()
    {
        GamePlayer player = Player();
        var main = new Brain(player);
        var other = new Brain(player);
        SetMainPet(player, main);
        PetAssistMode.Set(player, true);

        Assert.That(PetAssistMode.Assists(main), Is.True);
        Assert.That(PetAssistMode.Assists(other), Is.False, "a pet the pet window does not control");

        // A commander's minion (owner: the commander) follows the commander, not the player.
        var minion = new Brain(Npc());
        Assert.That(PetAssistMode.Assists(minion), Is.False);
        Assert.That(PetAssistMode.Assists(null), Is.False);
    }

    [Test]
    public void NothingHappensWithoutAPlayerOrATarget()
    {
        Assert.That(PetAssistMode.OwnerTarget(null), Is.Null);
        Assert.That(PetAssistMode.IsValidTarget(null, null, null), Is.False);
        Assert.That(() => PetAssistMode.OwnerEngaged(null, null), Throws.Nothing);
        Assert.That(() => PetAssistMode.Set(null, true), Throws.Nothing);

        GamePlayer noPet = Player();
        Assert.That(() => PetAssistMode.Set(noPet, true), Throws.Nothing, "on with no pet: the next pet assists");
        Assert.That(PetAssistMode.IsOn(noPet), Is.True);
        Assert.That(() => PetAssistMode.OwnerEngaged(noPet, Npc()), Throws.Nothing);
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using DOL.GS.Styles;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_BonedancerPetEquipment
{
    private sealed class EmptyServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl =>
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
    }

    private GameServer _oldServer;
    private PetTestLanguageScope _language;
    private IPropertyCalculator _oldMaxHealthCalculator;

    [OneTimeSetUp]
    public void InitializeLogging()
    {
        LoggerManager.InitializeWithExplicitLibrary(null, LogLibrary.None);
    }

    [SetUp]
    public void SetUp()
    {
        _oldServer = GameServer.Instance;
        GameServer.LoadTestDouble((EmptyServer)RuntimeHelpers.GetUninitializedObject(typeof(EmptyServer)));
        IPropertyCalculator[] calculators = (IPropertyCalculator[])typeof(GameLiving)
            .GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        _oldMaxHealthCalculator = calculators[(int)eProperty.MaxHealth];
        calculators[(int)eProperty.MaxHealth] = new MaxHealthCalculator();
        _language = new PetTestLanguageScope();
    }

    [TearDown]
    public void TearDown()
    {
        _language.Dispose();
        ((IPropertyCalculator[])typeof(GameLiving)
            .GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))[(int)eProperty.MaxHealth] =
            _oldMaxHealthCalculator;
        GameServer.LoadTestDouble(_oldServer);
    }

    // 1.65 looks (owner 2026-10-13): plain Norse weapons from the 1.64 retail client, no commander shield or helm,
    // no guardian buckler.
    [Test]
    public void CommanderShowsANorseWeaponAndNeverAShield()
    {
        CommanderPet commander = new CommanderPet(Template(50, "skeletal commander"));
        DbInventoryItem held = commander.Inventory.GetItem(eInventorySlot.RightHandWeapon) ?? commander.Inventory.GetItem(eInventorySlot.TwoHandWeapon);
        Assert.That(CommanderPet.CommanderWeapons.Select(w => (int)w.Model), Does.Contain(held?.Model), "one of the nine at summon");
        Assert.That(commander.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.Null);

        commander.CommanderSwitchWeapon(CommanderPet.eWeaponType.OneHandHammer, false, 322);
        Assert.That(commander.Inventory.GetItem(eInventorySlot.RightHandWeapon)?.Model, Is.EqualTo(322));
        Assert.That(commander.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.Null);

        commander.CommanderSwitchWeapon(CommanderPet.eWeaponType.TwoHandSword, false);
        Assert.That(commander.Inventory.GetItem(eInventorySlot.TwoHandWeapon)?.Model, Is.EqualTo(572));
        Assert.That(commander.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.Null);
    }

    [Test]
    public void OneHandedSwapBroadcastsAndDropsTheOtherWeaponsStyles()
    {
        CommanderPet commander = new CommanderPet(Template(50, "skeletal commander"));
        Style havoc = StyleOf("Havoc", eObjectType.Axe, 11);
        Style polar = StyleOf("Polar Rift", eObjectType.Sword, 12);
        commander.Styles = new List<Style> { havoc, polar };

        Assert.That(commander.CommanderSwitchWeapon(CommanderPet.eWeaponType.OneHandSword, false), Is.True);
        commander.SetPreferredCombatStyle(polar);
        int updates = commander.SameSlotEquipmentUpdates;
        Assert.That(commander.CommanderSwitchWeapon(CommanderPet.eWeaponType.OneHandAxe, false), Is.True);
        Assert.That(commander.ActiveWeaponSlot, Is.EqualTo(eActiveWeaponSlot.Standard));
        Assert.That(commander.ActiveWeapon.Object_Type, Is.EqualTo((int)eObjectType.Axe));
        Assert.That(commander.SameSlotEquipmentUpdates, Is.EqualTo(updates + 1));
        Assert.That(commander.PreferredCombatStyle, Is.Null);
        Assert.That(commander.styleComponent.NextCombatStyle, Is.Null);
        Assert.That(commander.StylesForEquippedWeapon().Select(s => s.Name), Is.EqualTo(new[] { "Havoc" }));
        Assert.That(commander.DialogMenu(), Does.Contain("[Havoc]"));
        Assert.That(commander.DialogMenu(), Does.Not.Contain("Polar Rift"));

        commander.styleComponent.NextCombatStyle = polar;
        Assert.That(commander.styleComponent.GetStyleToUse(), Is.Not.EqualTo(polar));
        Assert.That(commander.PreferredCombatStyle, Is.Null);

        int beforeTwoHand = commander.SameSlotEquipmentUpdates;
        Assert.That(commander.CommanderSwitchWeapon(CommanderPet.eWeaponType.TwoHandSword, false), Is.True);
        Assert.That(commander.ActiveWeaponSlot, Is.EqualTo(eActiveWeaponSlot.TwoHanded));
        Assert.That(commander.SameSlotEquipmentUpdates, Is.EqualTo(beforeTwoHand));
        Assert.That(commander.StylesForEquippedWeapon().Select(s => s.Name), Is.EqualTo(new[] { "Polar Rift" }));
    }

    [Test]
    public void DialogKeywordsRunTheOrderAndAppendTheReply()
    {
        CommanderPet commander = new CommanderPet(Template(50, "skeletal commander"));
        string menu = commander.DialogMenu();
        Assert.That(menu, Does.Contain("[Twohanded Sword]"));
        Assert.That(menu, Does.Contain("[Twohanded Axe]"));
        Assert.That(menu, Does.Contain("[Hammer]"));
        Assert.That(menu, Does.Contain("[Sword]"));
        Assert.That(menu, Does.Contain("[Axe]"));
        Assert.That(menu, Does.Contain("[assist]"));
        Assert.That(menu, Does.Contain("[split]"));
        Assert.That(menu, Does.Contain("[taunt]"));

        Assert.That(commander.ReplyToKeyword("assist"), Is.EqualTo("My minions will assist me."));
        Assert.That(commander.MinionsAssisting, Is.True);
        Assert.That(commander.ReplyToKeyword("split"), Is.EqualTo("My minions will split."));
        Assert.That(commander.MinionsAssisting, Is.False);
        commander.SetTaunting(true);
        Assert.That(commander.ReplyToKeyword("taunt"), Is.EqualTo("I will no longer taunt."));
        Assert.That(commander.Taunting, Is.False);

        Assert.That(commander.ReplyToKeyword("Twohanded Sword"), Is.EqualTo("I will wield the Twohanded Sword."));
        Assert.That(commander.ActiveWeaponSlot, Is.EqualTo(eActiveWeaponSlot.TwoHanded));
        Assert.That(commander.ActiveWeapon.Object_Type, Is.EqualTo((int)eObjectType.Sword));
    }

    private static Style StyleOf(string name, eObjectType weapon, int id) => new(new DbStyle
    {
        Name = name,
        ID = id,
        StyleID = id,
        SpecKeyName = "None",
        WeaponTypeRequirement = (int)weapon,
    }, null);

    [Test]
    public void BonePatrollerHasANorseHammerAndNoBuckler()
    {
        BdBufferSubPet patroller = new BdBufferSubPet(Template(65, "bone patroller"));
        patroller.InitializeActiveWeaponFromInventory();

        Assert.That(patroller.Inventory.GetItem(eInventorySlot.RightHandWeapon)?.Model, Is.EqualTo(321));
        Assert.That(patroller.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.Null);
        Assert.That(patroller.ActiveWeaponSlot, Is.EqualTo(eActiveWeaponSlot.Standard));
    }

    [Test]
    public void BoneDebufferCarriesANorseShortSword()
    {
        BdDebufferSubPet debuffer = new BdDebufferSubPet(Template(61, "bonecurser"));
        debuffer.InitializeActiveWeaponFromInventory();

        Assert.That(debuffer.Inventory.GetItem(eInventorySlot.RightHandWeapon)?.Model, Is.EqualTo(311));
        Assert.That(debuffer.Inventory.GetItem(eInventorySlot.TwoHandWeapon), Is.Null);
        Assert.That(debuffer.ActiveWeaponSlot, Is.EqualTo(eActiveWeaponSlot.Standard));
    }

    [Test]
    public void MeleeMinionCarriesOneOfTheFourNorseAxes()
    {
        for (int i = 0; i < 20; i++)
        {
            BdMeleeSubPet melee = new BdMeleeSubPet(Template(85, "bonecracker"));
            melee.InitializeActiveWeaponFromInventory();
            DbInventoryItem held = melee.Inventory.GetItem(eInventorySlot.RightHandWeapon) ?? melee.Inventory.GetItem(eInventorySlot.TwoHandWeapon);
            Assert.That(new[] { 578, 319, 317, 577 }, Does.Contain(held?.Model));
            Assert.That(melee.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.Null);
        }
    }

    private static NpcTemplate Template(int id, string name) => new(new DbNpcTemplate
    {
        TemplateId = id,
        Name = name,
        Model = "995",
        Level = "15",
        Size = "50",
        MaxSpeed = 200,
        Strength = 60,
        Constitution = 60,
        Dexterity = 60,
        Quickness = 60,
    });
}

using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DOL.UnitTests.Gameserver;

/// <summary>
/// Issues #143 (Minstrel bots dual wield) and #144 (a Cleric bot with two one-handed weapons): only a
/// class that can fight with a left-hand weapon may hold one there. Every other class keeps that hand
/// for its shield, in all three realms.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_BotShieldHand
{
    private sealed class ShieldTestServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private GameServer _previousServer;
    private PetTestLanguageScope _language;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        _language = new PetTestLanguageScope();
        GameServer.LoadTestDouble((ShieldTestServer)RuntimeHelpers.GetUninitializedObject(typeof(ShieldTestServer)));
    }

    [TearDown]
    public void TearDown()
    {
        GameServer.LoadTestDouble(_previousServer);
        _language.Dispose();
    }

    // The classes whose real players may put a weapon in the left hand (dual wield, Celtic dual, left axe).
    private static readonly HashSet<eCharacterClass> OffhandWeaponClasses = new()
    {
        eCharacterClass.Mercenary, eCharacterClass.Infiltrator,
        eCharacterClass.Berserker, eCharacterClass.Savage, eCharacterClass.Shadowblade,
        eCharacterClass.Blademaster, eCharacterClass.Nightshade, eCharacterClass.Ranger,
    };

    private sealed class ShieldHandBot : GameBot
    {
        private ShieldHandBot() : base((OfflineWorldBotRecord)null) { }
        public ICharacterClass Class;
        public override ICharacterClass CharacterClass => Class;
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
        public override void RefreshItemBonuses() { }
        public override void SwitchWeapon(eActiveWeaponSlot slot) { }
        public override bool HasAbilityToUseItem(DbItemTemplate item) =>
            (eObjectType)item.Object_Type != eObjectType.Shield || GetAbilityLevel(Abilities.Shield) >= item.Type_Damage;
    }

    private static ShieldHandBot Make(eRealm realm, eCharacterClass cls, int shieldLevel = 0)
    {
        var bot = (ShieldHandBot)RuntimeHelpers.GetUninitializedObject(typeof(ShieldHandBot));
        bot.Level = 20;
        bot.Realm = realm;
        bot.Name = cls.ToString();
        bot.Inventory = new BotInventory();
        Type type = typeof(GameBot).Assembly.GetTypes().First(t => !t.IsAbstract &&
            typeof(ICharacterClass).IsAssignableFrom(t) &&
            t.GetCustomAttributes<CharacterClassAttribute>(false).Any(a => a.ID == (int)cls));
        bot.Class = (ICharacterClass)Activator.CreateInstance(type);
        var abilities = new Dictionary<string, Ability>();
        if (shieldLevel > 0)
            abilities[Abilities.Shield] = new Ability(new DbAbility { KeyName = Abilities.Shield, Name = "Shields" }, shieldLevel);
        typeof(GameLiving).GetField("_abilitiesLock", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new Lock());
        typeof(GameLiving).GetField("m_abilities", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, abilities);
        return bot;
    }

    // A one-hander whose template allows either hand, as most loot and merchant weapons do.
    private static DbInventoryItem EitherHandWeapon(eObjectType type, int level = 20, int dps = 50) =>
        GameInventoryItem.Create(new DbItemTemplate
        {
            Id_nb = Guid.NewGuid().ToString(), Name = "either-hand " + type, Level = level,
            Object_Type = (int)type, Item_Type = Slot.LEFTHAND, Hand = 0,
            DPS_AF = dps, SPD_ABS = 30, Quality = 95, Condition = 50000, MaxCondition = 50000,
            Durability = 50000, MaxDurability = 50000, AllowedClasses = "0",
        });

    private static DbInventoryItem Shield(int size, int level = 20) =>
        GameInventoryItem.Create(new DbItemTemplate
        {
            Id_nb = Guid.NewGuid().ToString(), Name = "test shield", Level = level,
            Object_Type = (int)eObjectType.Shield, Item_Type = Slot.LEFTHAND, Type_Damage = size,
            DPS_AF = 10, SPD_ABS = 30, Quality = 90, Condition = 50000, MaxCondition = 50000,
            Durability = 50000, MaxDurability = 50000, AllowedClasses = "0",
        });

    private static void SetPlan(GameBot bot, eObjectType weapon) =>
        typeof(GameBot).GetProperty(nameof(GameBot.BotSpec), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(bot, new BotSpec { WeaponOneType = weapon });

    private static bool RepairShieldHand(GameBot bot) =>
        (bool)typeof(GameBot).GetMethod("EnsureShieldHandOffhand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bot, null);

    [Test]
    public void OnlyOffhandWeaponClassesMayHoldAWeaponInTheLeftHandInEveryRealm()
    {
        Assert.Multiple(() =>
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            foreach (eCharacterClass cls in AutonomousBotIdentityGenerator.GetEraClasses(realm))
            {
                ShieldHandBot bot = Make(realm, cls);
                DbInventoryItem weapon = EitherHandWeapon(eObjectType.SlashingWeapon);
                bool expected = OffhandWeaponClasses.Contains(cls);
                Assert.That(BotWeaponStats.CanWieldOffhandWeapon(bot), Is.EqualTo(expected), $"{realm} {cls}");
                Assert.That(BotWeaponStats.FitsConfiguredSlot(bot, weapon, eInventorySlot.LeftHandWeapon),
                    Is.EqualTo(expected), $"{realm} {cls} left hand");
                Assert.That(BotWeaponStats.FitsConfiguredSlot(bot, weapon, eInventorySlot.RightHandWeapon),
                    Is.True, $"{realm} {cls} right hand");
            }
        });
    }

    [TestCase(eRealm.Albion, eCharacterClass.Minstrel, eObjectType.SlashingWeapon)]
    [TestCase(eRealm.Albion, eCharacterClass.Minstrel, eObjectType.ThrustWeapon)]
    [TestCase(eRealm.Albion, eCharacterClass.Cleric, eObjectType.CrushingWeapon)]
    [TestCase(eRealm.Midgard, eCharacterClass.Skald, eObjectType.Sword)]
    [TestCase(eRealm.Midgard, eCharacterClass.Healer, eObjectType.Hammer)]
    [TestCase(eRealm.Hibernia, eCharacterClass.Bard, eObjectType.Blunt)]
    [TestCase(eRealm.Hibernia, eCharacterClass.Druid, eObjectType.Blades)]
    public void ALootedEitherHandWeaponIsAMainHandUpgradeAndNeverReplacesTheShield(
        eRealm realm, eCharacterClass cls, eObjectType type)
    {
        ShieldHandBot bot = Make(realm, cls, 1);
        SetPlan(bot, type);
        DbInventoryItem shield = Shield(1);
        Assert.That(bot.Inventory.AddItem(eInventorySlot.LeftHandWeapon, shield), Is.True);

        Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, EitherHandWeapon(type), out eInventorySlot slot), Is.True);
        Assert.That(slot, Is.EqualTo(eInventorySlot.RightHandWeapon));

        Assert.That(bot.Inventory.AddItem(eInventorySlot.RightHandWeapon, EitherHandWeapon(type, 20, 60)), Is.True);
        Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, EitherHandWeapon(type, 20, 50), out _), Is.False,
            "A weaker spare is not an upgrade just because the left hand holds a shield");
        Assert.That(bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.SameAs(shield));
    }

    [Test]
    public void DualWieldersStillTakeAnOffhandWeapon()
    {
        ShieldHandBot bot = Make(eRealm.Albion, eCharacterClass.Mercenary, 1);
        SetPlan(bot, eObjectType.SlashingWeapon);
        Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, EitherHandWeapon(eObjectType.SlashingWeapon), out eInventorySlot slot), Is.True);
        Assert.That(slot, Is.EqualTo(eInventorySlot.LeftHandWeapon));
    }

    [TestCase(eRealm.Albion, eCharacterClass.Minstrel)]
    [TestCase(eRealm.Albion, eCharacterClass.Cleric)]
    [TestCase(eRealm.Midgard, eCharacterClass.Skald)]
    [TestCase(eRealm.Hibernia, eCharacterClass.Bard)]
    public void ASavedBotWithAWeaponInItsShieldHandGetsItsOwnShieldBack(eRealm realm, eCharacterClass cls)
    {
        ShieldHandBot bot = Make(realm, cls, 1);
        DbInventoryItem offhandWeapon = EitherHandWeapon(eObjectType.SlashingWeapon);
        DbInventoryItem shield = Shield(1);
        Assert.That(bot.Inventory.AddItem(eInventorySlot.LeftHandWeapon, offhandWeapon), Is.True);
        Assert.That(bot.Inventory.AddItem(eInventorySlot.FirstBackpack + 3, shield), Is.True);

        Assert.That(RepairShieldHand(bot), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.SameAs(shield));
            Assert.That(bot.Inventory.AllItems, Does.Contain(offhandWeapon), "The weapon is stowed, never deleted");
            Assert.That(offhandWeapon.SlotPosition, Is.InRange((int)eInventorySlot.FirstBackpack, (int)eInventorySlot.LastBackpack));
        });
        Assert.That(RepairShieldHand(bot), Is.False, "A correct loadout is left alone");
    }

    [Test]
    public void AGamebotWithoutAShieldIsNotGivenOneAndATooLargeShieldStaysPacked()
    {
        ShieldHandBot bot = Make(eRealm.Albion, eCharacterClass.Cleric, 1);
        DbInventoryItem offhandWeapon = EitherHandWeapon(eObjectType.CrushingWeapon);
        DbInventoryItem towerShield = Shield(3);
        Assert.That(bot.Inventory.AddItem(eInventorySlot.LeftHandWeapon, offhandWeapon), Is.True);
        Assert.That(bot.Inventory.AddItem(eInventorySlot.FirstBackpack, towerShield), Is.True);

        Assert.That(RepairShieldHand(bot), Is.True);
        Assert.That(bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.Null,
            "Gamebots earn their gear: no shield is made, and a Cleric cannot use a large one");
        Assert.That(towerShield.SlotPosition, Is.EqualTo((int)eInventorySlot.FirstBackpack));
    }

    [Test]
    public void ADualWieldersOffhandWeaponIsLeftInPlace()
    {
        ShieldHandBot bot = Make(eRealm.Hibernia, eCharacterClass.Blademaster);
        DbInventoryItem offhandWeapon = EitherHandWeapon(eObjectType.Blades);
        Assert.That(bot.Inventory.AddItem(eInventorySlot.LeftHandWeapon, offhandWeapon), Is.True);
        Assert.That(RepairShieldHand(bot), Is.False);
        Assert.That(bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.SameAs(offhandWeapon));
    }
}

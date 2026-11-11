using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.PropertyCalc;
using DOL.GS.Styles;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// The summon path a player's commander takes (template, owner brain, SetPetLevel), with the
/// Midgard hammer rows of the released style table (Warrior, class 22).
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_CommanderSummonStyles
{
    private sealed class Server : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
    }

    private sealed class Bonedancer : GamePlayer
    {
        private Bonedancer() : base(null, null) { }
        public override ICharacterClass CharacterClass => new ClassBonedancer();
        public byte TestLevel;
        public override byte Level { get => TestLevel; set => TestLevel = value; }
    }

    private GameServer _previous;
    private PetTestLanguageScope _language;
    private System.Func<string, int, List<Style>> _source;
    private IPropertyCalculator _oldMaxHealthCalculator;

    private static IPropertyCalculator[] Calculators => (IPropertyCalculator[])typeof(GameLiving)
        .GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    [OneTimeSetUp]
    public void InitializeLogging() => LoggerManager.InitializeWithExplicitLibrary(null, LogLibrary.None);

    [SetUp]
    public void SetUp()
    {
        _language = new PetTestLanguageScope();
        _previous = GameServer.Instance;
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        _oldMaxHealthCalculator = Calculators[(int)eProperty.MaxHealth];
        Calculators[(int)eProperty.MaxHealth] = new MaxHealthCalculator();
        _source = CommanderPet.StyleSource;
        // style.sql: Warrior hammer line (ID, name, level, opening type, opening value).
        CommanderPet.StyleSource = (spec, classId) => classId != (int)eCharacterClass.Warrior || spec != "Hammer"
            ? new List<Style>()
            : new List<Style>
            {
                Row(161, "Thor's Anvil", 2, 0, 0), Row(162, "Crushing Blow", 4, 1, 0), Row(163, "Placate", 6, 0, 0),
                Row(164, "Slam", 8, 0, 162), Row(165, "Ruiner", 10, 0, 0), Row(166, "Frost Hammer", 12, 2, 1),
                Row(167, "Provoke", 15, 0, 0), Row(171, "Conquer", 29, 2, 0),
            };
    }

    [TearDown]
    public void TearDown()
    {
        CommanderPet.StyleSource = _source;
        Calculators[(int)eProperty.MaxHealth] = _oldMaxHealthCalculator;
        GameServer.LoadTestDouble(_previous);
        _language.Dispose();
    }

    private static Style Row(int id, string name, int level, int openingType, int openingValue) => new(new DbStyle
    {
        ID = id, StyleID = id, ClassId = (int)eCharacterClass.Warrior, Name = name, SpecKeyName = "Hammer",
        SpecLevelRequirement = level, OpeningRequirementType = openingType, OpeningRequirementValue = openingValue,
        WeaponTypeRequirement = (int)eObjectType.Hammer,
    }, null);

    private static CommanderPet Summon(byte ownerLevel)
    {
        var template = new NpcTemplate(new DbNpcTemplate
        {
            TemplateId = 49, Name = "returned commander", Model = "2220", Level = "0", Size = "50", MaxSpeed = 200,
            Styles = "157|22;186|22;545|22",
        });
        var commander = new CommanderPet(template);
        var owner = (Bonedancer)RuntimeHelpers.GetUninitializedObject(typeof(Bonedancer));
        owner.TestLevel = ownerLevel;
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(commander, new ArrayList());
        commander.SetOwnBrain(new CommanderBrain(owner));
        commander.SummonSpellDamage = -88;
        commander.SummonSpellValue = 44;
        commander.SetPetLevel();
        Assert.That(commander.CommanderSwitchWeapon(CommanderPet.eWeaponType.OneHandHammer, false, 322), Is.True);
        return commander;
    }

    [Test]
    public void ALowCommanderStillKnowsTheFirstHammerStyle()
    {
        CommanderPet commander = Summon(2);
        Assert.That(commander.Level, Is.EqualTo(1));
        Assert.That(commander.PickableStylesForEquippedWeapon().Select(s => s.Name), Is.EqualTo(new[] { "Thor's Anvil" }));
        Assert.That(commander.DialogMenu(), Does.Contain("[Thor's Anvil]"));
    }

    [Test]
    public void AHigherCommanderKnowsMoreHammerStyles()
    {
        CommanderPet commander = Summon(20);
        Assert.That(commander.Level, Is.EqualTo(17));
        Assert.That(commander.PickableStylesForEquippedWeapon().Select(s => s.Name),
            Is.EqualTo(new[] { "Thor's Anvil", "Placate", "Ruiner", "Frost Hammer", "Provoke" }));
        Assert.That(commander.StylesForEquippedWeapon().Select(s => s.Name), Does.Contain("Slam").And.Contain("Crushing Blow"),
            "chain and defensive styles stay in the commander's own rotation");
        Assert.That(commander.StylesForEquippedWeapon().Select(s => s.Name), Does.Not.Contain("Conquer"));
    }

    [Test]
    public void ALineFiledUnderAnotherClassIsStillFound()
    {
        CommanderPet.StyleSource = (spec, classId) => classId == 40 && spec == "Hammer"
            ? new List<Style> { Row(161, "Thor's Anvil", 2, 0, 0) }
            : new List<Style>();
        CommanderPet commander = Summon(20);
        Assert.That(commander.PickableStylesForEquippedWeapon().Select(s => s.Name), Is.EqualTo(new[] { "Thor's Anvil" }));
    }
}

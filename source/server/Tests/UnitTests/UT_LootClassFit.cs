using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public class UT_LootClassFit
{
    private static readonly (string, int)[] FriarSpecs = { (Specs.Staff, 50), (Specs.Rejuvenation, 30), (Specs.Enhancement, 30) };
    private static readonly (string, int)[] TheurgistSpecs = { (Specs.Earth_Magic, 50), (Specs.Cold_Magic, 10), (Specs.Wind_Magic, 20) };
    private static readonly (string, int)[] MinstrelSpecs = { (Specs.Instruments, 50), (Specs.Slash, 30), (Specs.Stealth, 10) };
    private static readonly (string, int)[] ArmsmanSpecs = { (Specs.Slash, 50), (Specs.Polearms, 44), (Specs.Shields, 42) };

    private static DbInventoryItem Item(params (eProperty property, int amount)[] bonuses)
    {
        DbItemTemplate template = new()
        {
            Id_nb = Guid.NewGuid().ToString(), Name = "test piece", Level = 50, Quality = 100,
            Item_Type = (int)eInventorySlot.TwoHandWeapon, Object_Type = (int)eObjectType.Staff,
            MaxCount = 1, PackSize = 1, MaxCondition = 10000, MaxDurability = 10000
        };
        DbInventoryItem item = GameInventoryItem.Create(template);
        for (int i = 0; i < bonuses.Length; i++)
        {
            int type = (int)bonuses[i].property;
            int amount = bonuses[i].amount;
            switch (i)
            {
                case 0: item.Bonus1Type = type; item.Bonus1 = amount; break;
                case 1: item.Bonus2Type = type; item.Bonus2 = amount; break;
                case 2: item.Bonus3Type = type; item.Bonus3 = amount; break;
                case 3: item.Bonus4Type = type; item.Bonus4 = amount; break;
                default: throw new ArgumentOutOfRangeException(nameof(bonuses));
            }
        }
        return item;
    }

    private static LootClassFit.Fit FitFor(ICharacterClass characterClass, IEnumerable<(string, int)> specs, DbInventoryItem item) =>
        LootClassFit.Score(LootClassFit.Build(characterClass, 50, specs), item);

    /// <summary>The +Staff quarterstaff Jay kept losing on his Friar.</summary>
    private static DbInventoryItem FriarStaff() =>
        Item((eProperty.Skill_Staff, 4), (eProperty.Piety, 12), (eProperty.Strength, 9), (eProperty.Quickness, 6));

    [Test]
    public void AFriarStaffSuitsAFriarAndNotAListCasterWhoWouldWieldTheStaff()
    {
        LootClassFit.Fit friar = FitFor(new ClassFriar(), FriarSpecs, FriarStaff());
        LootClassFit.Fit theurgist = FitFor(new ClassTheurgist(), TheurgistSpecs, FriarStaff());

        Assert.Multiple(() =>
        {
            Assert.That(friar.SuitsClass, Is.True);
            Assert.That(friar.Share, Is.EqualTo(1).Within(0.001));
            Assert.That(theurgist.SuitsClass, Is.False, "A Theurgist has no Staff line and no use for Piety, Strength or Quickness.");
            Assert.That(friar.Score, Is.GreaterThan(theurgist.Score));
        });
    }

    [Test]
    public void AMinstrelPieceSuitsAMinstrelAndNotAnArmsman()
    {
        DbInventoryItem minstrel = Item((eProperty.Charisma, 15), (eProperty.Skill_Instruments, 3), (eProperty.Dexterity, 6));

        LootClassFit.Fit forMinstrel = FitFor(new ClassMinstrel(), MinstrelSpecs, minstrel);
        LootClassFit.Fit forArmsman = FitFor(new ClassArmsman(), ArmsmanSpecs, minstrel);

        Assert.Multiple(() =>
        {
            Assert.That(forMinstrel.SuitsClass, Is.True);
            Assert.That(forArmsman.SuitsClass, Is.False);
            Assert.That(forMinstrel.Score, Is.GreaterThan(forArmsman.Score * 3));
        });
    }

    [Test]
    public void AcuityCountsForTheClassManaStatOnly()
    {
        DbInventoryItem acuity = Item((eProperty.Acuity, 15));

        Assert.Multiple(() =>
        {
            Assert.That(FitFor(new ClassMinstrel(), MinstrelSpecs, acuity).SuitsClass, Is.True);
            Assert.That(FitFor(new ClassTheurgist(), TheurgistSpecs, acuity).SuitsClass, Is.True);
            Assert.That(FitFor(new ClassArmsman(), ArmsmanSpecs, acuity).SuitsClass, Is.False);
        });
    }

    [Test]
    public void GearWithoutBonusesAndHitsAndResistsSuitEveryone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FitFor(new ClassArmsman(), ArmsmanSpecs, Item()).SuitsClass, Is.True);
            Assert.That(FitFor(new ClassArmsman(), ArmsmanSpecs, Item()).HasBonuses, Is.False);
            Assert.That(FitFor(new ClassTheurgist(), TheurgistSpecs,
                Item((eProperty.MaxHealth, 40), (eProperty.Resist_Heat, 8))).SuitsClass, Is.True);
        });
    }

    [Test]
    public void ATrainedLineIsWorthMoreThanAnUntrainedOne()
    {
        DbInventoryItem staffSkill = Item((eProperty.Skill_Staff, 4));
        double trained = FitFor(new ClassFriar(), new[] { (Specs.Staff, 50) }, staffSkill).Score;
        double untrained = FitFor(new ClassFriar(), new[] { (Specs.Staff, 1) }, staffSkill).Score;

        Assert.That(trained, Is.GreaterThan(untrained));
    }

    [Test]
    public void EveryRealmClassHasStatPriorities()
    {
        // Base classes (Fighter, Acolyte...) and the custom Sluaghbinder use the class's own stats.
        eCharacterClass[] baseOrCustom =
        {
            eCharacterClass.Unknown, eCharacterClass.Acolyte, eCharacterClass.AlbionRogue, eCharacterClass.Disciple,
            eCharacterClass.Elementalist, eCharacterClass.Fighter, eCharacterClass.Forester, eCharacterClass.Guardian,
            eCharacterClass.Mage, eCharacterClass.Magician, eCharacterClass.MidgardRogue, eCharacterClass.Mystic,
            eCharacterClass.Naturalist, eCharacterClass.Seer, eCharacterClass.Stalker, eCharacterClass.Viking,
            eCharacterClass.Sluaghbinder
        };
        Dictionary<eStat, double> friar = LootClassFit.StatsFor(new ClassFriar());
        List<eCharacterClass> missing = Enum.GetValues<eCharacterClass>()
            .Where(c => !baseOrCustom.Contains(c) && !LootClassFit.HasPriorities(c)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty);
            Assert.That(friar[eStat.PIE], Is.EqualTo(3));
            Assert.That(friar.ContainsKey(eStat.INT), Is.False);
        });
    }

    [Test]
    public void TheBestClassFitWinsANeedRollUnlessTheFitsAreClose()
    {
        LootRollEntry best = new() { Choice = eLootChoice.Need, Fit = 60 };
        LootRollEntry poor = new() { Choice = eLootChoice.Need, Fit = 10 };
        LootRollEntry greed = new() { Choice = eLootChoice.Greed, Fit = 90 };
        // poor rolls 100, best rolls 5, greed rolls 50.
        Queue<int> dice = new(new[] { 100, 5, 50 });
        List<LootRollEntry> ranked = GroupLootRolls.Rank(new[] { poor, best, greed }, () => dice.Dequeue());

        Assert.That(ranked, Is.EqualTo(new[] { best, poor, greed }));

        LootRollEntry close = new() { Choice = eLootChoice.Need, Fit = 57 };
        LootRollEntry closeBest = new() { Choice = eLootChoice.Need, Fit = 60 };
        Queue<int> closeDice = new(new[] { 80, 20 });
        List<LootRollEntry> closeRanked = GroupLootRolls.Rank(new[] { close, closeBest }, () => closeDice.Dequeue());

        Assert.That(closeRanked, Is.EqualTo(new[] { close, closeBest }), "Within 10% of the best fit, the die decides.");
    }

    [Test]
    public void ADropWithoutBonusesGoesByTheDieAlone()
    {
        LootRollEntry first = new() { Choice = eLootChoice.Need };
        LootRollEntry second = new() { Choice = eLootChoice.Need };
        Queue<int> dice = new(new[] { 10, 90 });

        List<LootRollEntry> ranked = GroupLootRolls.Rank(new[] { first, second }, () => dice.Dequeue());

        Assert.That(ranked, Is.EqualTo(new[] { second, first }));
    }
}

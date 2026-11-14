using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_RealmExchangePricing
{
    [Test]
    public void SuggestCopper_PricesAPlainClothTorsoFromLevelQualityCategoryAndSlot()
    {
        // level^2 * 8 = 800, then quality 100, armor 1.10 and torso 1.25.
        DbInventoryItem torso = Piece("plain torso", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);

        Assert.That(RealmExchangePricing.SuggestCopper(torso), Is.EqualTo(1100));
    }

    [Test]
    public void SuggestCopper_RaisesPriceWithLevelAndQuality()
    {
        DbInventoryItem low = Piece("low", 10, 50, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        DbInventoryItem highLevel = Piece("high level", 20, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        DbInventoryItem highQuality = Piece("high quality", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);

        Assert.That(RealmExchangePricing.SuggestCopper(highLevel), Is.EqualTo(RealmExchangePricing.SuggestCopper(highQuality) * 4));
        Assert.That(RealmExchangePricing.SuggestCopper(highQuality), Is.EqualTo(RealmExchangePricing.SuggestCopper(low) * 2));
    }

    [Test]
    public void SuggestCopper_WeightsASkillPointAboveTheSameAttributePoint()
    {
        DbInventoryItem attribute = Piece("strength", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        attribute.Bonus1Type = (int)eProperty.Strength;
        attribute.Bonus1 = 10;
        DbInventoryItem skill = Piece("slashing", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        skill.Bonus1Type = (int)eProperty.Skill_Slashing;
        skill.Bonus1 = 10;

        Assert.That(RealmExchangePricing.SuggestCopper(attribute), Is.EqualTo(2200));
        Assert.That(RealmExchangePricing.SuggestCopper(skill), Is.EqualTo(3850));
        Assert.That(RealmExchangePricing.SuggestCopper(skill), Is.GreaterThan(RealmExchangePricing.SuggestCopper(attribute)));
    }

    [Test]
    public void SuggestCopper_DoesNotAddTheLumpBonusBudgetOnTopOfTypedStats()
    {
        DbInventoryItem typed = Piece("typed", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        typed.Bonus1Type = (int)eProperty.Strength;
        typed.Bonus1 = 10;
        DbInventoryItem withBudget = Piece("budget", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        withBudget.Bonus1Type = (int)eProperty.Strength;
        withBudget.Bonus1 = 10;
        withBudget.Bonus = 35;

        Assert.That(RealmExchangePricing.SuggestCopper(withBudget), Is.EqualTo(RealmExchangePricing.SuggestCopper(typed)));
    }

    [Test]
    public void SuggestCopper_PricesAWeaponAboveAGenericItemAndATorsoAboveARing()
    {
        DbInventoryItem weapon = Piece("sword", 10, 100, eObjectType.SlashingWeapon, eInventorySlot.RightHandWeapon);
        DbInventoryItem generic = Piece("junk", 10, 100, eObjectType.GenericItem, eInventorySlot.FirstBackpack);
        DbInventoryItem torso = Piece("torso", 10, 100, eObjectType.Magical, eInventorySlot.TorsoArmor);
        DbInventoryItem ring = Piece("ring", 10, 100, eObjectType.Magical, eInventorySlot.RightRing);

        Assert.That(RealmExchangePricing.SuggestCopper(weapon), Is.EqualTo(1056));
        Assert.That(RealmExchangePricing.SuggestCopper(generic), Is.EqualTo(360));
        Assert.That(RealmExchangePricing.SuggestCopper(torso), Is.GreaterThan(RealmExchangePricing.SuggestCopper(ring)));
        DbInventoryItem plainRing = Piece("plain ring", 10, 100, eObjectType.GenericItem, eInventorySlot.RightRing);
        Assert.That(RealmExchangePricing.SuggestCopper(plainRing), Is.EqualTo(RealmExchangePricing.SuggestCopper(ring)));
    }

    [Test]
    public void SuggestCopper_SellerDispositionMovesThePriceAroundTheSuggestion()
    {
        DbInventoryItem item = Piece("cloth", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);

        Assert.That(RealmExchangePricing.SuggestCopper(item, -0.35), Is.EqualTo(715));
        Assert.That(RealmExchangePricing.SuggestCopper(item, 0.50), Is.EqualTo(1650));
    }

    [Test]
    public void SuggestCopper_PricesAStackByHowManyItHolds()
    {
        DbInventoryItem stack = Piece("cloth stack", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        stack.MaxCount = 10;
        stack.Count = 4;

        Assert.That(RealmExchangePricing.SuggestCopper(stack), Is.EqualTo(4400));
    }

    [Test]
    public void IsDepositGear_KeepsWornGearAndLeavesTheRest()
    {
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("armor", 10, 100, eObjectType.Plate, eInventorySlot.LegsArmor)), Is.True);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("sword", 10, 100, eObjectType.Sword, eInventorySlot.RightHandWeapon)), Is.True);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("shield", 10, 100, eObjectType.Shield, eInventorySlot.LeftHandWeapon)), Is.True);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("lute", 10, 100, eObjectType.Instrument, eInventorySlot.RightHandWeapon)), Is.True);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("ring", 10, 100, eObjectType.Magical, eInventorySlot.RightRing)), Is.True);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("cloak", 10, 100, eObjectType.Magical, eInventorySlot.Cloak)), Is.True);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("plain ring", 10, 100, eObjectType.GenericItem, eInventorySlot.LeftRing)), Is.True);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("potion", 10, 100, eObjectType.Magical, eInventorySlot.FirstBackpack)), Is.False);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("arrows", 10, 100, eObjectType.Arrow, eInventorySlot.FirstQuiver)), Is.False);
        Assert.That(RealmExchangePricing.IsDepositGear(Piece("junk", 10, 100, eObjectType.GenericItem, eInventorySlot.FirstBackpack)), Is.False);

        DbInventoryItem bound = Piece("bound sword", 10, 100, eObjectType.Sword, eInventorySlot.RightHandWeapon);
        bound.IsTradable = false;
        Assert.That(RealmExchangePricing.IsDepositGear(bound), Is.False);
    }

    [Test]
    public void BagBounds_CoverTheFiveBackpackBags()
    {
        Assert.That(RealmExchangePricing.BagCount * RealmExchangePricing.SlotsPerBag,
            Is.EqualTo((int)eInventorySlot.LastBackpack - (int)eInventorySlot.FirstBackpack + 1));
        Assert.That(RealmExchangePricing.TryGetBagBounds(1, out int first, out int last), Is.True);
        Assert.That(first, Is.EqualTo((int)eInventorySlot.FirstBackpack));
        Assert.That(last, Is.EqualTo((int)eInventorySlot.FirstBackpack + 7));
        Assert.That(RealmExchangePricing.TryGetBagBounds(5, out first, out last), Is.True);
        Assert.That(first, Is.EqualTo(72));
        Assert.That(last, Is.EqualTo((int)eInventorySlot.LastBackpack));
        Assert.That(RealmExchangePricing.TryGetBagBounds(0, out _, out _), Is.False);
        Assert.That(RealmExchangePricing.TryGetBagBounds(6, out _, out _), Is.False);
    }

    [Test]
    public void TryParseDepositBag_ReadsTheBagThePlayerNamed()
    {
        Assert.That(RealmExchangePricing.TryParseDepositBag("[Deposit all gear from bag 3]", out int bag), Is.True);
        Assert.That(bag, Is.EqualTo(3));
        Assert.That(RealmExchangePricing.TryParseDepositBag("deposit all gear from bag 1", out bag), Is.True);
        Assert.That(bag, Is.EqualTo(1));
        Assert.That(RealmExchangePricing.TryParseDepositBag("deposit all gear from bag 6", out _), Is.False);
        Assert.That(RealmExchangePricing.TryParseDepositBag("browse the exchange", out _), Is.False);
    }

    [Test]
    public void PlanBag_ListsOnlyTheOpenBagsTradableGearAtSuggestedPrices()
    {
        DbInventoryItem sword = Piece("sword", 20, 90, eObjectType.SlashingWeapon, eInventorySlot.RightHandWeapon);
        sword.SlotPosition = 48;
        DbInventoryItem potion = Piece("potion", 20, 90, eObjectType.Magical, eInventorySlot.FirstBackpack);
        potion.SlotPosition = 49;
        DbInventoryItem bound = Piece("bound armor", 20, 90, eObjectType.Chain, eInventorySlot.TorsoArmor);
        bound.SlotPosition = 50;
        bound.IsTradable = false;
        DbInventoryItem ring = Piece("ring", 20, 90, eObjectType.Magical, eInventorySlot.LeftRing);
        ring.SlotPosition = 52;
        DbInventoryItem otherBag = Piece("other bag", 20, 90, eObjectType.Sword, eInventorySlot.RightHandWeapon);
        otherBag.SlotPosition = 40;
        DbInventoryItem equipped = Piece("worn", 20, 90, eObjectType.Sword, eInventorySlot.RightHandWeapon);
        equipped.SlotPosition = (int)eInventorySlot.RightHandWeapon;

        List<RealmExchangePricing.DepositPiece> plan = RealmExchangePricing.PlanBag(
            new[] { ring, equipped, potion, sword, bound, otherBag },
            2,
            new[] { (int)eInventorySlot.Consignment_First },
            (int)eInventorySlot.Consignment_First,
            RealmExchangePricing.PlayerListingCapacity);

        Assert.That(plan.Select(piece => piece.Item), Is.EqualTo(new[] { sword, ring }));
        Assert.That(plan[0].ListingSlot, Is.EqualTo((int)eInventorySlot.Consignment_First + 1));
        Assert.That(plan[1].ListingSlot, Is.EqualTo((int)eInventorySlot.Consignment_First + 2));
        Assert.That(plan[0].PriceCopper, Is.EqualTo(RealmExchangePricing.SuggestCopper(sword)));
        Assert.That(plan[1].PriceCopper, Is.EqualTo(RealmExchangePricing.SuggestCopper(ring)));
    }

    [Test]
    public void PlanBag_StopsWhenTheChestHasNoFreeSlot()
    {
        DbInventoryItem first = Piece("first", 10, 100, eObjectType.Cloth, eInventorySlot.TorsoArmor);
        first.SlotPosition = 40;
        DbInventoryItem second = Piece("second", 10, 100, eObjectType.Cloth, eInventorySlot.LegsArmor);
        second.SlotPosition = 41;

        List<RealmExchangePricing.DepositPiece> plan = RealmExchangePricing.PlanBag(
            new[] { first, second }, 1, System.Array.Empty<int>(), (int)eInventorySlot.Consignment_First, 1);

        Assert.That(plan, Has.Count.EqualTo(1));
        Assert.That(plan[0].Item, Is.SameAs(first));
    }

    private static DbInventoryItem Piece(string name, int level, int quality, eObjectType objectType, eInventorySlot slot)
    {
        DbItemTemplate template = new()
        {
            Id_nb = name + "-" + System.Guid.NewGuid().ToString("N"),
            Name = name,
            Level = level,
            Quality = quality,
            Object_Type = (int)objectType,
            Item_Type = (int)slot,
            MaxCount = 1,
            PackSize = 1,
            IsTradable = true,
            IsDropable = true,
        };
        return GameInventoryItem.Create(template);
    }
}

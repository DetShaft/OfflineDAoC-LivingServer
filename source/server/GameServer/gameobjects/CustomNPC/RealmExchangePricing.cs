using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// One copper price for a real item, shared by a player's deposit and by the bots'
/// Realm Exchange. The price comes from the item's level, quality, category, equipment
/// slot, and its typed bonuses. An attribute point is worth less than a skill point.
/// The plain Bonus column is the item's bonus budget, not a second copy of those stats,
/// so it is not added again.
/// </summary>
public static class RealmExchangePricing
{
    public const int BagCount = 5;
    public const int SlotsPerBag = 8;
    public const int PlayerListingCapacity = 10_000;

    public const int AttributeWeight = 2;
    public const int SkillWeight = 5;
    public const int ResistWeight = 4;
    public const int PowerWeight = 3;
    public const int HealthWeight = 1;
    public const int OtherBonusWeight = 2;

    // Level 50 with no bonuses is 2 gold before quality, category and slot.
    // A useful level 50 piece lands around a few gold, which is what a bot will pay.
    private const int LevelSquaredCopper = 8;
    private const int UtilityCopperPerLevel = 4;

    public readonly record struct DepositPiece(DbInventoryItem Item, int ListingSlot, int PriceCopper);

    public static int SuggestCopper(DbInventoryItem item, double sellerDisposition = 0)
    {
        if (item?.Template == null)
            return 0;

        int level = Math.Clamp(item.Level, 1, 200);
        int quality = Math.Clamp(item.Quality, 1, 100);
        long utility = Math.Max(0, WeightedUtility(item));
        long raw = (long)level * level * LevelSquaredCopper + utility * level * UtilityCopperPerLevel;
        eInventorySlot slot = (eInventorySlot)item.Item_Type;
        long priced = raw * quality * CategoryPerMille((eObjectType)item.Object_Type, slot) * SlotPerMille(slot);
        priced /= 100L * 1000L * 1000L;
        if (item.MaxCount > 1)
            priced *= Math.Max(1, item.Count);
        if (priced < 1)
            priced = 1;

        double disposition = Math.Clamp(sellerDisposition, -0.35, 0.50);
        int factor = (int)Math.Round((1d + disposition) * 1000d, MidpointRounding.AwayFromZero);
        priced = priced * factor / 1000L;
        if (priced < 1)
            priced = 1;
        if (priced > int.MaxValue)
            priced = int.MaxValue;
        return (int)priced;
    }

    /// <summary>
    /// Armor, weapons, shields, instruments, and jewelry or other worn pieces.
    /// Potions, materials, ammunition and anything that cannot be traded stay in the bag.
    /// </summary>
    public static bool IsDepositGear(DbInventoryItem item)
    {
        if (item?.Template == null || !item.IsTradable)
            return false;

        eObjectType type = (eObjectType)item.Object_Type;
        if (type is eObjectType.Arrow or eObjectType.Bolt or eObjectType.Poison
            or eObjectType.AlchemyTincture or eObjectType.SpellcraftGem)
            return false;

        if (type is >= eObjectType._FirstWeapon and <= eObjectType._LastWeapon
            || type == eObjectType.GenericWeapon
            || type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor
            || type is eObjectType.Shield or eObjectType.Instrument)
            return true;

        // Jewelry is often stored as a generic or magical item whose slot is the worn piece.
        return IsWornSlot((eInventorySlot)item.Item_Type);
    }

    public static bool TryGetBagBounds(int bagNumber, out int firstSlot, out int lastSlot)
    {
        firstSlot = 0;
        lastSlot = 0;
        if (bagNumber < 1 || bagNumber > BagCount)
            return false;

        firstSlot = (int)eInventorySlot.FirstBackpack + (bagNumber - 1) * SlotsPerBag;
        lastSlot = firstSlot + SlotsPerBag - 1;
        return true;
    }

    public static bool TryParseDepositBag(string text, out int bagNumber)
    {
        bagNumber = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string command = text.Trim().TrimStart('[').TrimEnd(']').Trim().ToLowerInvariant();
        const string prefix = "deposit all gear from bag ";
        if (!command.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string rest = command[prefix.Length..].Trim();
        if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int bag) || bag < 1 || bag > BagCount)
            return false;

        bagNumber = bag;
        return true;
    }

    public static List<DepositPiece> PlanBag(
        IEnumerable<DbInventoryItem> bagItems,
        int bagNumber,
        IEnumerable<int> usedListingSlots,
        int firstListingSlot,
        int listingCapacity)
    {
        var planned = new List<DepositPiece>();
        if (bagItems == null || !TryGetBagBounds(bagNumber, out int firstSlot, out int lastSlot) || listingCapacity <= 0)
            return planned;

        var used = usedListingSlots == null ? new HashSet<int>() : new HashSet<int>(usedListingSlots);
        int limit = firstListingSlot + listingCapacity;
        foreach (DbInventoryItem item in bagItems
                     .Where(item => item != null && item.SlotPosition >= firstSlot && item.SlotPosition <= lastSlot && IsDepositGear(item))
                     .OrderBy(item => item.SlotPosition))
        {
            int listingSlot = NextFreeSlot(used, firstListingSlot, limit);
            if (listingSlot < 0)
                break;

            planned.Add(new DepositPiece(item, listingSlot, SuggestCopper(item)));
            used.Add(listingSlot);
        }

        return planned;
    }

    private static int NextFreeSlot(HashSet<int> used, int first, int limit)
    {
        for (int slot = first; slot < limit; slot++)
        {
            if (!used.Contains(slot))
                return slot;
        }

        return -1;
    }

    private static long WeightedUtility(DbInventoryItem item)
    {
        long total = (long)BonusWeight(item.Bonus1Type) * item.Bonus1
                      + (long)BonusWeight(item.Bonus2Type) * item.Bonus2
                      + (long)BonusWeight(item.Bonus3Type) * item.Bonus3
                      + (long)BonusWeight(item.Bonus4Type) * item.Bonus4
                      + (long)BonusWeight(item.Bonus5Type) * item.Bonus5
                      + (long)BonusWeight(item.Bonus6Type) * item.Bonus6
                      + (long)BonusWeight(item.Bonus7Type) * item.Bonus7
                      + (long)BonusWeight(item.Bonus8Type) * item.Bonus8
                      + (long)BonusWeight(item.Bonus9Type) * item.Bonus9
                      + (long)BonusWeight(item.Bonus10Type) * item.Bonus10
                      + (long)BonusWeight(item.ExtraBonusType) * item.ExtraBonus;
        return total;
    }

    private static int BonusWeight(int bonusType)
    {
        if (bonusType <= 0)
            return 0;
        if (bonusType <= (int)eProperty.Stat_Last || bonusType == (int)eProperty.Acuity)
            return AttributeWeight;
        if (bonusType == (int)eProperty.MaxMana)
            return PowerWeight;
        if (bonusType == (int)eProperty.MaxHealth)
            return HealthWeight;
        if (bonusType <= (int)eProperty.Resist_Last)
            return ResistWeight;
        if (bonusType >= (int)eProperty.Skill_First && bonusType <= (int)eProperty.Skill_Last)
            return SkillWeight;
        if (bonusType is (int)eProperty.AllMagicSkills or (int)eProperty.AllMeleeWeaponSkills
            or (int)eProperty.AllDualWieldingSkills or (int)eProperty.AllArcherySkills
            or (int)eProperty.AllSkills)
            return SkillWeight;
        return OtherBonusWeight;
    }

    private static int CategoryPerMille(eObjectType type, eInventorySlot slot)
    {
        if (type is >= eObjectType._FirstWeapon and <= eObjectType._LastWeapon || type == eObjectType.GenericWeapon)
            return 1200;
        if (type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor)
            return 1100;
        if (type == eObjectType.Shield)
            return 1050;
        if (type == eObjectType.Instrument)
            return 950;
        if (type == eObjectType.Magical)
            return 1000;
        if (type is eObjectType.Arrow or eObjectType.Bolt or eObjectType.Poison
            or eObjectType.AlchemyTincture or eObjectType.SpellcraftGem)
            return 350;
        // A worn piece with no finer category (some jewelry) is still gear, not loose junk.
        if (IsWornSlot(slot))
            return 1000;
        return 450;
    }

    private static int SlotPerMille(eInventorySlot slot) => slot switch
    {
        eInventorySlot.TwoHandWeapon or eInventorySlot.TorsoArmor or eInventorySlot.LegsArmor => 1250,
        eInventorySlot.RightHandWeapon or eInventorySlot.DistanceWeapon or eInventorySlot.HeadArmor or eInventorySlot.ArmsArmor => 1100,
        eInventorySlot.Neck or eInventorySlot.Cloak or eInventorySlot.Jewelry or eInventorySlot.Waist
            or eInventorySlot.LeftBracer or eInventorySlot.RightBracer or eInventorySlot.LeftRing
            or eInventorySlot.RightRing or eInventorySlot.Mythical => 1150,
        _ => 1000,
    };

    private static bool IsWornSlot(eInventorySlot slot) => slot is
        eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.TwoHandWeapon or eInventorySlot.DistanceWeapon
        or eInventorySlot.HeadArmor or eInventorySlot.HandsArmor or eInventorySlot.FeetArmor or eInventorySlot.TorsoArmor
        or eInventorySlot.Cloak or eInventorySlot.LegsArmor or eInventorySlot.ArmsArmor or eInventorySlot.Neck
        or eInventorySlot.Jewelry or eInventorySlot.Waist or eInventorySlot.LeftBracer or eInventorySlot.RightBracer
        or eInventorySlot.LeftRing or eInventorySlot.RightRing or eInventorySlot.Mythical
        or eInventorySlot.HorseArmor or eInventorySlot.HorseBarding or eInventorySlot.Horse;
}

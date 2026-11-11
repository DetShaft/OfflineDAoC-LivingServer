using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CommanderControls
{
    [Test]
    public void TryParseWeaponType_RecognizesAliases()
    {
        // Ensure GameObject/CommanderPet type init can see a database double if needed.
        using var server = new EpicTestServerScope();
        Assert.That(CommanderPet.TryParseWeaponType("1h-axe", out var a), Is.True);
        Assert.That(a, Is.EqualTo(CommanderPet.eWeaponType.OneHandAxe));
        Assert.That(CommanderPet.TryParseWeaponType("2h-hammer", out var h), Is.True);
        Assert.That(h, Is.EqualTo(CommanderPet.eWeaponType.TwoHandHammer));
        Assert.That(CommanderPet.TryParseWeaponType("bow", out var b), Is.True);
        Assert.That(b, Is.EqualTo(CommanderPet.eWeaponType.Bow));
        Assert.That(CommanderPet.TryParseWeaponType("sword", out var s), Is.True);
        Assert.That(s, Is.EqualTo(CommanderPet.eWeaponType.OneHandSword));
        Assert.That(CommanderPet.TryParseWeaponType("2h-sword", out var t), Is.True);
        Assert.That(t, Is.EqualTo(CommanderPet.eWeaponType.TwoHandSword));
        Assert.That(CommanderPet.TryParseWeaponType("nope", out _), Is.False);
        Assert.That(CommanderPet.TryParseWeaponType("Twohanded Sword", out var two), Is.True);
        Assert.That(two, Is.EqualTo(CommanderPet.eWeaponType.TwoHandSword));
        Assert.That(CommanderPet.TryParseWeaponType("Twohanded Axe", out var axe), Is.True);
        Assert.That(axe, Is.EqualTo(CommanderPet.eWeaponType.TwoHandAxe));
    }

    [Test]
    public void StyleFits_KeepsTheEquippedWeaponsLine()
    {
        var axe = new GameInventoryItem(new DOL.Database.DbItemTemplate { Object_Type = (int)eObjectType.Axe });
        var sword = new GameInventoryItem(new DOL.Database.DbItemTemplate { Object_Type = (int)eObjectType.Sword });
        Style havoc = StyleOf("Havoc", eObjectType.Axe, 1);
        Style polar = StyleOf("Polar Rift", eObjectType.Sword, 2);
        Assert.That(CommanderPet.StyleFits(havoc, axe), Is.True);
        Assert.That(CommanderPet.StyleFits(polar, axe), Is.False);
        Assert.That(CommanderPet.StyleFits(polar, sword), Is.True);
        var names = CommanderPet.StylesMatching(new[] { havoc, polar }, axe).Select(s => s.Name);
        Assert.That(names, Is.EqualTo(new[] { "Havoc" }));
    }

    [Test]
    public void SameSlotSwapNeedsAnEquipmentBroadcast()
    {
        Assert.That(CommanderPet.NeedsExtraEquipmentBroadcast(eActiveWeaponSlot.Standard, eActiveWeaponSlot.Standard), Is.True);
        Assert.That(CommanderPet.NeedsExtraEquipmentBroadcast(eActiveWeaponSlot.TwoHanded, eActiveWeaponSlot.TwoHanded), Is.True);
        Assert.That(CommanderPet.NeedsExtraEquipmentBroadcast(eActiveWeaponSlot.Standard, eActiveWeaponSlot.TwoHanded), Is.False);
        Assert.That(CommanderPet.NeedsExtraEquipmentBroadcast(eActiveWeaponSlot.Distance, eActiveWeaponSlot.Distance), Is.False);
    }

    private static Style StyleOf(string name, eObjectType weapon, int id)
    {
        var row = new DOL.Database.DbStyle
        {
            Name = name,
            ID = id,
            StyleID = id,
            SpecKeyName = "None",
            WeaponTypeRequirement = (int)weapon,
        };
        return new DOL.GS.Styles.Style(row, null);
    }

    [Test]
    public void SetTaunting_AndPreferredSpell_ArePublic()
    {
        // GameObject fills its quest cache on first use; isolate with an empty DB double.
        using var server = new EpicTestServerScope();
        var pet = (CommanderPet)RuntimeHelpers.GetUninitializedObject(typeof(CommanderPet));
        pet.SetTaunting(true);
        Assert.That(pet.Taunting, Is.True);
        pet.SetTaunting(false);
        Assert.That(pet.Taunting, Is.False);
        pet.SetPreferredSpell(CommanderPet.eCommanderPreferredSpell.Damage);
        Assert.That(pet.PreferredSpell, Is.EqualTo(CommanderPet.eCommanderPreferredSpell.Damage));
        pet.SetMinionsAssisting(true);
        Assert.That(pet.MinionsAssisting, Is.True);
        pet.SetMinionsAssisting(false);
        Assert.That(pet.MinionsAssisting, Is.False);
    }
}

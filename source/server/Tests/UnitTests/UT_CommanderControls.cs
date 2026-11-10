using System.Runtime.CompilerServices;
using DOL.GS;
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

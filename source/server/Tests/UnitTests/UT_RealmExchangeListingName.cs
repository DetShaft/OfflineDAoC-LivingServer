using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// A listing's price travels in its item name ("Name[price]"); the new client's My Listings column reads it from
/// there. A long name must never cut the price off.
/// </summary>
[TestFixture]
public sealed class UT_RealmExchangeListingName
{
    [Test]
    public void UnpricedItemKeepsItsName()
    {
        Assert.That(PacketLib1124.BuildItemDisplayName("Studded Helm", 1, 0, true), Is.EqualTo("Studded Helm"));
    }

    [Test]
    public void PriceFollowsTheName()
    {
        Assert.That(PacketLib1124.BuildItemDisplayName("Studded Helm", 1, 1000, true), Is.EqualTo("Studded Helm[1000 BP]"));
    }

    [Test]
    public void StackCountLeadsTheName()
    {
        Assert.That(PacketLib1124.BuildItemDisplayName("Arrows", 20, 50, true), Is.EqualTo("20 Arrows[50 BP]"));
    }

    [Test]
    public void LongNameIsShortenedButThePriceIsKeptWhole()
    {
        string longName = new('x', 80);
        string shown = PacketLib1124.BuildItemDisplayName(longName, 1, 123456, true);

        Assert.That(shown.Length, Is.EqualTo(55));
        Assert.That(shown, Does.EndWith("[123456 BP]"));
    }

    [Test]
    public void ShortNameIsNeverTruncated()
    {
        string name = new('y', 40);
        Assert.That(PacketLib1124.BuildItemDisplayName(name, 1, 0, true), Is.EqualTo(name));
    }
}

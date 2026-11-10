using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_LfgBrowser
{
    [SetUp]
    public void SetUp() => BotLfgAds.ClearForTests();

    [TearDown]
    public void TearDown() => BotLfgAds.ClearForTests();

    [Test]
    public void LfgListFilters_ParseClassLevelZone()
    {
        LfgListFilters f = LfgListFilters.Parse(["list", "Healer", "1-20", "Gotar"], 1);
        Assert.That(f.ClassContains, Is.EqualTo("Healer"));
        Assert.That(f.MinLevel, Is.EqualTo(1));
        Assert.That(f.MaxLevel, Is.EqualTo(20));
        Assert.That(f.ZoneContains, Is.EqualTo("Gotar"));
    }

    [Test]
    public void LfgListFilters_ParseLevelOnlyThenZone()
    {
        LfgListFilters f = LfgListFilters.Parse(["list", "40-50", "Camelot Hills"], 1);
        Assert.That(f.ClassContains, Is.Null);
        Assert.That(f.MinLevel, Is.EqualTo(40));
        Assert.That(f.MaxLevel, Is.EqualTo(50));
        Assert.That(f.ZoneContains, Is.EqualTo("Camelot Hills"));
    }

    [Test]
    public void LfgListFilters_MatchesEntry()
    {
        var entry = new LfgWindowEntry
        {
            Name = "Eirikaenulf",
            Level = 4,
            ClassName = "Healer",
            ZoneDescription = "Gotar",
            IsBot = true,
        };
        Assert.That(new LfgListFilters { ClassContains = "heal" }.Matches(entry), Is.True);
        Assert.That(new LfgListFilters { MinLevel = 10 }.Matches(entry), Is.False);
        Assert.That(new LfgListFilters { ZoneContains = "got" }.Matches(entry), Is.True);
        Assert.That(new LfgListFilters { ClassContains = "Warrior" }.Matches(entry), Is.False);
    }

    [Test]
    public void BotLfgAds_FreshWindow()
    {
        const long id = 42;
        BotLfgAds.SetAdTickForTests(id, 1000);
        GameBot bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        bot.DatabaseID = id;
        Assert.That(BotLfgAds.IsAdvertising(bot, now: 1000 + BotLfgAds.FreshMilliseconds - 1), Is.True);
        Assert.That(BotLfgAds.IsAdvertising(bot, now: 1000 + BotLfgAds.FreshMilliseconds + 1), Is.False);

        GameBot other = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        other.DatabaseID = 99;
        Assert.That(BotLfgAds.IsAdvertising(other, now: 1500), Is.False);
    }

    [Test]
    public void FormatTextLines_MarksBotsAndHintsInvite()
    {
        var lines = LfgBrowser.FormatTextLines([
            new LfgWindowEntry
            {
                Name = "Rageunmund", Level = 12, ClassName = "Healer",
                ZoneDescription = "Gotar", IsBot = true,
            },
            new LfgWindowEntry
            {
                Name = "Jai", Level = 3, ClassName = "Bonedancer",
                ZoneDescription = "Gotar", IsBot = false,
            },
        ]);
        Assert.That(lines[0], Does.Contain("Looking for group"));
        Assert.That(string.Join('\n', lines), Does.Contain("[BOT]"));
        Assert.That(string.Join('\n', lines), Does.Contain("/invite"));
    }
}

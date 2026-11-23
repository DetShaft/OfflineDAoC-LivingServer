using System.Data.SQLite;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OfflineDaoc.Launcher;

/// <summary>Issue #69: the launcher's character builder (Tools tab).</summary>
[TestFixture, NonParallelizable]
public class CharacterBuilderTests
{
    private string _folder = null!, _database = null!;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "builder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "opendaoc.sqlite3.db");
    }

    [TearDown] public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }
        Assert.Fail("Not found from the test folder: " + Path.Combine(parts));
        return "";
    }

    private static BuilderClass Class(string name) => CharacterBuilder.Classes.Single(c => c.Name == name);

    [Test] public void CreationPointsCostLikeTheServersValidator()
    {
        Assert.That(CharacterBuilder.CreationCost(0), Is.EqualTo(0));
        Assert.That(CharacterBuilder.CreationCost(10), Is.EqualTo(10));
        Assert.That(CharacterBuilder.CreationCost(11), Is.EqualTo(12));
        Assert.That(CharacterBuilder.CreationCost(15), Is.EqualTo(20));
        Assert.That(CharacterBuilder.CreationCost(16), Is.EqualTo(23));
    }

    [Test] public void LevelsRaiseTheClassStats()
    {
        BuilderClass armsman = Class("Armsman"); // STR, CON, DEX
        Assert.That(CharacterBuilder.LevelStatGain(armsman, BuilderStat.STR, 5), Is.EqualTo(0));
        Assert.That(CharacterBuilder.LevelStatGain(armsman, BuilderStat.STR, 50), Is.EqualTo(45));
        Assert.That(CharacterBuilder.LevelStatGain(armsman, BuilderStat.CON, 50), Is.EqualTo(23));
        Assert.That(CharacterBuilder.LevelStatGain(armsman, BuilderStat.DEX, 50), Is.EqualTo(15));
        Assert.That(CharacterBuilder.LevelStatGain(armsman, BuilderStat.INT, 50), Is.EqualTo(0));
    }

    [Test] public void SpecPointsFollowVerifySpecPoints()
    {
        Assert.That(CharacterBuilder.SpecPointsAt(Class("Wizard"), 5, false), Is.EqualTo(14));
        Assert.That(CharacterBuilder.SpecPointsAt(Class("Wizard"), 50, false), Is.EqualTo(1494));
        Assert.That(CharacterBuilder.SpecPointsAt(Class("Bard"), 50, false), Is.EqualTo(2223));
        Assert.That(CharacterBuilder.SpecPointsAt(Class("Armsman"), 40, false), Is.EqualTo(1624));
        Assert.That(CharacterBuilder.SpecPointsAt(Class("Armsman"), 40, true), Is.EqualTo(1664));
        Assert.That(CharacterBuilder.SpecPointsAt(Class("Armsman"), 50, true), Is.EqualTo(2979), "there is no 50.5");
        Assert.That(CharacterBuilder.SpecPointsAt(Class("Infiltrator"), 50, false), Is.EqualTo(3706));
    }

    [Test] public void AutotrainedLinesAreCheaper()
    {
        BuilderClass armsman = Class("Armsman");
        Assert.That(CharacterBuilder.SpecCost(50), Is.EqualTo(1274));
        Assert.That(CharacterBuilder.SpecPointsSpent(armsman, "Slash", 50, 50), Is.EqualTo(1274 - 77));
        Assert.That(CharacterBuilder.SpecPointsSpent(armsman, "Crush", 50, 50), Is.EqualTo(1274));
        Assert.That(CharacterBuilder.SpecPointsSpent(armsman, "Slash", 5, 50), Is.EqualTo(0), "below the free level it costs nothing");
        Assert.That(CharacterBuilder.AutotrainLevel(3), Is.EqualTo(1));
    }

    [Test] public void EveryRosterClassHasRulesAndRaces()
    {
        Assert.That(CharacterBuilder.Classes.Count, Is.EqualTo(41));
        foreach (BuilderClass cls in CharacterBuilder.Classes)
        {
            Assert.That(cls.Races, Is.Not.Empty, cls.Name);
            Assert.That(cls.SpecMultiplier, Is.InRange(10, 25), cls.Name);
        }
        Assert.That(CharacterBuilder.ClassesForRealm(3, allowSluaghbinder: false).Any(c => c.Id == 63), Is.False);
        Assert.That(CharacterBuilder.ClassesForRealm(3, allowSluaghbinder: true).Any(c => c.Id == 63), Is.True);
    }

    /// <summary>The class table is a copy of the server's class files; this keeps them from drifting apart.</summary>
    [Test] public void ClassTableMatchesTheServerSources()
    {
        string folder = RepoFile("source", "server", "GameServer", "playerclasses");
        string skills = File.ReadAllText(RepoFile("source", "server", "GameServer", "gameutils", "SkillConstants.cs"));
        string specsBlock = skills[skills.IndexOf("abstract class Specs", StringComparison.Ordinal)..];
        var specNames = Regex.Matches(specsBlock, @"public const string (\w+)\s*=\s*""([^""]+)""")
            .GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => g.First().Groups[2].Value);

        var files = new Dictionary<string, (string Base, string Text, string? Name)>();
        foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Match type = Regex.Match(text, @"public class (\w+)\s*:\s*(\w+)");
            Match attribute = Regex.Match(text, @"\[CharacterClass(?:Attribute)?\(\(int\)eCharacterClass\.\w+,\s*""([^""]+)""");
            files[type.Groups[1].Value] = (type.Groups[2].Value, text, attribute.Success ? attribute.Groups[1].Value : null);
        }
        string? Field(string type, string pattern)
        {
            for (string? current = type; current != null && files.TryGetValue(current, out var entry); current = entry.Base)
            {
                Match m = Regex.Match(entry.Text, pattern);
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }

        foreach (BuilderClass cls in CharacterBuilder.Classes)
        {
            string type = files.Single(f => f.Value.Name == cls.Name).Key;
            Assert.That(int.Parse(Field(type, @"m_specializationMultiplier\s*=\s*(\d+)") ?? "10"), Is.EqualTo(cls.SpecMultiplier), cls.Name + " multiplier");
            Assert.That(Field(type, @"m_primaryStat\s*=\s*eStat\.(\w+)") ?? "UNDEFINED", Is.EqualTo(cls.Primary.ToString()), cls.Name + " primary");
            Assert.That(Field(type, @"m_secondaryStat\s*=\s*eStat\.(\w+)") ?? "UNDEFINED", Is.EqualTo(cls.Secondary.ToString()), cls.Name + " secondary");
            Assert.That(Field(type, @"m_tertiaryStat\s*=\s*eStat\.(\w+)") ?? "UNDEFINED", Is.EqualTo(cls.Tertiary.ToString()), cls.Name + " tertiary");
            Assert.That(Field(type, @"m_manaStat\s*=\s*eStat\.(\w+)") ?? "UNDEFINED", Is.EqualTo(cls.ManaStat.ToString()), cls.Name + " power stat");
            Assert.That(int.Parse(Field(type, @"m_baseHP\s*=\s*(\d+)") ?? "700"), Is.EqualTo(cls.BaseHp), cls.Name + " base HP");
            string? autotrain = Field(type, @"AutotrainableSkills\s*=\s*new\[\]\s*\{([^}]*)\}");
            string[] expected = autotrain == null ? Array.Empty<string>() : autotrain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => specNames[s.Split('.').Last()]).ToArray();
            Assert.That(cls.Autotrain, Is.EqualTo(expected), cls.Name + " autotrain");
        }
    }

    [Test] public void StartingStatsMatchGlobalConstants()
    {
        string text = File.ReadAllText(RepoFile("source", "server", "GameServer", "GlobalConstants.cs"));
        var races = new Dictionary<string, int> { ["Briton"] = 1, ["Avalonian"] = 2, ["Highlander"] = 3, ["Saracen"] = 4, ["Norseman"] = 5, ["Troll"] = 6,
            ["Dwarf"] = 7, ["Kobold"] = 8, ["Celt"] = 9, ["Firbolg"] = 10, ["Elf"] = 11, ["Lurikeen"] = 12, ["Inconnu"] = 13, ["Valkyn"] = 14, ["Sylvan"] = 15 };
        foreach (var (race, id) in races)
        {
            Match line = Regex.Match(text, @"^\s*\{ eRace\." + race + @",.*$", RegexOptions.Multiline);
            Assert.That(line.Success, race);
            foreach (Match stat in Regex.Matches(line.Value, @"eStat\.(\w+), (\d+)"))
                Assert.That(CharacterBuilder.StartingStat(id, Enum.Parse<BuilderStat>(stat.Groups[1].Value)), Is.EqualTo(int.Parse(stat.Groups[2].Value)), race + " " + stat.Groups[1].Value);
        }
    }

    private void Exec(string sql)
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void SeedDruid()
    {
        Exec("""
            CREATE TABLE ClassXSpecialization (ClassXSpecializationID INTEGER PRIMARY KEY, ClassID INT, SpecKeyName TEXT, LevelAcquired INT);
            CREATE TABLE Specialization (KeyName TEXT, Name TEXT, Implementation TEXT);
            CREATE TABLE Style (StyleID INTEGER PRIMARY KEY, ClassId INT, Name TEXT, SpecKeyName TEXT, SpecLevelRequirement INT);
            CREATE TABLE SpecXAbility (Spec TEXT, SpecLevel INT, AbilityKey TEXT, AbilityLevel INT, ClassId INT);
            CREATE TABLE Ability (KeyName TEXT, Name TEXT);
            CREATE TABLE SpellLine (SpellLineID INTEGER PRIMARY KEY, KeyName TEXT, Name TEXT, Spec TEXT, IsBaseLine INT, ClassIDHint INT);
            CREATE TABLE LineXSpell (LineName TEXT, SpellID INT, Level INT);
            CREATE TABLE Spell (SpellID INT, Name TEXT, Type TEXT, Value REAL, Damage REAL);
            INSERT INTO ClassXSpecialization VALUES (1, 47, 'NaturalistCareer', -1000), (2, 47, 'Nurture', -10), (3, 47, 'Regrowth', -9),
                (4, 47, 'Nature', -8), (5, 47, 'Realm Abilities', -65535), (6, 47, 'Parry', 15);
            INSERT INTO Specialization VALUES ('NaturalistCareer', 'NaturalistCareer', 'DOL.GS.LiveCareerSpecialization'),
                ('Nurture', 'Nurture', 'DOL.GS.LiveSpellHybridSpecialization'), ('Regrowth', 'Regrowth', 'DOL.GS.LiveSpellHybridSpecialization'),
                ('Nature', 'Nature', 'DOL.GS.LiveSpellHybridSpecialization'), ('Realm Abilities', 'Realm Abilities', 'DOL.GS.LiveRealmAbilitiesSpecialization'),
                ('Parry', 'Parry', NULL);
            INSERT INTO SpecXAbility VALUES ('Parry', 1, 'Parry', 1, 0), ('Parry', 1, 'Shadow trick', 1, 9);
            INSERT INTO Ability VALUES ('Parry', 'Parry');
            INSERT INTO SpellLine VALUES (1, 'Nurture', 'Nurture', 'Nurture', 1, 0), (2, 'Druid Nurture Spec', 'Nurture', 'Nurture', 0, 47),
                (3, 'Bard Nurture Spec', 'Nurture', 'Nurture', 0, 48), (4, 'Druid Nature Magic', 'Nature', 'Nature', 1, 0), (5, 'Druid Nature Spec', 'Nature', 'Nature', 0, 0);
            INSERT INTO LineXSpell VALUES ('Nurture', 1, 2), ('Druid Nurture Spec', 2, 5), ('Bard Nurture Spec', 3, 5), ('Druid Nature Magic', 4, 1), ('Druid Nature Spec', 5, 10);
            INSERT INTO Spell VALUES (1, 'Base Str', 'StrengthBuff', 12, 0), (2, 'Strength of the Tree', 'StrengthConstitutionBuff', 21, 0),
                (3, 'Bard Spec Buff', 'StrengthConstitutionBuff', 20, 0), (4, 'Summon Ally', 'SummonCommander', 0, 0), (5, 'Sun Bolt', 'DirectDamage', 0, 33);
            """);
    }

    [Test] public void ReadsTheClassLinesAndUnlocksLikeTheServer()
    {
        SeedDruid();
        BuilderClassData data = CharacterBuilderDatabase.Load(_database, 47);
        Assert.That(data.Specs.Select(s => s.Key), Is.EqualTo(new[] { "Nurture", "Regrowth", "Nature", "Parry" }), "careers and realm abilities are not trainable");
        Assert.That(data.Specs.Single(s => s.Key == "Parry").LevelAcquired, Is.EqualTo(15));
        Assert.That(data.Specs.Single(s => s.Key == "Nurture").LevelAcquired, Is.EqualTo(1));

        var nurture = data.Unlocks.Where(u => u.SpecKey == "Nurture").ToList();
        Assert.That(nurture.Select(u => u.Name), Is.EquivalentTo(new[] { "Base Str", "Strength of the Tree" }), "the class hint wins over another class's line");
        Assert.That(nurture.Single(u => u.Name == "Base Str").Baseline, Is.True);
        Assert.That(nurture.Single(u => u.Name == "Strength of the Tree").Baseline, Is.False);
        Assert.That(data.Unlocks.Single(u => u.Name == "Sun Bolt").Detail, Does.Contain("33"), "a line without a hint is used when no hinted line exists");
        Assert.That(data.Unlocks.Where(u => u.SpecKey == "Parry").Select(u => u.Name), Is.EqualTo(new[] { "Parry 1" }), "another class's ability is left out");
    }

    [Test] public void SavedBuildsRoundTrip()
    {
        var plan = new CharacterBuildPlan
        {
            Name = "Pure: Nurture/45?", ClassId = 47, RaceId = 9, Level = 45, HalfLevel = true,
            CreationPoints = { ["EMP"] = 15, ["CON"] = 5 }, SpecLevels = { ["Nurture"] = 45 },
        };
        string path = plan.Save(_folder);
        Assert.That(Path.GetFileName(path), Does.Not.Contain(":").And.Not.Contain("?").And.Not.Contain("/"));
        Assert.That(CharacterBuildPlan.List(_folder), Has.Count.EqualTo(1));
        CharacterBuildPlan? loaded = CharacterBuildPlan.Load(path);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Name, Is.EqualTo(plan.Name));
        Assert.That(loaded.HalfLevel, Is.True);
        Assert.That(loaded.CreationPoints["EMP"], Is.EqualTo(15));
        Assert.That(loaded.SpecLevels["Nurture"], Is.EqualTo(45));
        File.WriteAllText(Path.Combine(CharacterBuildPlan.Folder(_folder), "broken.json"), "{ not json");
        Assert.That(CharacterBuildPlan.Load(Path.Combine(CharacterBuildPlan.Folder(_folder), "broken.json")), Is.Null);
    }

    /// <summary>A real render of the builder for the launcher-ui-preview artifact, with the seeded druid.</summary>
    [Test, Apartment(ApartmentState.STA)] public void RendersTheBuilderPreview()
    {
        SeedDruid();
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-preview");
        Directory.CreateDirectory(folder);
        var launcher = Assembly.Load("OfflineDAoC");
        var type = launcher.GetType("OfflineDaoc.Launcher.CharacterBuilderControl")!;
        using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20_000, -20_000),
            ClientSize = new Size(1180, 640), BackColor = Color.FromArgb(35, 31, 26) };
        var control = (Control)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            [_database, _folder, true], null)!;
        host.Controls.Add(control);
        host.Show();
        var realm = (ComboBox)All(control).OfType<ComboBox>().First();
        var classes = All(control).OfType<ComboBox>().ElementAt(1);
        realm.SelectedIndex = 2; // Hibernia
        classes.SelectedIndex = classes.Items.Cast<object>().ToList().FindIndex(o => o.ToString() == "Druid");
        var grids = All(control).OfType<DataGridView>().ToList();
        DateTime until = DateTime.UtcNow.AddSeconds(10);
        while (grids[0].Rows.Count == 0 && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
        Assert.That(grids[0].Rows.Count, Is.EqualTo(4), "the druid's four trainable lines are listed");
        grids[0].Rows[0].Cells["Level"].Value = 30;
        Application.DoEvents();
        Assert.That(grids[0].Rows[0].Cells["Points"].Value, Is.EqualTo(CharacterBuilder.SpecCost(30)));
        string summary = (string)type.GetMethod("Summary", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null)!;
        Assert.That(summary, Does.Contain("Druid").And.Contain("Nurture 30"));
        using var image = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        image.Save(Path.Combine(folder, "7-character-builder.png"));
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control grandchild in All(child))
                yield return grandchild;
        }
    }
}

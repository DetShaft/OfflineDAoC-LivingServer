using System.Data.SQLite;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Launcher;

/// <summary>The eight base stats, in the order the game's character sheet lists them.</summary>
public enum BuilderStat { STR, CON, DEX, QUI, INT, PIE, EMP, CHR, UNDEFINED }

/// <summary>
/// One playable class as the server defines it (source/server/GameServer/playerclasses/*): its spec point multiplier,
/// the stats it raises from level 6, its power stat, base hit points and the lines it autotrains.
/// CharacterBuilderTests.ClassTableMatchesTheServerSources checks this table against those files.
/// </summary>
public sealed record BuilderClass(
    int Id, string Name, int Realm, int SpecMultiplier, BuilderStat Primary, BuilderStat Secondary, BuilderStat Tertiary,
    BuilderStat ManaStat, int BaseHp, IReadOnlyList<string> Autotrain, IReadOnlyList<(int Id, string Name)> Races);

/// <summary>
/// Issue #69: the launcher's character planner. The rules are the server's own (classic-era OpenDAoC, client 1.127):
/// - starting stats: GlobalConstants.STARTING_STATS_DICT;
/// - the 30 creation points and their cost (1 point each up to +10, 2 up to +15, 3 beyond): CharacterStatValidator;
/// - stats from level 6 (primary every level, secondary every 2nd, tertiary every 3rd): CharacterStatValidator;
/// - spec points per level and half level: GamePlayer.VerifySpecPoints;
/// - the cost of a spec level (raising a line to N costs N) and autotrain refunds: GamePlayer.GetAutoTrainPoints.
/// It only plans: nothing here creates or changes a character.
/// </summary>
public static class CharacterBuilder
{
    public const int CreationPointBudget = 30;
    public const int MaxLevel = 50;

    public static readonly BuilderStat[] Stats =
        [BuilderStat.STR, BuilderStat.CON, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, BuilderStat.PIE, BuilderStat.EMP, BuilderStat.CHR];

    public static readonly string[] RealmNames = ["", "Albion", "Midgard", "Hibernia"];

    // GlobalConstants.STARTING_STATS_DICT, in Stats order (STR CON DEX QUI INT PIE EMP CHR).
    private static readonly Dictionary<int, int[]> RaceStats = new()
    {
        [1] = [60, 60, 60, 60, 60, 60, 60, 60],   // Briton
        [2] = [45, 45, 60, 70, 80, 60, 60, 60],   // Avalonian
        [3] = [70, 70, 50, 50, 60, 60, 60, 60],   // Highlander
        [4] = [50, 50, 80, 60, 60, 60, 60, 60],   // Saracen
        [5] = [70, 70, 50, 50, 60, 60, 60, 60],   // Norseman
        [6] = [100, 70, 35, 35, 60, 60, 60, 60],  // Troll
        [7] = [60, 80, 50, 50, 60, 60, 60, 60],   // Dwarf
        [8] = [50, 50, 70, 70, 60, 60, 60, 60],   // Kobold
        [9] = [60, 60, 60, 60, 60, 60, 60, 60],   // Celt
        [10] = [90, 60, 40, 40, 60, 60, 70, 60],  // Firbolg
        [11] = [40, 40, 75, 75, 70, 60, 60, 60],  // Elf
        [12] = [40, 40, 80, 80, 60, 60, 60, 60],  // Lurikeen
        [13] = [50, 60, 70, 50, 70, 60, 60, 60],  // Inconnu
        [14] = [55, 45, 65, 75, 60, 60, 60, 60],  // Valkyn
        [15] = [70, 60, 55, 45, 70, 60, 60, 60],  // Sylvan
    };

    private static readonly (int Id, string Name, int Multiplier, BuilderStat Primary, BuilderStat Secondary, BuilderStat Tertiary, BuilderStat Mana, int Hp, string[] Autotrain)[] ClassRules =
    [
        (1, "Paladin", 20, BuilderStat.CON, BuilderStat.PIE, BuilderStat.STR, BuilderStat.PIE, 760, new[] { "Slash", "Chants" }),
        (2, "Armsman", 20, BuilderStat.STR, BuilderStat.CON, BuilderStat.DEX, BuilderStat.UNDEFINED, 880, new[] { "Slash", "Thrust" }),
        (3, "Scout", 20, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.STR, BuilderStat.DEX, 720, new[] { "Archery", "Longbows" }),
        (4, "Minstrel", 15, BuilderStat.CHR, BuilderStat.DEX, BuilderStat.STR, BuilderStat.CHR, 720, new[] { "Instruments" }),
        (5, "Theurgist", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (6, "Cleric", 10, BuilderStat.PIE, BuilderStat.CON, BuilderStat.STR, BuilderStat.PIE, 720, Array.Empty<string>()),
        (7, "Wizard", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (8, "Sorcerer", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (9, "Infiltrator", 25, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.STR, BuilderStat.UNDEFINED, 720, new[] { "Stealth" }),
        (10, "Friar", 15, BuilderStat.PIE, BuilderStat.CON, BuilderStat.STR, BuilderStat.PIE, 720, Array.Empty<string>()),
        (11, "Mercenary", 20, BuilderStat.STR, BuilderStat.DEX, BuilderStat.CON, BuilderStat.UNDEFINED, 880, new[] { "Slash", "Thrust" }),
        (12, "Necromancer", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (13, "Cabalist", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (19, "Reaver", 20, BuilderStat.STR, BuilderStat.DEX, BuilderStat.PIE, BuilderStat.PIE, 760, new[] { "Slash", "Flexible" }),
        (21, "Thane", 20, BuilderStat.STR, BuilderStat.PIE, BuilderStat.CON, BuilderStat.PIE, 720, Array.Empty<string>()),
        (22, "Warrior", 20, BuilderStat.STR, BuilderStat.CON, BuilderStat.DEX, BuilderStat.UNDEFINED, 880, new[] { "Axe", "Hammer", "Sword" }),
        (23, "Shadowblade", 22, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.STR, BuilderStat.UNDEFINED, 760, new[] { "Stealth" }),
        (24, "Skald", 15, BuilderStat.CHR, BuilderStat.STR, BuilderStat.CON, BuilderStat.CHR, 760, Array.Empty<string>()),
        (25, "Hunter", 20, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.STR, BuilderStat.DEX, 720, new[] { "Archery", "Composite Bow" }),
        (26, "Healer", 10, BuilderStat.PIE, BuilderStat.CON, BuilderStat.STR, BuilderStat.PIE, 720, Array.Empty<string>()),
        (27, "Spiritmaster", 10, BuilderStat.PIE, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.PIE, 560, Array.Empty<string>()),
        (28, "Shaman", 10, BuilderStat.PIE, BuilderStat.CON, BuilderStat.STR, BuilderStat.PIE, 720, Array.Empty<string>()),
        (29, "Runemaster", 10, BuilderStat.PIE, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.PIE, 560, Array.Empty<string>()),
        (30, "Bonedancer", 10, BuilderStat.PIE, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.PIE, 560, Array.Empty<string>()),
        (31, "Berserker", 20, BuilderStat.STR, BuilderStat.DEX, BuilderStat.CON, BuilderStat.UNDEFINED, 880, Array.Empty<string>()),
        (32, "Savage", 15, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.STR, BuilderStat.UNDEFINED, 880, Array.Empty<string>()),
        (40, "Eldritch", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (41, "Enchanter", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (42, "Mentalist", 10, BuilderStat.INT, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.INT, 560, Array.Empty<string>()),
        (43, "Blademaster", 20, BuilderStat.STR, BuilderStat.DEX, BuilderStat.CON, BuilderStat.UNDEFINED, 880, Array.Empty<string>()),
        (44, "Hero", 20, BuilderStat.STR, BuilderStat.CON, BuilderStat.DEX, BuilderStat.UNDEFINED, 880, Array.Empty<string>()),
        (45, "Champion", 20, BuilderStat.STR, BuilderStat.INT, BuilderStat.DEX, BuilderStat.INT, 760, Array.Empty<string>()),
        (46, "Warden", 15, BuilderStat.EMP, BuilderStat.STR, BuilderStat.CON, BuilderStat.EMP, 720, Array.Empty<string>()),
        (47, "Druid", 10, BuilderStat.EMP, BuilderStat.CON, BuilderStat.STR, BuilderStat.EMP, 720, Array.Empty<string>()),
        (48, "Bard", 15, BuilderStat.CHR, BuilderStat.EMP, BuilderStat.CON, BuilderStat.CHR, 720, Array.Empty<string>()),
        (49, "Nightshade", 22, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.STR, BuilderStat.DEX, 720, new[] { "Stealth" }),
        (50, "Ranger", 20, BuilderStat.DEX, BuilderStat.QUI, BuilderStat.STR, BuilderStat.DEX, 720, new[] { "Archery", "Recurve Bow" }),
        (55, "Animist", 10, BuilderStat.INT, BuilderStat.CON, BuilderStat.DEX, BuilderStat.INT, 560, Array.Empty<string>()),
        (56, "Valewalker", 15, BuilderStat.STR, BuilderStat.INT, BuilderStat.CON, BuilderStat.INT, 720, Array.Empty<string>()),
        (63, "Sluaghbinder", 10, BuilderStat.STR, BuilderStat.INT, BuilderStat.CON, BuilderStat.INT, 760, Array.Empty<string>()),
    ];

    private static readonly Lazy<IReadOnlyList<BuilderClass>> AllClasses = new(() =>
    {
        var list = new List<BuilderClass>();
        foreach (int realm in new[] { 1, 2, 3 })
        foreach ((int id, _) in BotCharacterGenerator.ClassesForRealm(realm, allowSluaghbinder: true))
        {
            var rule = ClassRules.First(r => r.Id == id);
            list.Add(new BuilderClass(rule.Id, rule.Name, realm, rule.Multiplier, rule.Primary, rule.Secondary, rule.Tertiary,
                rule.Mana, rule.Hp, rule.Autotrain, BotCharacterGenerator.RacesForClass(id)));
        }
        return list;
    });

    /// <summary>Every playable class (the launcher's bot roster, which follows the server's eligible races).</summary>
    public static IReadOnlyList<BuilderClass> Classes => AllClasses.Value;

    public static IEnumerable<BuilderClass> ClassesForRealm(int realm, bool allowSluaghbinder) =>
        Classes.Where(c => c.Realm == realm && (allowSluaghbinder || c.Id != 63));

    public static BuilderClass? FindClass(int id) => Classes.FirstOrDefault(c => c.Id == id);

    public static int StartingStat(int raceId, BuilderStat stat) =>
        RaceStats.TryGetValue(raceId, out int[]? values) && stat != BuilderStat.UNDEFINED ? values[(int)stat] : 60;

    /// <summary>What the levels from 6 to <paramref name="level"/> add to a stat (CharacterStatValidator's loop).</summary>
    public static int LevelStatGain(BuilderClass cls, BuilderStat stat, int level)
    {
        int gain = 0;
        for (int lvl = level; lvl > 5; lvl--)
        {
            if (cls.Primary == stat)
                gain++;
            if (cls.Secondary == stat && (lvl - 6) % 2 == 0)
                gain++;
            if (cls.Tertiary == stat && (lvl - 6) % 3 == 0)
                gain++;
        }
        return gain;
    }

    /// <summary>Creation points spent to raise one stat by <paramref name="above"/> over its race value.</summary>
    public static int CreationCost(int above) =>
        above <= 0 ? 0 : above + Math.Max(0, above - 10) + Math.Max(0, above - 15);

    /// <summary>Spec points a character has at a level; <paramref name="halfLevel"/> is the x.5 stage from 40.5 to 49.5.</summary>
    public static int SpecPointsAt(BuilderClass cls, int level, bool halfLevel)
    {
        level = Math.Clamp(level, 1, MaxLevel);
        int points = -1;
        for (int i = 1; i <= level; i++)
        {
            if (i <= 5) points += i;
            if (i > 5) points += cls.SpecMultiplier * i / 10;
            if (i > 40) points += cls.SpecMultiplier * (i - 1) / 20;
        }
        if (halfLevel && level >= 40 && level < MaxLevel)
            points += cls.SpecMultiplier * level / 20;
        return points;
    }

    /// <summary>Points to raise a line from 1 to <paramref name="specLevel"/> (each level N costs N).</summary>
    public static int SpecCost(int specLevel) => specLevel <= 1 ? 0 : (specLevel * (specLevel + 1) - 2) / 2;

    /// <summary>The level an autotrained line reaches for free at a character level (level / 4, at least 1).</summary>
    public static int AutotrainLevel(int level) => Math.Max(1, level / 4);

    public static bool IsAutotrained(BuilderClass cls, string specKey) =>
        cls.Autotrain.Any(a => string.Equals(a, specKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Points a line costs the player: its cost less the autotrain refund (GetAutoTrainPoints mode 0).</summary>
    public static int SpecPointsSpent(BuilderClass cls, string specKey, int specLevel, int level)
    {
        int cost = SpecCost(specLevel);
        if (!IsAutotrained(cls, specKey))
            return cost;
        int free = Math.Min(AutotrainLevel(level), specLevel);
        return cost - (free * (free + 1) - 2) / 2;
    }

    /// <summary>A spec line can be trained up to the character's level.</summary>
    public static int MaxSpecLevel(int level) => Math.Clamp(level, 1, MaxLevel);
}

/// <summary>A trainable line of a class, with the character level that unlocks it (ClassXSpecialization).</summary>
public sealed record BuilderSpec(string Key, string Name, int LevelAcquired);

/// <summary>Something a class gains: at a spec level of a line, or (Baseline) at a character level.</summary>
public sealed record BuilderUnlock(string SpecKey, int Level, string Kind, string Name, string Detail, bool Baseline);

public sealed record BuilderClassData(IReadOnlyList<BuilderSpec> Specs, IReadOnlyList<BuilderUnlock> Unlocks);

/// <summary>
/// Reads a class's lines and what they unlock from the game database, read-only (safe while the server runs). The
/// selection follows the server: ClassXSpecialization for the lines (careers, realm abilities, master levels and
/// champion lines are not trainable), Style by class, SpecXAbility for the class or every class (0), and SpellLine
/// with the class hint, falling back to the lines without one (Specialization.GetSpellLinesForLiving). Baseline
/// spell lines follow the character level; specialization lines follow the line's level.
/// </summary>
public static class CharacterBuilderDatabase
{
    private static readonly string[] UntrainableImplementations =
    [
        "DOL.GS.LiveCareerSpecialization", "DOL.GS.LiveWideCareerSpecialization", "DOL.GS.UntrainableSpecialization",
        "DOL.GS.LiveRealmAbilitiesSpecialization", "DOL.GS.LiveMasterLevelsSpecialization", "DOL.GS.LiveChampionsSpecialization",
    ];

    public static BuilderClassData Load(string database, int classId)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
        connection.Open();
        var specs = new List<BuilderSpec>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT x.SpecKeyName, COALESCE(s.Name, x.SpecKeyName), x.LevelAcquired, COALESCE(s.Implementation, '')
                FROM ClassXSpecialization x LEFT JOIN Specialization s ON s.KeyName = x.SpecKeyName
                WHERE x.ClassID = @class
                ORDER BY x.LevelAcquired, x.ClassXSpecializationID
                """;
            command.Parameters.AddWithValue("@class", classId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                string implementation = reader.GetString(3);
                if (UntrainableImplementations.Contains(implementation) || key.EndsWith("Career", StringComparison.Ordinal))
                    continue;
                if (specs.Any(s => s.Key == key))
                    continue;
                specs.Add(new BuilderSpec(key, reader.GetString(1), Math.Max(1, Convert.ToInt32(reader.GetValue(2)))));
            }
        }

        var unlocks = new List<BuilderUnlock>();
        foreach (BuilderSpec spec in specs)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Name, SpecLevelRequirement FROM Style WHERE SpecKeyName = @spec AND ClassId = @class ORDER BY SpecLevelRequirement, StyleID";
                command.Parameters.AddWithValue("@spec", spec.Key);
                command.Parameters.AddWithValue("@class", classId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    unlocks.Add(new BuilderUnlock(spec.Key, Convert.ToInt32(reader.GetValue(1)), "Style", reader.GetString(0), "", false));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT COALESCE(a.Name, x.AbilityKey), x.SpecLevel, x.AbilityLevel FROM SpecXAbility x
                    LEFT JOIN Ability a ON a.KeyName = x.AbilityKey
                    WHERE x.Spec = @spec AND (x.ClassId = 0 OR x.ClassId = @class) ORDER BY x.SpecLevel
                    """;
                command.Parameters.AddWithValue("@spec", spec.Key);
                command.Parameters.AddWithValue("@class", classId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int abilityLevel = Convert.ToInt32(reader.GetValue(2));
                    unlocks.Add(new BuilderUnlock(spec.Key, Convert.ToInt32(reader.GetValue(1)), "Ability",
                        abilityLevel > 0 ? $"{reader.GetString(0)} {abilityLevel}" : reader.GetString(0), "", false));
                }
            }

            var lines = new List<(string Key, bool Baseline, int Hint)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT KeyName, IsBaseLine, ClassIDHint FROM SpellLine WHERE Spec = @spec ORDER BY SpellLineID";
                command.Parameters.AddWithValue("@spec", spec.Key);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    lines.Add((reader.GetString(0), Convert.ToInt32(reader.GetValue(1)) != 0, Convert.ToInt32(reader.GetValue(2))));
            }
            foreach (bool baseline in new[] { true, false })
            {
                var chosen = lines.Where(l => l.Baseline == baseline && l.Hint == classId).ToList();
                if (chosen.Count == 0)
                    chosen = lines.Where(l => l.Baseline == baseline && l.Hint == 0).ToList();
                foreach (var line in chosen)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = """
                        SELECT s.Name, x.Level, s.Type, s.Value, s.Damage FROM LineXSpell x JOIN Spell s ON s.SpellID = x.SpellID
                        WHERE x.LineName = @line ORDER BY x.Level, s.SpellID
                        """;
                    command.Parameters.AddWithValue("@line", line.Key);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        unlocks.Add(new BuilderUnlock(spec.Key, Convert.ToInt32(reader.GetValue(1)), "Spell", reader.GetString(0),
                            SpellDetail(reader.IsDBNull(2) ? "" : reader.GetString(2), ToDouble(reader.GetValue(3)), ToDouble(reader.GetValue(4))), baseline));
                }
            }
        }
        return new BuilderClassData(specs, unlocks);
    }

    private static double ToDouble(object value) => value is DBNull ? 0 : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

    private static string SpellDetail(string type, double value, double damage)
    {
        string text = type;
        if (damage > 0)
            text += $" ({damage:0.#} dmg)";
        else if (value > 0)
            text += $" ({value:0.#})";
        return text;
    }
}

/// <summary>A saved plan: runtime\character-builds\&lt;name&gt;.json. Nothing in the game reads these files.</summary>
public sealed class CharacterBuildPlan
{
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int RaceId { get; set; }
    public int Level { get; set; } = CharacterBuilder.MaxLevel;
    public bool HalfLevel { get; set; }
    public Dictionary<string, int> CreationPoints { get; set; } = new();
    public Dictionary<string, int> SpecLevels { get; set; } = new();
    public string Notes { get; set; } = "";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string Folder(string runtimeRoot) => Path.Combine(runtimeRoot, "character-builds");

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return string.IsNullOrEmpty(cleaned) ? "build" : cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    public string Save(string runtimeRoot)
    {
        string folder = Folder(runtimeRoot);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, SafeFileName(Name) + ".json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    public static CharacterBuildPlan? Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<CharacterBuildPlan>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> List(string runtimeRoot)
    {
        string folder = Folder(runtimeRoot);
        if (!Directory.Exists(folder))
            return [];
        return Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

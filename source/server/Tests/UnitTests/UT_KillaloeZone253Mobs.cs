using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// OpenDAoC Killaloe (region 237) field spawns stored as region 253 mobs, retail offset 64,64
    /// (524288) removed from X and Y. Tests the git SQL seed only; not an in-game check.
    /// </summary>
    [TestFixture]
    public class UT_KillaloeZone253Mobs
    {
        private const int ExpectedCount = 74;
        private const int Region = 253;
        private const int Offset = 64 * 8192;
        private const int MaxCoord = 65536;
        private static readonly string[] ExpectedNames =
        {
            "boogey spawn", "forlorn spectre", "clicking beetle", "large rat", "forest cat", "Shadow in the Trees",
        };

        [Test]
        public void SeedHasSeventyFourKillaloeMobsInsideTheDroppedOffset()
        {
            Assert.That(Offset, Is.EqualTo(524288));
            IReadOnlyList<Row> rows = LoadSeed();
            Assert.That(rows, Has.Count.EqualTo(ExpectedCount));
            Assert.That(rows.Select(row => row.MobId), Is.Unique);
            Assert.That(rows.Select(row => row.Name).Distinct().OrderBy(name => name),
                Is.EqualTo(ExpectedNames.OrderBy(name => name)));
            Assert.That(rows.Count(row => row.Name == "Shadow in the Trees"), Is.EqualTo(1));
            Assert.That(rows.All(row =>
                row.Region == Region &&
                row.X is >= 0 and <= MaxCoord &&
                row.Y is >= 0 and <= MaxCoord &&
                row.ClassType == "DOL.GS.GameNPC" &&
                row.PackageId == "offline-killaloe-zone253"), Is.True);

            Row spectre = rows.Single(row => row.MobId == "100032783");
            Assert.That(spectre.Name, Is.EqualTo("forlorn spectre"));
            Assert.That(spectre.X, Is.EqualTo(565529 - Offset));
            Assert.That(spectre.Y, Is.EqualTo(551832 - Offset));
            Assert.That(spectre.Z, Is.EqualTo(8180));
            Assert.That(spectre.Heading, Is.EqualTo(2724));
            Assert.That(spectre.Level, Is.EqualTo(20));
            Assert.That(spectre.Model, Is.EqualTo(2135));

            Row shadow = rows.Single(row => row.Name == "Shadow in the Trees");
            Assert.That(shadow.MobId, Is.EqualTo("100032839"));
            Assert.That(shadow.X, Is.EqualTo(582396 - Offset));
            Assert.That(shadow.Y, Is.EqualTo(569120 - Offset));
            Assert.That(shadow.Z, Is.EqualTo(8986));
            Assert.That(shadow.Heading, Is.EqualTo(1647));
            Assert.That(shadow.Level, Is.EqualTo(22));
            Assert.That(shadow.Model, Is.EqualTo(100));
        }

        [Test]
        public void SeedInsertsIntoAnEmptyMobTable()
        {
            IReadOnlyList<Row> rows = LoadSeed();
            string path = Path.Combine(Path.GetTempPath(), "ut-killaloe-zone253-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var connection = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False");
                connection.Open();
                using (var create = connection.CreateCommand())
                {
                    create.CommandText = """
                        CREATE TABLE Mob (
                            ClassType TEXT, TranslationId TEXT, Name TEXT NOT NULL, Suffix TEXT, Guild TEXT,
                            ExamineArticle TEXT, MessageArticle TEXT, X INTEGER NOT NULL, Y INTEGER NOT NULL,
                            Z INTEGER NOT NULL, Speed INTEGER, Heading INTEGER, Region INTEGER, Model INTEGER,
                            Size INTEGER, Strength INTEGER, Constitution INTEGER, Dexterity INTEGER,
                            Quickness INTEGER, Intelligence INTEGER, Piety INTEGER, Empathy INTEGER,
                            Charisma INTEGER, Level INTEGER, Realm INTEGER, EquipmentTemplateID TEXT,
                            ItemsListTemplateID TEXT, NPCTemplateID INTEGER, Race INTEGER, Flags INTEGER,
                            AggroLevel INTEGER, AggroRange INTEGER, MeleeDamageType INTEGER,
                            RespawnInterval INTEGER, FactionID INTEGER, BodyType INTEGER, HouseNumber INTEGER,
                            Brain TEXT, PathID TEXT, OwnerID TEXT, RoamingRange INTEGER, IsCloakHoodUp INTEGER,
                            Gender INTEGER, PackageID TEXT, VisibleWeaponSlots INTEGER, LastTimeRowUpdated TEXT,
                            Mob_ID TEXT PRIMARY KEY
                        );
                        """;
                    create.ExecuteNonQuery();
                }

                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = """
                        INSERT INTO Mob (Name, X, Y, Z, Heading, Region, Model, Level, ClassType, PackageID, Mob_ID)
                        VALUES (@name, @x, @y, @z, @heading, @region, @model, @level, @class, @package, @id)
                        """;
                    foreach (string parameter in new[] { "@name", "@class", "@package", "@id" })
                        insert.Parameters.Add(parameter, System.Data.DbType.String);
                    foreach (string parameter in new[] { "@x", "@y", "@z", "@heading", "@region", "@model", "@level" })
                        insert.Parameters.Add(parameter, System.Data.DbType.Int32);

                    foreach (Row row in rows)
                    {
                        insert.Parameters["@name"].Value = row.Name;
                        insert.Parameters["@x"].Value = row.X;
                        insert.Parameters["@y"].Value = row.Y;
                        insert.Parameters["@z"].Value = row.Z;
                        insert.Parameters["@heading"].Value = row.Heading;
                        insert.Parameters["@region"].Value = row.Region;
                        insert.Parameters["@model"].Value = row.Model;
                        insert.Parameters["@level"].Value = row.Level;
                        insert.Parameters["@class"].Value = row.ClassType;
                        insert.Parameters["@package"].Value = row.PackageId;
                        insert.Parameters["@id"].Value = row.MobId;
                        insert.ExecuteNonQuery();
                    }
                }

                using var verify = connection.CreateCommand();
                verify.CommandText = """
                    SELECT COUNT(*),
                           SUM(CASE WHEN Region=253 AND X BETWEEN 0 AND 65536 AND Y BETWEEN 0 AND 65536 THEN 1 ELSE 0 END),
                           SUM(CASE WHEN Name='Shadow in the Trees' THEN 1 ELSE 0 END)
                    FROM Mob WHERE PackageID='offline-killaloe-zone253'
                    """;
                using var reader = verify.ExecuteReader();
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(ExpectedCount));
                Assert.That(reader.GetInt32(1), Is.EqualTo(ExpectedCount));
                Assert.That(reader.GetInt32(2), Is.EqualTo(1));
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        private sealed record Row(string MobId, string Name, string ClassType, string PackageId,
            int X, int Y, int Z, int Heading, int Region, int Model, int Level);

        private static IReadOnlyList<Row> LoadSeed()
        {
            string path = FindSeed();
            string text = File.ReadAllText(path, Encoding.UTF8);
            int valuesAt = text.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
            Assert.That(valuesAt, Is.GreaterThan(0), path);
            var rows = new List<Row>();
            foreach (List<string> tokens in ParseRows(text[(valuesAt + "VALUES".Length)..]))
            {
                Assert.That(tokens, Has.Count.EqualTo(47), "OpenDAoC mob.sql column count");
                rows.Add(new Row(
                    Unquote(tokens[46]), Unquote(tokens[2]), Unquote(tokens[0]), Unquote(tokens[43]),
                    int.Parse(tokens[7]), int.Parse(tokens[8]), int.Parse(tokens[9]), int.Parse(tokens[11]),
                    int.Parse(tokens[12]), int.Parse(tokens[13]), int.Parse(tokens[23])));
            }
            return rows;
        }

        private static string FindSeed()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                foreach (string relative in new[]
                {
                    Path.Combine("source", "tools", "OfflineDaoc.Setup", "data", "killaloe_zone253_mobs.sql"),
                    Path.Combine("tools", "OfflineDaoc.Setup", "data", "killaloe_zone253_mobs.sql"),
                    Path.Combine("OfflineDaoc.Setup", "data", "killaloe_zone253_mobs.sql"),
                    Path.Combine("data", "killaloe_zone253_mobs.sql"),
                })
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            Assert.Fail("killaloe_zone253_mobs.sql not found");
            return null;
        }

        private static IEnumerable<List<string>> ParseRows(string values)
        {
            int index = 0;
            while (index < values.Length)
            {
                while (index < values.Length && (char.IsWhiteSpace(values[index]) || values[index] == ',' || values[index] == ';')) index++;
                if (index >= values.Length) yield break;
                if (values[index] != '(') throw new FormatException("Expected a SQL value row.");
                index++;
                var row = new List<string>();
                var token = new StringBuilder();
                bool quoted = false;
                bool escaped = false;
                while (index < values.Length)
                {
                    char character = values[index++];
                    if (quoted)
                    {
                        token.Append(character);
                        if (escaped) escaped = false;
                        else if (character == '\\') escaped = true;
                        else if (character == '\'') quoted = false;
                        continue;
                    }
                    if (character == '\'')
                    {
                        quoted = true;
                        token.Append(character);
                    }
                    else if (character == ',')
                    {
                        row.Add(token.ToString().Trim());
                        token.Clear();
                    }
                    else if (character == ')')
                    {
                        row.Add(token.ToString().Trim());
                        yield return row;
                        break;
                    }
                    else
                    {
                        token.Append(character);
                    }
                }
            }
        }

        private static string Unquote(string token)
        {
            if (token.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return null;
            if (token.Length >= 2 && token[0] == '\'' && token[^1] == '\'')
                return token[1..^1].Replace("\\'", "'", StringComparison.Ordinal);
            return token;
        }
    }
}

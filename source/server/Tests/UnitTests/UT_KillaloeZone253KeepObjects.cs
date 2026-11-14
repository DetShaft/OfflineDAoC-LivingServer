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
    /// Live-captured Killaloe keep NPCs and doors stored as region 253. Local X/Y already
    /// have retail offset 64,64 (524288) removed. Region 253 Keep rows are moved onto those
    /// spots; Abermenai Door InternalIDs are dropped. Tests the git SQL seeds only; not an in-game check.
    /// </summary>
    [TestFixture]
    public class UT_KillaloeZone253KeepObjects
    {
        private const int ExpectedNpcCount = 105;
        private const int ExpectedDoorCount = 17;
        private const int Region = 253;
        private const int MaxCoord = 65536;
        private const string NpcPackage = "offline-killaloe-keep-253";

        [Test]
        public void KeepNpcSeedHasCapturedKillaloeStaffInsideRegion253()
        {
            IReadOnlyList<NpcRow> rows = LoadNpcSeed();
            Assert.That(rows, Has.Count.EqualTo(ExpectedNpcCount));
            Assert.That(rows.Select(row => row.MobId), Is.Unique);
            Assert.That(rows.All(row =>
                row.Region == Region &&
                row.X is >= 0 and <= MaxCoord &&
                row.Y is >= 0 and <= MaxCoord &&
                row.ClassType == "DOL.GS.GameNPC" &&
                row.PackageId == NpcPackage &&
                row.Realm is >= 1 and <= 3), Is.True);

            Assert.That(rows.Count(row => row.Name == "Bowman Commander"), Is.EqualTo(14));
            Assert.That(rows.Count(row => row.Name == "Master Wizard"), Is.EqualTo(12));
            Assert.That(rows.Count(row => row.Name == "Knight Commander"), Is.EqualTo(9));
            Assert.That(rows.Count(row => row.Name == "Master Hunter"), Is.EqualTo(4));
            Assert.That(rows.Count(row => row.Name == "Master of Runes"), Is.EqualTo(6));
            Assert.That(rows.Count(row => row.Name == "Viking Jarl"), Is.EqualTo(3));
            Assert.That(rows.Count(row => row.Name == "Master Ranger"), Is.EqualTo(4));
            Assert.That(rows.Count(row => row.Name == "Master Eldritch"), Is.EqualTo(4));
            Assert.That(rows.Count(row => row.Name == "Champion"), Is.EqualTo(3));
            Assert.That(rows.Count(row => row.Name == "Huscarl"), Is.EqualTo(4));
            Assert.That(rows.Count(row => row.Name == "Jarl Killaloe"), Is.EqualTo(1));
            Assert.That(rows.Select(row => row.Name), Does.Not.Contain("Hunter"));
            Assert.That(rows.Select(row => row.Name), Does.Not.Contain("teleport spell effect"));
            Assert.That(rows.Select(row => row.Name), Does.Not.Contain("clicking beetle"));

            NpcRow jarl = rows.Single(row => row.Name == "Jarl Killaloe");
            Assert.That(jarl.MobId, Is.EqualTo("offline-killaloe-keep-01d7"));
            Assert.That(jarl.X, Is.EqualTo(32924));
            Assert.That(jarl.Y, Is.EqualTo(31537));
            Assert.That(jarl.Z, Is.EqualTo(9345));
            Assert.That(jarl.Heading, Is.EqualTo(0x05A4));
            Assert.That(jarl.Level, Is.EqualTo(27));
            Assert.That(jarl.Model, Is.EqualTo(0x00BC));
            Assert.That(jarl.Realm, Is.EqualTo(2));

            NpcRow huscarl = rows.Single(row => row.MobId == "offline-killaloe-keep-0170");
            Assert.That(huscarl.Name, Is.EqualTo("Huscarl"));
            Assert.That(huscarl.X, Is.EqualTo(32785));
            Assert.That(huscarl.Y, Is.EqualTo(32232));
            Assert.That(huscarl.Z, Is.EqualTo(8768));
            Assert.That(huscarl.Level, Is.EqualTo(18));
            Assert.That(huscarl.Realm, Is.EqualTo(2));

            Assert.That(rows.Count(row => row.Realm == 1), Is.EqualTo(47));
            Assert.That(rows.Count(row => row.Realm == 2), Is.EqualTo(39));
            Assert.That(rows.Count(row => row.Realm == 3), Is.EqualTo(19));
        }

        [Test]
        public void KeepDoorSeedHasCapturedGatesAndPosternsInZone253()
        {
            IReadOnlyList<DoorRow> rows = LoadDoorSeed();
            Assert.That(rows, Has.Count.EqualTo(ExpectedDoorCount));
            Assert.That(rows.Select(row => row.DoorId), Is.Unique);
            Assert.That(rows.Select(row => row.InternalId), Is.Unique);
            Assert.That(rows.All(row =>
                row.InternalId / 1_000_000 == Region &&
                row.X is >= 0 and <= MaxCoord &&
                row.Y is >= 0 and <= MaxCoord &&
                row.Type == 2), Is.True);

            Assert.That(rows.Count(row => row.Name == "Keep Gate"), Is.EqualTo(4));
            Assert.That(rows.Count(row => row.Name == "Gatehouse Door"), Is.EqualTo(8));
            Assert.That(rows.Count(row => row.Name == "Postern Door" && row.IsPostern), Is.EqualTo(5));
            Assert.That(rows.Count(row => row.IsPostern), Is.EqualTo(5));
            Assert.That(rows.Select(row => row.Name), Does.Not.Contain("Albion Banner"));
            Assert.That(rows.Select(row => row.Name), Does.Not.Contain("Midgard Banner"));
            Assert.That(rows.Select(row => row.Name), Does.Not.Contain("pile of smoldering rubble"));

            DoorRow albGate = rows.Single(row => row.DoorId == "offline-killaloe-door-00b7");
            Assert.That(albGate.Name, Is.EqualTo("Keep Gate"));
            Assert.That(albGate.X, Is.EqualTo(20564));
            Assert.That(albGate.Y, Is.EqualTo(56709));
            Assert.That(albGate.Z, Is.EqualTo(8288));
            Assert.That(albGate.Heading, Is.EqualTo(0x0888));
            Assert.That(albGate.InternalId, Is.EqualTo(253201001));
            Assert.That(albGate.IsPostern, Is.False);

            DoorRow postern = rows.Single(row => row.DoorId == "offline-killaloe-door-01d3");
            Assert.That(postern.Name, Is.EqualTo("Postern Door"));
            Assert.That(postern.X, Is.EqualTo(33291));
            Assert.That(postern.Y, Is.EqualTo(31343));
            Assert.That(postern.Z, Is.EqualTo(9177));
            Assert.That(postern.IsPostern, Is.True);
        }

        [Test]
        public void KeepPadSqlMovesRegion253KeepsAndDropsAbermenaiDoors()
        {
            IReadOnlyList<string> statements = LoadPadStatements();
            Assert.That(statements.Count(row => row.StartsWith("UPDATE Keep", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(4));
            Assert.That(statements.Count(row => row.StartsWith("DELETE FROM Door", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
            Assert.That(statements.Any(row => row.Contains("253203001", StringComparison.Ordinal)), Is.False,
                "Hibernia keep gate was not captured and must not be invented.");

            IReadOnlyList<DoorRow> capturedDoors = LoadDoorSeed();
            string path = Path.Combine(Path.GetTempPath(), "ut-killaloe-pads-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var connection = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False");
                connection.Open();
                using (var create = connection.CreateCommand())
                {
                    create.CommandText = """
                        CREATE TABLE Keep (
                            KeepID INTEGER PRIMARY KEY, Name TEXT, Region INTEGER NOT NULL,
                            X INTEGER NOT NULL, Y INTEGER NOT NULL, Z INTEGER NOT NULL, Heading INTEGER NOT NULL
                        );
                        CREATE TABLE Door (
                            Name TEXT, Type INTEGER NOT NULL, Z INTEGER NOT NULL, Y INTEGER NOT NULL,
                            X INTEGER NOT NULL, Heading INTEGER NOT NULL, InternalID INTEGER NOT NULL,
                            Health INTEGER NOT NULL, Door_ID TEXT PRIMARY KEY, IsPostern INTEGER NOT NULL,
                            State INTEGER NOT NULL
                        );
                        CREATE TABLE Area (
                            Description TEXT, Region INTEGER NOT NULL, X INTEGER NOT NULL, Y INTEGER NOT NULL, Z INTEGER NOT NULL
                        );
                        CREATE TABLE Mob (
                            Name TEXT, X INTEGER NOT NULL, Y INTEGER NOT NULL, Region INTEGER, PackageID TEXT, Mob_ID TEXT PRIMARY KEY
                        );
                        """;
                    create.ExecuteNonQuery();
                }

                using (var insertKeep = connection.CreateCommand())
                {
                    insertKeep.CommandText = """
                        INSERT INTO Keep (KeepID, Name, Region, X, Y, Z, Heading) VALUES
                            (33, 'Dun Abermenai', 253, 33089, 38271, 3720, 2915),
                            (35, 'Hibernia Portal Keep', 253, 18362, 18257, 4320, 3517),
                            (36, 'Midgard Portal Keep', 253, 54053, 24680, 4320, 346),
                            (37, 'Albion Portal Keep', 253, 37301, 52362, 3944, 2035),
                            (14, 'Albion Portal Keep', 252, 37301, 52362, 3944, 2035)
                        """;
                    insertKeep.ExecuteNonQuery();
                }

                using (var insertArea = connection.CreateCommand())
                {
                    insertArea.CommandText = """
                        INSERT INTO Area (Description, Region, X, Y, Z) VALUES
                            ('Albion Portal Keep', 253, 37301, 52362, 3944),
                            ('Midgard Portal Keep', 253, 54053, 24680, 4320),
                            ('Hibernia Portal Keep', 253, 18362, 18257, 4320),
                            ('Dun Abermenai', 253, 33089, 38271, 3720),
                            ('Albion Portal Keep', 252, 37301, 52362, 3944)
                        """;
                    insertArea.ExecuteNonQuery();
                }

                using (var insertOldDoor = connection.CreateCommand())
                {
                    insertOldDoor.CommandText = """
                        INSERT INTO Door (Name, Type, Z, Y, X, Heading, InternalID, Health, Door_ID, IsPostern, State) VALUES
                            ('door', 2, 4010, 51689, 36923, 19, 253000101, 51000, 'abermenai-alb-1', 0, 1),
                            ('door', 2, 4010, 52085, 36927, 2088, 253000102, 51000, 'abermenai-alb-2', 0, 1),
                            ('door', 2, 4296, 25597, 53512, 2331, 253000201, 51000, 'abermenai-mid-1', 0, 1),
                            ('door', 2, 4308, 25241, 53649, 264, 253000202, 51000, 'abermenai-mid-2', 0, 1),
                            ('door', 2, 3737, 39604, 33849, 1864, 253000301, 2545, 'abermenai-center-1', 0, 0),
                            ('door', 2, 3720, 39059, 33659, 3901, 253000302, 2545, 'abermenai-center-2', 0, 0),
                            ('door', 2, 4318, 18865, 19257, 1469, 253041601, 51000, 'abermenai-hib-1', 0, 1),
                            ('door', 2, 4320, 18470, 18807, 3591, 253041602, 51000, 'abermenai-hib-2', 0, 1)
                        """;
                    insertOldDoor.ExecuteNonQuery();
                }

                using (var insertCaptured = connection.CreateCommand())
                {
                    insertCaptured.CommandText = """
                        INSERT INTO Door (Name, Type, Z, Y, X, Heading, InternalID, Health, Door_ID, IsPostern, State)
                        VALUES (@name, @type, @z, @y, @x, @heading, @internal, @health, @id, @postern, @state)
                        """;
                    insertCaptured.Parameters.Add("@name", System.Data.DbType.String);
                    insertCaptured.Parameters.Add("@id", System.Data.DbType.String);
                    foreach (string parameter in new[] { "@type", "@z", "@y", "@x", "@heading", "@internal", "@health", "@postern", "@state" })
                        insertCaptured.Parameters.Add(parameter, System.Data.DbType.Int32);
                    foreach (DoorRow row in capturedDoors)
                    {
                        insertCaptured.Parameters["@name"].Value = row.Name;
                        insertCaptured.Parameters["@type"].Value = row.Type;
                        insertCaptured.Parameters["@z"].Value = row.Z;
                        insertCaptured.Parameters["@y"].Value = row.Y;
                        insertCaptured.Parameters["@x"].Value = row.X;
                        insertCaptured.Parameters["@heading"].Value = row.Heading;
                        insertCaptured.Parameters["@internal"].Value = row.InternalId;
                        insertCaptured.Parameters["@health"].Value = row.Health;
                        insertCaptured.Parameters["@id"].Value = row.DoorId;
                        insertCaptured.Parameters["@postern"].Value = row.IsPostern ? 1 : 0;
                        insertCaptured.Parameters["@state"].Value = row.State;
                        insertCaptured.ExecuteNonQuery();
                    }
                }

                using (var insertJarl = connection.CreateCommand())
                {
                    insertJarl.CommandText = """
                        INSERT INTO Mob (Name, X, Y, Region, PackageID, Mob_ID)
                        VALUES ('Jarl Killaloe', 32924, 31537, 253, 'offline-killaloe-keep-253', 'offline-killaloe-keep-01d7')
                        """;
                    insertJarl.ExecuteNonQuery();
                }

                foreach (string statement in statements)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }

                using var verify = connection.CreateCommand();
                verify.CommandText = """
                    SELECT
                        (SELECT X FROM Keep WHERE KeepID=37),
                        (SELECT Y FROM Keep WHERE KeepID=37),
                        (SELECT Z FROM Keep WHERE KeepID=37),
                        (SELECT Heading FROM Keep WHERE KeepID=37),
                        (SELECT X FROM Keep WHERE KeepID=36),
                        (SELECT Y FROM Keep WHERE KeepID=36),
                        (SELECT X FROM Keep WHERE KeepID=35),
                        (SELECT Y FROM Keep WHERE KeepID=35),
                        (SELECT X FROM Keep WHERE KeepID=33),
                        (SELECT Y FROM Keep WHERE KeepID=33),
                        (SELECT Z FROM Keep WHERE KeepID=33),
                        (SELECT X FROM Keep WHERE KeepID=14),
                        (SELECT Y FROM Keep WHERE KeepID=14),
                        (SELECT COUNT(*) FROM Door WHERE InternalID IN
                            (253000101, 253000102, 253000201, 253000202, 253000301, 253000302, 253041601, 253041602)),
                        (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%'),
                        (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%' AND Name='Keep Gate'
                            AND InternalID BETWEEN 253203000 AND 253203999),
                        (SELECT X FROM Area WHERE Region=253 AND Description='Albion Portal Keep'),
                        (SELECT Y FROM Area WHERE Region=253 AND Description='Albion Portal Keep'),
                        (SELECT X FROM Area WHERE Region=252 AND Description='Albion Portal Keep'),
                        (SELECT X FROM Mob WHERE Mob_ID='offline-killaloe-keep-01d7'),
                        (SELECT Y FROM Mob WHERE Mob_ID='offline-killaloe-keep-01d7'),
                        (SELECT COUNT(*) FROM Keep WHERE Region=253 AND (
                            (X=37301 AND Y=52362) OR (X=54053 AND Y=24680) OR
                            (X=18362 AND Y=18257) OR (X=33089 AND Y=38271)))
                    """;
                using var reader = verify.ExecuteReader();
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(20536));
                Assert.That(reader.GetInt32(1), Is.EqualTo(57944));
                Assert.That(reader.GetInt32(2), Is.EqualTo(8288));
                Assert.That(reader.GetInt32(3), Is.EqualTo(2035), "Heading stays the imported keep template.");
                Assert.That(reader.GetInt32(4), Is.EqualTo(60657));
                Assert.That(reader.GetInt32(5), Is.EqualTo(35209));
                Assert.That(reader.GetInt32(6), Is.EqualTo(10963));
                Assert.That(reader.GetInt32(7), Is.EqualTo(13279));
                Assert.That(reader.GetInt32(8), Is.EqualTo(32779));
                Assert.That(reader.GetInt32(9), Is.EqualTo(32612));
                Assert.That(reader.GetInt32(10), Is.EqualTo(8768));
                Assert.That(reader.GetInt32(11), Is.EqualTo(37301), "Region 252 pads stay on Thidranki.");
                Assert.That(reader.GetInt32(12), Is.EqualTo(52362));
                Assert.That(reader.GetInt32(13), Is.EqualTo(0), "Abermenai Door InternalIDs must not load beside Killaloe doors.");
                Assert.That(reader.GetInt32(14), Is.EqualTo(ExpectedDoorCount));
                Assert.That(reader.GetInt32(15), Is.EqualTo(0), "No invented Hibernia keep gate.");
                Assert.That(reader.GetInt32(16), Is.EqualTo(20536));
                Assert.That(reader.GetInt32(17), Is.EqualTo(57944));
                Assert.That(reader.GetInt32(18), Is.EqualTo(37301));
                Assert.That(reader.GetInt32(19), Is.EqualTo(32924), "Keep NPCs stay on the captured Killaloe spots.");
                Assert.That(reader.GetInt32(20), Is.EqualTo(31537));
                Assert.That(reader.GetInt32(21), Is.EqualTo(0));
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        [Test]
        public void SeedsInsertIntoEmptyMobAndDoorTables()
        {
            IReadOnlyList<NpcRow> npcs = LoadNpcSeed();
            IReadOnlyList<DoorRow> doors = LoadDoorSeed();
            string path = Path.Combine(Path.GetTempPath(), "ut-killaloe-keep-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var connection = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False");
                connection.Open();
                using (var create = connection.CreateCommand())
                {
                    create.CommandText = """
                        CREATE TABLE Mob (
                            ClassType TEXT, Name TEXT NOT NULL, Guild TEXT, X INTEGER NOT NULL, Y INTEGER NOT NULL,
                            Z INTEGER NOT NULL, Heading INTEGER, Region INTEGER, Model INTEGER, Level INTEGER,
                            Realm INTEGER, PackageID TEXT, Mob_ID TEXT PRIMARY KEY
                        );
                        CREATE TABLE Door (
                            Name TEXT, Type INTEGER NOT NULL, Z INTEGER NOT NULL, Y INTEGER NOT NULL,
                            X INTEGER NOT NULL, Heading INTEGER NOT NULL, InternalID INTEGER NOT NULL,
                            Health INTEGER NOT NULL, Door_ID TEXT PRIMARY KEY, IsPostern INTEGER NOT NULL,
                            State INTEGER NOT NULL
                        );
                        """;
                    create.ExecuteNonQuery();
                }

                using (var insertNpc = connection.CreateCommand())
                {
                    insertNpc.CommandText = """
                        INSERT INTO Mob (Name, Guild, X, Y, Z, Heading, Region, Model, Level, Realm, ClassType, PackageID, Mob_ID)
                        VALUES (@name, @guild, @x, @y, @z, @heading, @region, @model, @level, @realm, @class, @package, @id)
                        """;
                    foreach (string parameter in new[] { "@name", "@guild", "@class", "@package", "@id" })
                        insertNpc.Parameters.Add(parameter, System.Data.DbType.String);
                    foreach (string parameter in new[] { "@x", "@y", "@z", "@heading", "@region", "@model", "@level", "@realm" })
                        insertNpc.Parameters.Add(parameter, System.Data.DbType.Int32);

                    foreach (NpcRow row in npcs)
                    {
                        insertNpc.Parameters["@name"].Value = row.Name;
                        insertNpc.Parameters["@guild"].Value = row.Guild ?? "";
                        insertNpc.Parameters["@x"].Value = row.X;
                        insertNpc.Parameters["@y"].Value = row.Y;
                        insertNpc.Parameters["@z"].Value = row.Z;
                        insertNpc.Parameters["@heading"].Value = row.Heading;
                        insertNpc.Parameters["@region"].Value = row.Region;
                        insertNpc.Parameters["@model"].Value = row.Model;
                        insertNpc.Parameters["@level"].Value = row.Level;
                        insertNpc.Parameters["@realm"].Value = row.Realm;
                        insertNpc.Parameters["@class"].Value = row.ClassType;
                        insertNpc.Parameters["@package"].Value = row.PackageId;
                        insertNpc.Parameters["@id"].Value = row.MobId;
                        insertNpc.ExecuteNonQuery();
                    }
                }

                using (var insertDoor = connection.CreateCommand())
                {
                    insertDoor.CommandText = """
                        INSERT INTO Door (Name, Type, Z, Y, X, Heading, InternalID, Health, Door_ID, IsPostern, State)
                        VALUES (@name, @type, @z, @y, @x, @heading, @internal, @health, @id, @postern, @state)
                        """;
                    insertDoor.Parameters.Add("@name", System.Data.DbType.String);
                    insertDoor.Parameters.Add("@id", System.Data.DbType.String);
                    foreach (string parameter in new[] { "@type", "@z", "@y", "@x", "@heading", "@internal", "@health", "@postern", "@state" })
                        insertDoor.Parameters.Add(parameter, System.Data.DbType.Int32);

                    foreach (DoorRow row in doors)
                    {
                        insertDoor.Parameters["@name"].Value = row.Name;
                        insertDoor.Parameters["@type"].Value = row.Type;
                        insertDoor.Parameters["@z"].Value = row.Z;
                        insertDoor.Parameters["@y"].Value = row.Y;
                        insertDoor.Parameters["@x"].Value = row.X;
                        insertDoor.Parameters["@heading"].Value = row.Heading;
                        insertDoor.Parameters["@internal"].Value = row.InternalId;
                        insertDoor.Parameters["@health"].Value = row.Health;
                        insertDoor.Parameters["@id"].Value = row.DoorId;
                        insertDoor.Parameters["@postern"].Value = row.IsPostern ? 1 : 0;
                        insertDoor.Parameters["@state"].Value = row.State;
                        insertDoor.ExecuteNonQuery();
                    }
                }

                using var verify = connection.CreateCommand();
                verify.CommandText = """
                    SELECT
                        (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Region=253),
                        (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Name='Jarl Killaloe'),
                        (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Name='Huscarl'),
                        (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%' AND CAST(InternalID/1000000 AS INTEGER)=253),
                        (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%' AND Name='Keep Gate'),
                        (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%' AND IsPostern=1)
                    """;
                using var reader = verify.ExecuteReader();
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(ExpectedNpcCount));
                Assert.That(reader.GetInt32(1), Is.EqualTo(1));
                Assert.That(reader.GetInt32(2), Is.EqualTo(4));
                Assert.That(reader.GetInt32(3), Is.EqualTo(ExpectedDoorCount));
                Assert.That(reader.GetInt32(4), Is.EqualTo(4));
                Assert.That(reader.GetInt32(5), Is.EqualTo(5));
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        private sealed record NpcRow(string MobId, string Name, string Guild, string ClassType, string PackageId,
            int X, int Y, int Z, int Heading, int Region, int Model, int Level, int Realm);

        private sealed record DoorRow(string DoorId, string Name, int Type, int X, int Y, int Z, int Heading,
            int InternalId, int Health, bool IsPostern, int State);

        private static IReadOnlyList<NpcRow> LoadNpcSeed()
        {
            string text = File.ReadAllText(FindSeed("killaloe_zone253_keep_npcs.sql"), Encoding.UTF8);
            int valuesAt = text.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
            Assert.That(valuesAt, Is.GreaterThan(0));
            var rows = new List<NpcRow>();
            foreach (List<string> tokens in ParseRows(text[(valuesAt + "VALUES".Length)..]))
            {
                Assert.That(tokens, Has.Count.EqualTo(47), "OpenDAoC mob.sql column count");
                rows.Add(new NpcRow(
                    Unquote(tokens[46]), Unquote(tokens[2]), Unquote(tokens[4]), Unquote(tokens[0]), Unquote(tokens[43]),
                    int.Parse(tokens[7]), int.Parse(tokens[8]), int.Parse(tokens[9]), int.Parse(tokens[11]),
                    int.Parse(tokens[12]), int.Parse(tokens[13]), int.Parse(tokens[23]), int.Parse(tokens[24])));
            }
            return rows;
        }

        private static IReadOnlyList<string> LoadPadStatements()
        {
            string text = File.ReadAllText(FindSeed("killaloe_zone253_keep_pads.sql"), Encoding.UTF8);
            var uncommented = new StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                    continue;
                if (uncommented.Length > 0)
                    uncommented.Append('\n');
                uncommented.Append(trimmed);
            }

            var statements = new List<string>();
            foreach (string raw in uncommented.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string statement = raw.Replace('\n', ' ').Trim();
                if (statement.Length > 0)
                    statements.Add(statement);
            }
            return statements;
        }

        private static IReadOnlyList<DoorRow> LoadDoorSeed()
        {
            string text = File.ReadAllText(FindSeed("killaloe_zone253_keep_doors.sql"), Encoding.UTF8);
            int valuesAt = text.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
            Assert.That(valuesAt, Is.GreaterThan(0));
            var rows = new List<DoorRow>();
            foreach (List<string> tokens in ParseRows(text[(valuesAt + "VALUES".Length)..]))
            {
                Assert.That(tokens, Has.Count.EqualTo(17), "door seed column count");
                rows.Add(new DoorRow(
                    Unquote(tokens[14]), Unquote(tokens[0]), int.Parse(tokens[1]),
                    int.Parse(tokens[4]), int.Parse(tokens[3]), int.Parse(tokens[2]), int.Parse(tokens[5]),
                    int.Parse(tokens[6]), int.Parse(tokens[12]), int.Parse(tokens[15]) != 0, int.Parse(tokens[16])));
            }
            return rows;
        }

        private static string FindSeed(string fileName)
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                foreach (string relative in new[]
                {
                    Path.Combine("source", "tools", "OfflineDaoc.Setup", "data", fileName),
                    Path.Combine("tools", "OfflineDaoc.Setup", "data", fileName),
                    Path.Combine("OfflineDaoc.Setup", "data", fileName),
                    Path.Combine("data", fileName),
                })
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            Assert.Fail(fileName + " not found");
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

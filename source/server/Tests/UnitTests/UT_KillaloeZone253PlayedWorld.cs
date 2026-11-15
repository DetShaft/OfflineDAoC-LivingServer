using System;
using System.Data.SQLite;
using System.IO;
using DOL.GS.DatabaseUpdate;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.1.18 Setup only seeded a fresh world. An already-played world must get the same
    /// packs on the next server start, without moving NPCs onto the old Abermenai pads
    /// or inventing a Hibernia keep gate.
    /// </summary>
    [TestFixture]
    public class UT_KillaloeZone253PlayedWorld
    {
        [Test]
        public void ApplyWritesKillaloePacksOntoAnAbermenaiPlayedWorldAndIsIdempotent()
        {
            string path = Path.Combine(Path.GetTempPath(), "ut-killaloe-played-" + Guid.NewGuid().ToString("N") + ".db");
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
                        CREATE TABLE Door (
                            Name TEXT, Type INTEGER NOT NULL, Z INTEGER NOT NULL, Y INTEGER NOT NULL,
                            X INTEGER NOT NULL, Heading INTEGER NOT NULL, InternalID INTEGER NOT NULL,
                            Guild TEXT, Level INTEGER, Realm INTEGER, Flags INTEGER, Locked INTEGER,
                            Health INTEGER NOT NULL, LastTimeRowUpdated TEXT,
                            Door_ID TEXT PRIMARY KEY, IsPostern INTEGER NOT NULL, State INTEGER NOT NULL
                        );
                        CREATE TABLE Keep (
                            KeepID INTEGER, Name TEXT, Region INTEGER, X INTEGER, Y INTEGER, Z INTEGER,
                            Heading INTEGER, Realm INTEGER
                        );
                        CREATE TABLE Area (
                            Description TEXT, Region INTEGER, X INTEGER, Y INTEGER, Z INTEGER
                        );
                        INSERT INTO Keep (KeepID, Name, Region, X, Y, Z, Heading, Realm) VALUES
                            (37, 'Albion Portal Keep', 253, 37301, 52362, 3944, 2035, 1),
                            (36, 'Midgard Portal Keep', 253, 54053, 24680, 4320, 346, 2),
                            (35, 'Hibernia Portal Keep', 253, 18362, 18257, 4320, 3517, 3),
                            (33, 'Dun Abermenai', 253, 33089, 38271, 3720, 2915, 0),
                            (14, 'Albion Portal Keep', 252, 37301, 52362, 3944, 2035, 1);
                        INSERT INTO Door (Name, Type, Z, Y, X, Heading, InternalID, Health, Door_ID, IsPostern, State) VALUES
                            ('door', 2, 4010, 51689, 36923, 19, 253000101, 51000, 'abermenai-alb-1', 0, 1),
                            ('door', 2, 4010, 52085, 36927, 2088, 253000102, 51000, 'abermenai-alb-2', 0, 1),
                            ('door', 2, 4296, 25597, 53512, 2331, 253000201, 51000, 'abermenai-mid-1', 0, 1),
                            ('door', 2, 4308, 25241, 53649, 264, 253000202, 51000, 'abermenai-mid-2', 0, 1),
                            ('door', 2, 3737, 39604, 33849, 1864, 253000301, 2545, 'abermenai-center-1', 0, 0),
                            ('door', 2, 3720, 39059, 33659, 3901, 253000302, 2545, 'abermenai-center-2', 0, 0),
                            ('door', 2, 4318, 18865, 19257, 1469, 253041601, 51000, 'abermenai-hib-1', 0, 1),
                            ('door', 2, 4320, 18470, 18807, 3591, 253041602, 51000, 'abermenai-hib-2', 0, 1);
                        INSERT INTO Area (Description, Region, X, Y, Z) VALUES
                            ('Albion Portal Keep', 253, 37301, 52362, 3944),
                            ('Albion Portal Keep', 252, 37301, 52362, 3944);
                        """;
                    create.ExecuteNonQuery();
                }

                KillaloeZone253Seeds.Apply(connection);
                AssertPack(connection);
                KillaloeZone253Seeds.Apply(connection);
                AssertPack(connection);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        private static void AssertPack(SQLiteConnection connection)
        {
            using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-zone253' AND Region=253),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Region=253),
                    (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%'),
                    (SELECT X FROM Keep WHERE KeepID=37 AND Region=253),
                    (SELECT Y FROM Keep WHERE KeepID=37 AND Region=253),
                    (SELECT Z FROM Keep WHERE KeepID=37 AND Region=253),
                    (SELECT X FROM Keep WHERE KeepID=36 AND Region=253),
                    (SELECT Y FROM Keep WHERE KeepID=36 AND Region=253),
                    (SELECT X FROM Keep WHERE KeepID=35 AND Region=253),
                    (SELECT Y FROM Keep WHERE KeepID=35 AND Region=253),
                    (SELECT X FROM Keep WHERE KeepID=33 AND Region=253),
                    (SELECT Y FROM Keep WHERE KeepID=33 AND Region=253),
                    (SELECT Z FROM Keep WHERE KeepID=33 AND Region=253),
                    (SELECT X FROM Keep WHERE KeepID=14),
                    (SELECT COUNT(*) FROM Door WHERE InternalID IN
                        (253000101, 253000102, 253000201, 253000202, 253000301, 253000302, 253041601, 253041602)),
                    (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%' AND Name='Keep Gate'
                        AND InternalID BETWEEN 253203000 AND 253203999),
                    (SELECT X FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Name='Jarl Killaloe'),
                    (SELECT Y FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Name='Jarl Killaloe'),
                    (SELECT COUNT(*) FROM offline_world_migrations WHERE MigrationId IN
                        ('killaloe-zone253-spawns-v1', 'killaloe-zone253-keep-objects-v1', 'killaloe-zone253-keep-pads-v1')),
                    (SELECT X FROM Area WHERE Region=253 AND Description='Albion Portal Keep'),
                    (SELECT Y FROM Area WHERE Region=253 AND Description='Albion Portal Keep')
                """;
            using var reader = verify.ExecuteReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(KillaloeZone253Seeds.ExpectedFieldSpawnCount));
            Assert.That(reader.GetInt32(1), Is.EqualTo(KillaloeZone253Seeds.ExpectedKeepNpcCount));
            Assert.That(reader.GetInt32(2), Is.EqualTo(KillaloeZone253Seeds.ExpectedKeepDoorCount));
            Assert.That(reader.GetInt32(3), Is.EqualTo(20536));
            Assert.That(reader.GetInt32(4), Is.EqualTo(57944));
            Assert.That(reader.GetInt32(5), Is.EqualTo(8288));
            Assert.That(reader.GetInt32(6), Is.EqualTo(60657));
            Assert.That(reader.GetInt32(7), Is.EqualTo(35209));
            Assert.That(reader.GetInt32(8), Is.EqualTo(10963));
            Assert.That(reader.GetInt32(9), Is.EqualTo(13279));
            Assert.That(reader.GetInt32(10), Is.EqualTo(32779));
            Assert.That(reader.GetInt32(11), Is.EqualTo(32612));
            Assert.That(reader.GetInt32(12), Is.EqualTo(8768));
            Assert.That(reader.GetInt32(13), Is.EqualTo(37301), "Region 252 pads stay on Thidranki.");
            Assert.That(reader.GetInt32(14), Is.EqualTo(0), "Abermenai Door InternalIDs must not load beside Killaloe doors.");
            Assert.That(reader.GetInt32(15), Is.EqualTo(0), "No invented Hibernia keep gate.");
            Assert.That(reader.GetInt32(16), Is.EqualTo(32924), "Keep NPCs stay on the captured Killaloe spots.");
            Assert.That(reader.GetInt32(17), Is.EqualTo(31537));
            Assert.That(reader.GetInt32(18), Is.EqualTo(3));
            Assert.That(reader.GetInt32(19), Is.EqualTo(20536));
            Assert.That(reader.GetInt32(20), Is.EqualTo(57944));
        }
    }
}

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
                            Heading INTEGER, Realm INTEGER, OriginalRealm INTEGER
                        );
                        CREATE TABLE Area (
                            Description TEXT, Region INTEGER, X INTEGER, Y INTEGER, Z INTEGER
                        );
                        INSERT INTO Keep (KeepID, Name, Region, X, Y, Z, Heading, Realm, OriginalRealm) VALUES
                            (37, 'Albion Portal Keep', 253, 37301, 52362, 3944, 2035, 1, 1),
                            (36, 'Midgard Portal Keep', 253, 54053, 24680, 4320, 346, 2, 2),
                            (35, 'Hibernia Portal Keep', 253, 18362, 18257, 4320, 3517, 3, 3),
                            (33, 'Dun Abermenai', 253, 33089, 38271, 3720, 2915, 0, 0),
                            (14, 'Albion Portal Keep', 252, 37301, 52362, 3944, 2035, 1, 1);
                        INSERT INTO Door (Name, Type, Z, Y, X, Heading, InternalID, Health, Door_ID, IsPostern, State) VALUES
                            ('door', 2, 4010, 51689, 36923, 19, 253000101, 51000, 'abermenai-alb-1', 0, 1),
                            ('door', 2, 4010, 52085, 36927, 2088, 253000102, 51000, 'abermenai-alb-2', 0, 1),
                            ('door', 2, 4296, 25597, 53512, 2331, 253000201, 51000, 'abermenai-mid-1', 0, 1),
                            ('door', 2, 4308, 25241, 53649, 264, 253000202, 51000, 'abermenai-mid-2', 0, 1),
                            ('door', 2, 3737, 39604, 33849, 1864, 253000301, 2545, 'abermenai-center-1', 0, 0),
                            ('door', 2, 3720, 39059, 33659, 3901, 253000302, 2545, 'abermenai-center-2', 0, 0),
                            ('door', 2, 4318, 18865, 19257, 1469, 253041601, 51000, 'abermenai-hib-1', 0, 1),
                            ('door', 2, 4320, 18470, 18807, 3591, 253041602, 51000, 'abermenai-hib-2', 0, 1);
                        INSERT INTO Mob (Name, X, Y, Z, Region, Mob_ID) VALUES
                            ('Renegade Guardian', 33836, 39721, 3724, 253, 'abermenai-renegade-1'),
                            ('boogey spawn', 20533, 37663, 2960, 253, 'abermenai-boogey-1'),
                            ('Renegade Guardian', 33836, 39721, 3724, 252, 'thidranki-keeps-its-own');
                        INSERT INTO Area (Description, Region, X, Y, Z) VALUES
                            ('Albion Portal Keep', 253, 37301, 52362, 3944),
                            ('Albion Portal Keep', 252, 37301, 52362, 3944);
                        """;
                    create.ExecuteNonQuery();
                }

                KillaloeZone253Seeds.Apply(connection);
                AssertPack(connection);
                AssertAbermenaiSpawnsGone(connection);
                KillaloeZone253Seeds.Apply(connection);
                AssertPack(connection);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        private static void AssertAbermenaiSpawnsGone(SQLiteConnection connection)
        {
            using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM Mob WHERE Region=253 AND (PackageID IS NULL OR PackageID='')),
                    (SELECT COUNT(*) FROM Mob WHERE Mob_ID='thidranki-keeps-its-own')
                """;
            using var reader = verify.ExecuteReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(0), "Abermenai's own spawns, buried under Killaloe's ground, are removed.");
            Assert.That(reader.GetInt32(1), Is.EqualTo(1), "Other battlegrounds keep their spawns.");
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
                        ('killaloe-zone253-spawns-v1', 'killaloe-zone253-keep-objects-v1',
                         'killaloe-zone253-keep-pads-v1', 'killaloe-zone253-playable-v1')),
                    (SELECT X FROM Area WHERE Region=253 AND Description='Albion Portal Keep'),
                    (SELECT Y FROM Area WHERE Region=253 AND Description='Albion Portal Keep'),
                    (SELECT Realm FROM Keep WHERE KeepID=33 AND Region=253),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253'
                        AND (ClassType LIKE 'DOL.GS.Keeps.Guard%' OR ClassType LIKE 'DOL.GS.Keeps.GateKeeper%')),
                    (SELECT AggroLevel FROM Mob WHERE Name='Shadow in the Trees' AND PackageID='offline-killaloe-zone253'),
                    (SELECT RoamingRange FROM Mob WHERE Name='Shadow in the Trees' AND PackageID='offline-killaloe-zone253'),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-zone253' AND NPCTemplateID=-1),
                    (SELECT COUNT(*) FROM Door WHERE Door_ID LIKE 'offline-killaloe-door-%' AND Realm=6),
                    (SELECT MIN(X) FROM Mob WHERE PackageID='offline-killaloe-keep-253'),
                    (SELECT Z FROM Mob WHERE Mob_ID='100032784' AND PackageID='offline-killaloe-zone253'),
                    (SELECT X FROM Mob WHERE Mob_ID='100032792' AND PackageID='offline-killaloe-zone253'),
                    (SELECT Y FROM Mob WHERE Mob_ID='100032792' AND PackageID='offline-killaloe-zone253'),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Guild='Realm Enhancement'),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253'
                        AND ClassType IN ('DOL.GS.GameMerchant','DOL.GS.GameBountyMerchant')
                        AND ItemsListTemplateID IS NOT NULL AND ItemsListTemplateID <> ''),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253'
                        AND ClassType='DOL.GS.GameHastener'
                        AND Name IN ('Minstrel of Albion','Skald of Midgard','Bard of Hibernia')),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253'
                        AND ClassType IN ('DOL.GS.AlbionTeleporter','DOL.GS.MidgardTeleporter','DOL.GS.HiberniaTeleporter')
                        AND Guild IN ('Channeler','Stor Gothi')),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-zone253' AND (X>73728 OR Y>73728 OR X<8192 OR Y<8192)),
                    (SELECT COUNT(*) FROM Mob WHERE PackageID='offline-killaloe-keep-253' AND Name LIKE '%Enter Gatekeeper%')
                """;
            using var reader = verify.ExecuteReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(KillaloeZone253Seeds.ExpectedFieldSpawnCount));
            Assert.That(reader.GetInt32(1), Is.EqualTo(KillaloeZone253Seeds.ExpectedPlayableKeepNpcCount));
            Assert.That(reader.GetInt32(2), Is.EqualTo(KillaloeZone253Seeds.ExpectedKeepDoorCount));
            Assert.That(reader.GetInt32(3), Is.EqualTo(28728));
            Assert.That(reader.GetInt32(4), Is.EqualTo(66136));
            Assert.That(reader.GetInt32(5), Is.EqualTo(8288));
            Assert.That(reader.GetInt32(6), Is.EqualTo(68849));
            Assert.That(reader.GetInt32(7), Is.EqualTo(43401));
            Assert.That(reader.GetInt32(8), Is.EqualTo(19155));
            Assert.That(reader.GetInt32(9), Is.EqualTo(21471));
            Assert.That(reader.GetInt32(10), Is.EqualTo(40971));
            Assert.That(reader.GetInt32(11), Is.EqualTo(40804));
            Assert.That(reader.GetInt32(12), Is.EqualTo(8768));
            Assert.That(reader.GetInt32(13), Is.EqualTo(37301), "Region 252 pads stay on Thidranki.");
            Assert.That(reader.GetInt32(14), Is.EqualTo(0), "Abermenai Door InternalIDs must not load beside Killaloe doors.");
            Assert.That(reader.GetInt32(15), Is.EqualTo(0), "No invented Hibernia keep gate.");
            Assert.That(reader.GetInt32(16), Is.EqualTo(41116), "Keep NPCs stay on the captured Killaloe spots, plus region 253's 8192 offset.");
            Assert.That(reader.GetInt32(17), Is.EqualTo(39729));
            Assert.That(reader.GetInt32(18), Is.EqualTo(4));
            Assert.That(reader.GetInt32(19), Is.EqualTo(28728));
            Assert.That(reader.GetInt32(20), Is.EqualTo(66136));
            Assert.That(reader.GetInt32(21), Is.EqualTo(2), "Dun Killaloe is Midgard-owned.");
            Assert.That(reader.GetInt32(22), Is.GreaterThan(0), "Keep combat staff load as realm-class keep guards.");
            Assert.That(reader.GetInt32(23), Is.EqualTo(80), "Shadow in the Trees is aggressive.");
            Assert.That(reader.GetInt32(24), Is.EqualTo(350));
            Assert.That(reader.GetInt32(25), Is.EqualTo(KillaloeZone253Seeds.ExpectedFieldSpawnCount));
            Assert.That(reader.GetInt32(26), Is.EqualTo(0), "Keep doors are not eRealm.Door.");
            Assert.That(reader.GetInt32(27), Is.GreaterThanOrEqualTo(KillaloeZone253Seeds.ZoneSectorOffset),
                "Keep staff include region 253's 8192 zone offset so they sit on the keep meshes.");
            Assert.That(reader.GetInt32(28), Is.EqualTo(11512), "The large rat keeps the ground Z of its zone-local spot.");
            Assert.That(reader.GetInt32(29), Is.EqualTo(39923), "The field pack takes region 253's 8192 zone offset, like the keep staff.");
            Assert.That(reader.GetInt32(30), Is.EqualTo(67409));
            Assert.That(reader.GetInt32(31), Is.EqualTo(0), "Realm-enhancement clerics are gone.");
            Assert.That(reader.GetInt32(32), Is.GreaterThan(0), "Siege and bounty merchants have a trade list.");
            Assert.That(reader.GetInt32(33), Is.EqualTo(3), "Each portal keep hastener is that realm's bard.");
            Assert.That(reader.GetInt32(34), Is.GreaterThan(0), "Captured channelers load as realm teleporters. Hibernia uses Channeler, not Grove Tender.");
            Assert.That(reader.GetInt32(35), Is.EqualTo(0), "The field pack sits inside zone 253 (8192..73728).");
            Assert.That(reader.GetInt32(36), Is.EqualTo(0), "Enter Gatekeepers are gone; the keep doors are the entrance.");
        }
    }
}

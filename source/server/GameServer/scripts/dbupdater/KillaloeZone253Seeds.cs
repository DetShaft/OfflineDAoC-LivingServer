using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DOL.Database.Connection;

namespace DOL.GS.DatabaseUpdate
{
    /// <summary>
    /// The 1.1.18 Killaloe packs live in OfflineDaoc.Setup. A fresh world gets them there.
    /// An already-played world never ran Setup, so the next server start applies the same SQL
    /// here (before NPCs load). Region 253 portal keeps stay out of the Thidranki placeholder copy.
    /// </summary>
    public static class KillaloeZone253Seeds
    {
        public const int ExpectedFieldSpawnCount = 74;
        public const int ExpectedKeepNpcCount = 105;
        public const int ExpectedKeepDoorCount = 17;
        public const string FieldSpawnsMigrationId = "killaloe-zone253-spawns-v1";
        public const string KeepObjectsMigrationId = "killaloe-zone253-keep-objects-v1";
        public const string KeepPadsMigrationId = "killaloe-zone253-keep-pads-v1";

        public static void ApplyToLiveDatabase()
        {
            GameServer server = GameServer.Instance
                ?? throw new InvalidOperationException("Killaloe seeds need the running GameServer.");
            if (server.Configuration.DBType != EConnectionType.DATABASE_SQLITE)
                throw new InvalidOperationException("Killaloe seeds only run against the SQLite world.");
            string connectionString = WorldDatabaseSafety.PrepareConnectionString(
                server.Configuration.DBType, server.Configuration.DBConnectionString);
            using var connection = new SQLiteConnection(connectionString);
            connection.Open();
            Apply(connection);
        }

        public static void Apply(SQLiteConnection connection)
        {
            EnsureMigrationsTable(connection);
            ApplyFieldSpawns(connection);
            ApplyKeepObjects(connection);
        }

        private static void ApplyFieldSpawns(SQLiteConnection connection)
        {
            ExecuteSqlFile(connection, "killaloe_zone253_mobs.sql");
            int placed = ScalarInt(connection, """
                SELECT COUNT(*) FROM Mob
                WHERE PackageID='offline-killaloe-zone253' AND Region=253 AND ClassType='DOL.GS.GameNPC'
                """);
            if (placed != ExpectedFieldSpawnCount)
                throw new InvalidOperationException($"Killaloe zone 253 spawn pack has {placed:N0} rows; expected {ExpectedFieldSpawnCount:N0}.");
            int bad = ScalarInt(connection, """
                SELECT COUNT(*) FROM Mob
                WHERE PackageID='offline-killaloe-zone253'
                  AND (Region<>253 OR X<0 OR X>65536 OR Y<0 OR Y>65536)
                """);
            if (bad != 0)
                throw new InvalidOperationException($"Killaloe zone 253 spawn pack has {bad:N0} rows outside region 253 or 0..65536.");
            RecordMigration(connection, FieldSpawnsMigrationId,
                "Place OpenDAoC Killaloe (region 237) field spawns into region 253 with retail offset 524288 removed from X and Y.",
                placed);
        }

        private static void ApplyKeepObjects(SQLiteConnection connection)
        {
            ExecuteSqlFile(connection, "killaloe_zone253_keep_npcs.sql");
            ExecuteSqlFile(connection, "killaloe_zone253_keep_doors.sql");
            int npcs = ScalarInt(connection, """
                SELECT COUNT(*) FROM Mob
                WHERE PackageID='offline-killaloe-keep-253' AND Region=253 AND ClassType='DOL.GS.GameNPC'
                """);
            if (npcs != ExpectedKeepNpcCount)
                throw new InvalidOperationException($"Killaloe keep NPC pack has {npcs:N0} rows; expected {ExpectedKeepNpcCount:N0}.");
            int badNpcs = ScalarInt(connection, """
                SELECT COUNT(*) FROM Mob
                WHERE PackageID='offline-killaloe-keep-253'
                  AND (Region<>253 OR X<0 OR X>65536 OR Y<0 OR Y>65536)
                """);
            if (badNpcs != 0)
                throw new InvalidOperationException($"Killaloe keep NPC pack has {badNpcs:N0} rows outside region 253 or 0..65536.");
            int doors = ScalarInt(connection, """
                SELECT COUNT(*) FROM Door
                WHERE Door_ID LIKE 'offline-killaloe-door-%'
                  AND CAST(InternalID/1000000 AS INTEGER)=253
                """);
            if (doors != ExpectedKeepDoorCount)
                throw new InvalidOperationException($"Killaloe keep door pack has {doors:N0} rows; expected {ExpectedKeepDoorCount:N0}.");
            RelocateKeepPads(connection);
            RecordMigration(connection, KeepObjectsMigrationId,
                "Place live-captured Killaloe keep NPCs and doors into region 253 with retail offset 524288 already removed from X and Y. Move region 253 Keep rows onto those Killaloe spots and drop the Abermenai Door InternalIDs.",
                npcs + doors);
        }

        private static void RelocateKeepPads(SQLiteConnection connection)
        {
            bool hasArea = TableExists(connection, "Area");
            foreach (string statement in SplitSqlStatements(LoadEmbeddedText("killaloe_zone253_keep_pads.sql"), skipArea: !hasArea))
                Execute(connection, statement);

            AssertKeepAt(connection, 37, 20536, 57944, 8288);
            AssertKeepAt(connection, 36, 60657, 35209, 8288);
            AssertKeepAt(connection, 35, 10963, 13279, 8288);
            AssertKeepAt(connection, 33, 32779, 32612, 8768);

            int leftoverKeeps = ScalarInt(connection, """
                SELECT COUNT(*) FROM Keep WHERE Region=253 AND (
                    (X=37301 AND Y=52362) OR (X=54053 AND Y=24680) OR
                    (X=18362 AND Y=18257) OR (X=33089 AND Y=38271))
                """);
            if (leftoverKeeps != 0)
                throw new InvalidOperationException($"Region 253 still has {leftoverKeeps:N0} Keep row(s) on the Abermenai pads.");

            int leftoverDoors = ScalarInt(connection, """
                SELECT COUNT(*) FROM Door WHERE InternalID IN
                    (253000101, 253000102, 253000201, 253000202, 253000301, 253000302, 253041601, 253041602)
                """);
            if (leftoverDoors != 0)
                throw new InvalidOperationException($"Region 253 still has {leftoverDoors:N0} Abermenai Door row(s) loaded beside the Killaloe doors.");

            int capturedDoors = ScalarInt(connection, """
                SELECT COUNT(*) FROM Door
                WHERE Door_ID LIKE 'offline-killaloe-door-%'
                  AND CAST(InternalID/1000000 AS INTEGER)=253
                """);
            if (capturedDoors != ExpectedKeepDoorCount)
                throw new InvalidOperationException($"Killaloe keep door pack has {capturedDoors:N0} rows after the pad move; expected {ExpectedKeepDoorCount:N0}.");

            int hibKeepGates = ScalarInt(connection, """
                SELECT COUNT(*) FROM Door
                WHERE Door_ID LIKE 'offline-killaloe-door-%'
                  AND CAST(InternalID/1000000 AS INTEGER)=253
                  AND Name='Keep Gate'
                  AND InternalID BETWEEN 253203000 AND 253203999
                """);
            if (hibKeepGates != 0)
                throw new InvalidOperationException("Hibernia keep gate was not in the capture and must not be invented.");

            int jarl = ScalarInt(connection, """
                SELECT COUNT(*) FROM Mob
                WHERE PackageID='offline-killaloe-keep-253' AND Name='Jarl Killaloe'
                  AND Region=253 AND X=32924 AND Y=31537
                """);
            if (jarl != 1)
                throw new InvalidOperationException("Killaloe keep NPCs must stay on the captured spots; they were not retargeted onto Abermenai.");

            RecordMigration(connection, KeepPadsMigrationId,
                "Move region 253 Keep rows onto captured Killaloe spots and drop Abermenai Door InternalIDs 253000101/102, 253000201/202, 253000301/302, 253041601/602.",
                leftoverDoors);
        }

        private static void AssertKeepAt(SQLiteConnection connection, int keepId, int x, int y, int z)
        {
            int present = ScalarInt(connection, "SELECT COUNT(*) FROM Keep WHERE Region=253 AND KeepID=@id", ("@id", keepId));
            if (present == 0)
                return;
            int moved = ScalarInt(connection,
                "SELECT COUNT(*) FROM Keep WHERE Region=253 AND KeepID=@id AND X=@x AND Y=@y AND Z=@z",
                ("@id", keepId), ("@x", x), ("@y", y), ("@z", z));
            if (moved != 1)
                throw new InvalidOperationException($"Region 253 KeepID {keepId} is not at the captured Killaloe spot {x},{y},{z}.");
        }

        private static void ExecuteSqlFile(SQLiteConnection connection, string suffix)
        {
            // Setup dumps use MySQL backticks (`mob`). Unquoted SQLite names match Mob/Door.
            string sql = StripComments(LoadEmbeddedText(suffix)).Replace("`", "");
            if (sql.Length == 0)
                throw new InvalidOperationException($"Killaloe SQL '{suffix}' is empty.");
            Execute(connection, sql);
        }

        private static string StripComments(string sql)
        {
            var output = new StringBuilder();
            foreach (string line in sql.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                    continue;
                if (output.Length > 0)
                    output.Append('\n');
                output.Append(trimmed);
            }
            return output.ToString().Trim();
        }

        private static IEnumerable<string> SplitSqlStatements(string sql, bool skipArea)
        {
            string uncommented = StripComments(sql);
            foreach (string raw in uncommented.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string statement = raw.Replace('\n', ' ').Trim();
                if (statement.Length == 0)
                    continue;
                if (skipArea && statement.StartsWith("UPDATE Area", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return statement;
            }
        }

        private static void EnsureMigrationsTable(SQLiteConnection connection)
        {
            Execute(connection, """
                CREATE TABLE IF NOT EXISTS offline_world_migrations (
                    MigrationId TEXT PRIMARY KEY,
                    AppliedUtc TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    ArchivedRows INTEGER NOT NULL
                );
                """);
        }

        private static void RecordMigration(SQLiteConnection connection, string id, string description, int count)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO offline_world_migrations(MigrationId, AppliedUtc, Description, ArchivedRows)
                VALUES (@id, @utc, @description, @count);
                """;
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@description", description);
            command.Parameters.AddWithValue("@count", count);
            command.ExecuteNonQuery();
        }

        private static string LoadEmbeddedText(string suffix)
        {
            Assembly assembly = typeof(KillaloeZone253Seeds).Assembly;
            string resource = assembly.GetManifestResourceNames().SingleOrDefault(name =>
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The embedded Killaloe SQL '{suffix}' is missing.");
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The embedded Killaloe SQL '{suffix}' could not be opened.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static bool TableExists(SQLiteConnection connection, string name)
        {
            return ScalarInt(connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name",
                ("@name", name)) > 0;
        }

        private static int ScalarInt(SQLiteConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}

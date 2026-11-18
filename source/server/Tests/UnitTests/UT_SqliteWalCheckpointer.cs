using System;
using System.Data.SQLite;
using System.IO;
using DOL.Database;
using DOL.Database.Handlers;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Issue #95: the WAL is checkpointed on a background thread (PASSIVE, never waiting for the game's own
    /// connections), and the writer gate's wait report names the statement that waited.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_SqliteWalCheckpointer
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ut-wal-checkpointer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(_folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        [Test]
        public void APassiveCheckpointMovesTheWalIntoTheFileWhileTheServerConnectionStaysOpen()
        {
            string world = Path.Combine(_folder, "opendaoc.sqlite3.db");
            using var server = new SQLiteConnection($"Data Source={world};Pooling=False;Journal Mode=WAL");
            server.Open();
            Execute(server, "PRAGMA wal_autocheckpoint=0; CREATE TABLE saves(id INTEGER PRIMARY KEY, what TEXT);");
            for (int i = 0; i < 300; i++)
                Execute(server, $"INSERT INTO saves(what) VALUES('save {i}')");

            SqliteWalCheckpointer.Result result = SqliteWalCheckpointer.CheckpointPassive(world);

            Assert.That(result.Busy, Is.EqualTo(0L));
            Assert.That(result.WalPages, Is.GreaterThan(0L), "the saves were in the WAL");
            Assert.That(result.Moved, Is.EqualTo(result.WalPages), "nothing held the checkpoint back, so every page moved");
            Execute(server, "INSERT INTO saves(what) VALUES('after the checkpoint')");
        }

        [Test]
        public void OneCheckpointerPerDatabaseFile()
        {
            string world = Path.Combine(_folder, "world.db");
            using (var create = new SQLiteConnection($"Data Source={world};Pooling=False;Journal Mode=WAL"))
                create.Open();
            using SqliteWalCheckpointer first = SqliteWalCheckpointer.Start(world);
            Assert.That(SqliteWalCheckpointer.Start(Path.Combine(_folder, ".", "world.db")), Is.SameAs(first));
        }

        [TestCase("DELETE FROM `Inventory` WHERE `Inventory_ID` = @Inventory_ID", "DELETE FROM `Inventory`")]
        [TestCase("INSERT INTO `ItemUnique` (`Id_nb`, `Name`) VALUES (@a, @b)", "INSERT INTO `ItemUnique`")]
        [TestCase("UPDATE `Mob` SET `X` = 1", "UPDATE `Mob` SET `X` = 1")]
        [TestCase("", "statement")]
        public void WriteGateReportNamesTheStatementAndTable(string sql, string expected) =>
            Assert.That(SqliteObjectDatabase.Describe(sql), Is.EqualTo(expected));
    }
}

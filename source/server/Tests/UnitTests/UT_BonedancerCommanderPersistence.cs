using System.IO;
using System.Linq;
using DOL.Database;
using DOL.Database.Connection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The level-40 Bonedancer commanders written through a real SQLite ObjectDatabase (issue #35, 2026-11-17). 1.1.19
    /// logged "AddObject: DataObject (Spell, ObjectId{10106}) not allowed to be added to Database" on every start: DbSpell
    /// and DbLineXSpell rows default to AllowAdd = false, and the in-memory tests never went through the database, so the
    /// Keeper and Captain never existed on a player's install.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_BonedancerCommanderPersistence
    {
        private string _folder;
        private IObjectDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _folder = TemporarySqlite.NewFolder("ut-bd-commanders");
            _db = ObjectDatabase.GetObjectDatabase(EConnectionType.DATABASE_SQLITE,
                $"Data Source={Path.Combine(_folder, "world.db")};Pooling=False");
            _db.RegisterDataObject(typeof(DbNpcTemplate));
            _db.RegisterDataObject(typeof(DbSpell));
            _db.RegisterDataObject(typeof(DbLineXSpell));
            foreach (int id in new[] { 211, 212, 213 })
                Assert.That(_db.AddObject(new DbNpcTemplate { TemplateId = id, Name = $"template {id}", Model = "1", Spells = id == 213 ? "60119" : "" }), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _db = null;
            TemporarySqlite.DeleteFolder(_folder);
        }

        [Test]
        public void TheSpellTables_RefuseADefaultRow()
        {
            // The cause of the bug: without AllowAdd these rows are never written.
            Assert.That(_db.AddObject(new DbSpell { SpellID = 99999, Name = "x", Description = "x", Target = "Self" }), Is.False);
            Assert.That(_db.AddObject(new DbLineXSpell { LineName = "Bone Mystics", SpellID = 99999, Level = 40 }), Is.False);
        }

        [Test]
        public void AFreshWorld_GetsAllThreeCommanders_AndAnotherStartChangesNothing()
        {
            Assert.That(BonedancerSpecialtyCommanders.Ensure(_db), Is.EqualTo(0));
            AssertAllThreeOnce();

            Assert.That(BonedancerSpecialtyCommanders.Ensure(_db), Is.EqualTo(0), "second start");
            AssertAllThreeOnce();

            var keeper = _db.SelectObject<DbNpcTemplate>(DB.Column("TemplateId").IsEqualTo(213));
            Assert.That(keeper.Spells, Is.EqualTo("60119;10085;2658"));
        }

        [Test]
        public void AHandPatchedWorld_KeepsItsRows_WithoutDuplicates()
        {
            // The stopgap patch on the owner's install (2026-11-17) may insert rows first, under other ObjectIds.
            var patched = new DbSpell { ObjectId = "hand-10106", AllowAdd = true };
            BonedancerSpecialtyCommanders.Fill(patched, BonedancerSpecialtyCommanders.Commanders[1]);
            Assert.That(_db.AddObject(patched), Is.True);
            var stale = new DbSpell { ObjectId = "hand-10107", AllowAdd = true };
            BonedancerSpecialtyCommanders.Fill(stale, BonedancerSpecialtyCommanders.Commanders[2]);
            stale.Power = -10;
            Assert.That(_db.AddObject(stale), Is.True);
            Assert.That(_db.AddObject(new DbLineXSpell { LineName = "Bone Guardians", SpellID = 10106, Level = 40, AllowAdd = true }), Is.True);

            Assert.That(BonedancerSpecialtyCommanders.Ensure(_db), Is.EqualTo(0));
            AssertAllThreeOnce();
            Assert.That(_db.SelectObject<DbSpell>(DB.Column("SpellID").IsEqualTo(10106)).ObjectId, Is.EqualTo("hand-10106"));
            Assert.That(_db.SelectObject<DbSpell>(DB.Column("SpellID").IsEqualTo(10107)).Power, Is.EqualTo(-20), "a differing row is corrected");
        }

        [Test]
        public void AMissingTemplate_IsCountedAsAFailure()
        {
            _db.DeleteObject(_db.SelectObject<DbNpcTemplate>(DB.Column("TemplateId").IsEqualTo(211)));
            Assert.That(BonedancerSpecialtyCommanders.Ensure(_db), Is.EqualTo(1));
        }

        private void AssertAllThreeOnce()
        {
            foreach (var commander in BonedancerSpecialtyCommanders.Commanders)
            {
                var spells = _db.SelectObjects<DbSpell>(DB.Column("SpellID").IsEqualTo(commander.SpellId));
                Assert.That(spells, Has.Count.EqualTo(1), commander.Name);
                Assert.That(BonedancerSpecialtyCommanders.Matches(spells[0], commander), Is.True, commander.Name);
                var lines = _db.SelectObjects<DbLineXSpell>(DB.Column("SpellID").IsEqualTo(commander.SpellId));
                Assert.That(lines.Count(row => row.LineName == commander.Line && row.Level == BonedancerSpecialtyCommanders.LineLevel),
                    Is.EqualTo(1), commander.Name + " line row");
            }
        }
    }
}

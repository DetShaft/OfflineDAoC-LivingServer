using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using DOL.GS;
using DOL.GS.DatabaseUpdate;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Killaloe mob camps: the ground decision and the camp data file the bot goals read. Tests only; whether the
    /// camps stand on the ground in game needs the owner's navmesh and a play check.
    /// </summary>
    [TestFixture]
    public sealed class UT_KillaloeZone253Camps
    {
        [Test]
        public void NoLandUnderACampMobDropsIt()
        {
            Assert.That(KillaloeZone253Camps.Decide(new Vector3(100, 100, 8000), null),
                Is.EqualTo(KillaloeZone253Camps.Placement.Drop));
        }

        [Test]
        public void AMobOnTheFloorStays()
        {
            Assert.That(KillaloeZone253Camps.Decide(new Vector3(100, 100, 8000), new Vector3(102, 99, 8005)),
                Is.EqualTo(KillaloeZone253Camps.Placement.Keep));
        }

        [TestCase(8000, 8400)]
        [TestCase(9500, 8100)]
        public void AGuessAboveOrBelowTheFloorMoves(float guessZ, float floorZ)
        {
            Assert.That(KillaloeZone253Camps.Decide(new Vector3(100, 100, guessZ), new Vector3(100, 100, floorZ)),
                Is.EqualTo(KillaloeZone253Camps.Placement.Move));
        }

        [Test]
        public void CampFileMatchesTheSqlAndIsEvenAcrossRealms()
        {
            string path = Find(Path.Combine("source", "server", "GameServer", "bots", "autonomous", "data", "killaloe-camps.json"));
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement camps = json.RootElement.GetProperty("camps");
            int members = camps.EnumerateArray().Sum(camp => camp.GetProperty("members").GetArrayLength());
            Assert.That(members, Is.EqualTo(KillaloeZone253Seeds.ExpectedCampMobCount));

            var perThird = new Dictionary<string, List<int>>();
            foreach (JsonElement camp in camps.EnumerateArray())
            {
                string third = camp.GetProperty("realm_third").GetString();
                if (!perThird.TryGetValue(third, out List<int> levels))
                    perThird[third] = levels = new List<int>();
                levels.Add(camp.GetProperty("base_level").GetInt32());
                int x = camp.GetProperty("x").GetInt32();
                int y = camp.GetProperty("y").GetInt32();
                Assert.That(x is >= 8192 and <= 73728 && y is >= 8192 and <= 73728, Is.True, camp.GetProperty("id").GetString());
            }
            Assert.That(perThird.Keys, Is.EquivalentTo(new[] { "Albion", "Midgard", "Hibernia" }));
            // Thirds differ only by how many camps their empty ground needed; the level mix repeats the same bands.
            foreach (List<int> levels in perThird.Values)
                Assert.That(levels.Average(), Is.InRange(16.0, 18.0));

            string sql = File.ReadAllText(Find(Path.Combine("source", "tools", "OfflineDaoc.Setup", "data", "killaloe_zone253_camps.sql")));
            foreach (JsonElement camp in camps.EnumerateArray())
                foreach (JsonElement member in camp.GetProperty("members").EnumerateArray())
                    Assert.That(sql, Does.Contain("'" + member.GetProperty("mob_id").GetString() + "'"));
        }

        private static string Find(string relative)
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate))
                    return candidate;
            }
            Assert.Fail(relative + " not found");
            return null;
        }
    }
}

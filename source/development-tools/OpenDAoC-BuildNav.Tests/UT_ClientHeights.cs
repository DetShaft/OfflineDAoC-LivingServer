using System.Buffers.Binary;
using CEM.World;
using NUnit.Framework;

namespace OpenDAoC_BuildNav.Tests
{
    /// <summary>
    /// The heights.f32 reader and the choices the mesh export makes from it.
    /// These tests build no navmesh and read no game client.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_ClientHeights
    {
        private readonly List<string> _directories = new List<string>();

        [SetUp]
        public void SetUp()
        {
            ClientHeights.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ClientHeights.Clear();
            foreach (string directory in _directories)
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            _directories.Clear();
        }

        [Test]
        public void Resolve_ZoneEqualsPath_NamesThatZone()
        {
            ClientHeights.Assignment assignment = ClientHeights.Resolve(
                @"253=C:\zones\zone253\heights.f32", new ushort[] { 10, 253 });

            Assert.That(assignment.ZoneId, Is.EqualTo(253));
            Assert.That(assignment.FilePath, Is.EqualTo(@"C:\zones\zone253\heights.f32"));
        }

        [Test]
        public void Resolve_BarePath_UsesTheOnlyZoneInTheBuild()
        {
            ClientHeights.Assignment assignment = ClientHeights.Resolve(
                "/tmp/zone253/heights.f32", new ushort[] { 253 });

            Assert.That(assignment.ZoneId, Is.EqualTo(253));
            Assert.That(assignment.FilePath, Is.EqualTo("/tmp/zone253/heights.f32"));
        }

        [Test]
        public void Resolve_BarePath_RefusesWhenSeveralZonesAreInTheBuild()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                ClientHeights.Resolve("/tmp/heights.f32", new ushort[] { 253, 252 }))!;

            Assert.That(ex.Message, Does.Contain("one zone"));
        }

        [Test]
        public void Resolve_RefusesAZoneThatIsNotBeingBuilt()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                ClientHeights.Resolve("253=/tmp/heights.f32", new ushort[] { 252 }))!;

            Assert.That(ex.Message, Does.Contain("253"));
            Assert.That(ex.Message, Does.Contain("not in this build"));
        }

        [Test]
        public void Resolve_KeepsEqualsSignsThatBelongToThePath()
        {
            ClientHeights.Assignment assignment = ClientHeights.Resolve(
                "253=C:\\foo=bar\\heights.f32", new ushort[] { 253 });

            Assert.That(assignment.FilePath, Is.EqualTo("C:\\foo=bar\\heights.f32"));
        }

        [Test]
        public void Use_StoresTheFileForThatZoneOnly()
        {
            string path = WriteRamp();

            ClientHeights.Assignment assignment = ClientHeights.Use("253=" + path, new ushort[] { 252, 253 });

            Assert.That(assignment.ZoneId, Is.EqualTo(253));
            Assert.That(ClientHeights.PathFor(253), Is.EqualTo(path));
            Assert.That(ClientHeights.PathFor(252), Is.Null);
        }

        [Test]
        public void Use_MissingFile_LeavesNoZoneOverridden()
        {
            ClientHeights.Use("252=" + WriteRamp(), new ushort[] { 252 });

            Assert.Throws<InvalidDataException>(() =>
                ClientHeights.Use("253=/no/such/heights.f32", new ushort[] { 253 }));

            Assert.That(ClientHeights.PathFor(252), Is.Null);
            Assert.That(ClientHeights.PathFor(253), Is.Null);
        }

        [Test]
        public void ReadUnits_LittleEndianLayout_RowIsSouth_ColumnIsEast()
        {
            // 1.0 m at (east 1, south 0) is 40 units. 2.0 m at (east 0, south 1) is 80 units.
            // 0.5 m at the far corner (256, 256) is 20 units. A swapped axis would move these.
            var bytes = new byte[ClientHeights.Samples * ClientHeights.Samples * sizeof(float)];
            WriteMetres(bytes, 1, 0, 1.0f);
            WriteMetres(bytes, 0, 1, 2.0f);
            WriteMetres(bytes, 256, 256, 0.5f);
            string path = WriteBytes(bytes);

            int[,] units = ClientHeights.ReadUnits(path);

            Assert.That(units.GetLength(0), Is.EqualTo(257));
            Assert.That(units.GetLength(1), Is.EqualTo(257));
            Assert.That(units[1, 0], Is.EqualTo(40));
            Assert.That(units[0, 1], Is.EqualTo(80));
            Assert.That(units[256, 256], Is.EqualTo(20));
            Assert.That(units[0, 0], Is.EqualTo(0));
            Assert.That(units[256, 0], Is.EqualTo(0));
            Assert.That(units[0, 256], Is.EqualTo(0));
        }

        [Test]
        public void ReadUnits_RoundTripsEverySample_IncludingAFarEdgeThatIsNotACopy()
        {
            // build_zone.py stores (units * 0.025) as float32. A stitched far edge differs from sample 255.
            var units = new int[257, 257];
            for (int y = 0; y < 257; y++)
            {
                for (int x = 0; x < 257; x++)
                    units[x, y] = x * 1000 + y;
            }
            string path = WriteUnits(units);

            int[,] read = ClientHeights.ReadUnits(path);

            for (int y = 0; y < 257; y++)
            {
                for (int x = 0; x < 257; x++)
                    Assert.That(read[x, y], Is.EqualTo(units[x, y]), $"sample east {x}, south {y}");
            }
            Assert.That(read[256, 10], Is.Not.EqualTo(read[255, 10]));
            Assert.That(read[10, 256], Is.Not.EqualTo(read[10, 255]));
        }

        [Test]
        public void ReadUnits_RejectsTheRefinedGrid()
        {
            string path = WriteBytes(new byte[513 * 513 * sizeof(float)]);

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => ClientHeights.ReadUnits(path))!;

            Assert.That(ex.Message, Does.Contain("heights_2x.f32"));
            Assert.That(ex.Message, Does.Contain("257"));
        }

        [Test]
        public void ReadUnits_RejectsAShortFile()
        {
            string path = WriteBytes(new byte[100]);

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => ClientHeights.ReadUnits(path))!;

            Assert.That(ex.Message, Does.Contain("257"));
            Assert.That(ex.Message, Does.Not.Contain("heights_2x"));
        }

        [Test]
        public void ReadUnits_MissingFile_NamesThePath()
        {
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                ClientHeights.ReadUnits("/no/such/zone253/heights.f32"))!;

            Assert.That(ex.Message, Does.Contain("/no/such/zone253/heights.f32"));
        }

        [Test]
        public void CellHeight_ClassicGrid_AsksTheNeighbourForTheFarEdge()
        {
            var classic = new int[256, 256];
            classic[255, 4] = 10;
            classic[10, 11] = 70;
            int calls = 0;

            int edge = ClientHeights.CellHeight(classic, 256, 4, (x, y) =>
            {
                calls++;
                Assert.That(x, Is.EqualTo(256));
                Assert.That(y, Is.EqualTo(4));
                return 42;
            });
            int interior = ClientHeights.CellHeight(classic, 10, 11, (x, y) =>
            {
                calls++;
                return -1;
            });

            Assert.That(edge, Is.EqualTo(42));
            Assert.That(interior, Is.EqualTo(70));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void CellHeight_ClientGrid_UsesTheFilesFarEdge()
        {
            var client = new int[257, 257];
            client[256, 256] = 900;
            client[256, 4] = 901;
            client[3, 256] = 902;
            int calls = 0;

            Assert.That(ClientHeights.CellHeight(client, 256, 256, Refuse), Is.EqualTo(900));
            Assert.That(ClientHeights.CellHeight(client, 256, 4, Refuse), Is.EqualTo(901));
            Assert.That(ClientHeights.CellHeight(client, 3, 256, Refuse), Is.EqualTo(902));
            Assert.That(calls, Is.EqualTo(0));

            int Refuse(int x, int y)
            {
                calls++;
                return -1;
            }
        }

        [Test]
        public void Interpolate_HitsEveryWrittenSample_IncludingTheFarEdge()
        {
            var heights = new int[257, 257];
            for (int y = 0; y < 257; y++)
            {
                for (int x = 0; x < 257; x++)
                    heights[x, y] = x * 1000 + y;
            }

            Assert.That(ClientHeights.Interpolate(heights, 0, 0, 65536), Is.EqualTo(0));
            Assert.That(ClientHeights.Interpolate(heights, 2 * 256, 5 * 256, 65536), Is.EqualTo(2005));
            Assert.That(ClientHeights.Interpolate(heights, 128 * 256, 0, 65536), Is.EqualTo(128000));
            Assert.That(ClientHeights.Interpolate(heights, 0, 128 * 256, 65536), Is.EqualTo(128));
            Assert.That(ClientHeights.Interpolate(heights, 65536, 0, 65536), Is.EqualTo(256000));
            Assert.That(ClientHeights.Interpolate(heights, 0, 65536, 65536), Is.EqualTo(256));
            Assert.That(ClientHeights.Interpolate(heights, 65536, 65536, 65536), Is.EqualTo(256000 + 256));
        }

        [Test]
        public void Interpolate_UsesTheSameTriangleSplitAsTheClassicHeightMap()
        {
            // px = 0.2, py = 0.3 is the first triangle. px = 0.75, py = 0.75 is the second.
            var heights = new int[2, 2];
            heights[0, 0] = 0;
            heights[1, 0] = 100;
            heights[0, 1] = 200;
            heights[1, 1] = 400;

            Assert.That(ClientHeights.Interpolate(heights, 0.2f, 0.3f, 1), Is.EqualTo(74f).Within(0.02f));
            Assert.That(ClientHeights.Interpolate(heights, 0.75f, 0.75f, 1), Is.EqualTo(287.5f).Within(0.02f));
        }

        [Test]
        public void SampleIndex_ClassicGrid_MatchesTheOldClamp()
        {
            float[] locals = { -10f, 0f, 1f, 255f, 256f, 257f, 65280f, 65535f, 65536f };
            foreach (float local in locals)
            {
                int legacy = (int)(local * 256 / 65536);
                if (legacy < 0)
                    legacy = 0;
                if (legacy > 255)
                    legacy = 255;
                Assert.That(ClientHeights.SampleIndex(256, local, 65536), Is.EqualTo(legacy), $"local {local}");
            }
        }

        [Test]
        public void SampleIndex_ClientGrid_FarEdgeIsThe257thSample()
        {
            for (int cell = 0; cell <= 256; cell++)
                Assert.That(ClientHeights.SampleIndex(257, cell * 256f, 65536), Is.EqualTo(cell), $"cell {cell}");
        }

        private string WriteRamp()
        {
            var units = new int[257, 257];
            for (int i = 0; i < 257; i++)
                units[i, i] = 1000 + i;
            return WriteUnits(units);
        }

        private string WriteUnits(int[,] units)
        {
            var bytes = new byte[257 * 257 * sizeof(float)];
            for (int y = 0; y < 257; y++)
            {
                for (int x = 0; x < 257; x++)
                    WriteMetres(bytes, x, y, (float)(units[x, y] * ClientHeights.UnitsToMetres));
            }
            return WriteBytes(bytes);
        }

        private static void WriteMetres(byte[] bytes, int x, int y, float metres)
        {
            int offset = (y * ClientHeights.Samples + x) * sizeof(float);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, sizeof(float)), metres);
        }

        private string WriteBytes(byte[] bytes)
        {
            string directory = Directory.CreateTempSubdirectory("heights-f32-").FullName;
            _directories.Add(directory);
            string path = Path.Combine(directory, "heights.f32");
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}

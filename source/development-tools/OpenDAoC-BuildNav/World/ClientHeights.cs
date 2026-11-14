using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace CEM.World {
  /// <summary>
  /// One outdoor zone's ground from the new client's heights.f32, instead of terrain.pcx.
  /// build_zone.py writes 257 x 257 little-endian float32 metres: row = south (Y), column = east (X),
  /// one sample every 256 units, and the last row and column are the zone's far edge.
  /// </summary>
  internal static class ClientHeights {
    public const int Samples = 257;
    public const double UnitsToMetres = 0.025;

    private static readonly Dictionary<ushort, string> Files = new Dictionary<ushort, string>();

    /// <summary>The heights.f32 chosen for one zone in this run.</summary>
    internal readonly struct Assignment {
      public Assignment(ushort zoneId, string filePath) {
        ZoneId = zoneId;
        FilePath = filePath;
      }

      public ushort ZoneId { get; }
      public string FilePath { get; }
    }

    public static void Clear() {
      Files.Clear();
    }

    /// <summary>Path supplied for this zone, or null when the zone keeps terrain.pcx.</summary>
    public static string? PathFor(ushort zoneId) {
      string? path;
      return Files.TryGetValue(zoneId, out path) ? path : null;
    }

    /// <summary>
    /// Remember one zone's heights.f32 for this run and check that the file is a 257 grid.
    /// <paramref name="argument"/> is <c>zone=path</c>, or a bare path when
    /// <paramref name="zonesInThisBuild"/> contains exactly one zone.
    /// </summary>
    public static Assignment Use(string argument, IReadOnlyCollection<ushort> zonesInThisBuild) {
      Clear();
      Assignment assignment = Resolve(argument, zonesInThisBuild);
      ReadUnits(assignment.FilePath);
      Files[assignment.ZoneId] = assignment.FilePath;
      return assignment;
    }

    public static Assignment Resolve(string argument, IReadOnlyCollection<ushort> zonesInThisBuild) {
      if (argument == null)
        throw new ArgumentNullException(nameof(argument));
      if (zonesInThisBuild == null)
        throw new ArgumentNullException(nameof(zonesInThisBuild));
      argument = argument.Trim();
      if (argument.Length >= 2 && argument[0] == '"' && argument[argument.Length - 1] == '"')
        argument = argument.Substring(1, argument.Length - 2).Trim();
      if (argument.Length == 0)
        throw new ArgumentException("heights.f32 path is empty. Use --heights=<zone>=<path to heights.f32>.");

      ushort zoneId;
      string path;
      int split = argument.IndexOf('=');
      if (split > 0 && ushort.TryParse(argument.Substring(0, split).Trim(), out zoneId)) {
        path = argument.Substring(split + 1).Trim().Trim('"');
        if (path.Length == 0)
          throw new ArgumentException("heights.f32 path is empty. Use --heights=<zone>=<path to heights.f32>.");
      }
      else {
        path = argument.Trim('"');
        zoneId = OnlyZone(zonesInThisBuild);
      }

      if (!Contains(zonesInThisBuild, zoneId))
        throw new ArgumentException(
          "Zone " + zoneId + " is not in this build. Pass --zones=" + zoneId +
          " (or a region that contains it) with --heights=" + zoneId + "=<path to heights.f32>.");
      return new Assignment(zoneId, path);
    }

    /// <summary>
    /// DAoC units indexed [east, south]. The same integer a terrain.pcx sample would have been,
    /// recovered from the metres build_zone.py stored.
    /// </summary>
    public static int[,] ReadUnits(string path) {
      if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("heights.f32 path is empty.");
      if (!File.Exists(path))
        throw new InvalidDataException("heights.f32 was not found: " + path);

      byte[] bytes = File.ReadAllBytes(path);
      int expected = Samples * Samples * sizeof(float);
      if (bytes.Length != expected) {
        string hint = bytes.Length == 513 * 513 * sizeof(float)
          ? " This is the 513 x 513 refined grid (heights_2x.f32). The builder uses heights.f32."
          : "";
        throw new InvalidDataException(
          "heights.f32 must be " + Samples + " x " + Samples + " little-endian float32 metres (" + expected +
          " bytes). " + path + " is " + bytes.Length + " bytes." + hint);
      }

      var units = new int[Samples, Samples];
      for (int y = 0; y < Samples; y++) {
        for (int x = 0; x < Samples; x++) {
          int offset = (y * Samples + x) * sizeof(float);
          float metres = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, sizeof(float)));
          units[x, y] = (int) Math.Round(metres / UnitsToMetres, MidpointRounding.ToEven);
        }
      }
      return units;
    }

    /// <summary>True when the grid has its own sample at this cell. A 257-grid includes cell 256.</summary>
    public static bool Contains(int[,] heights, int cellX, int cellY) {
      return heights != null && cellX >= 0 && cellY >= 0 &&
             cellX < heights.GetLength(0) && cellY < heights.GetLength(1);
    }

    /// <summary>
    /// Height at a terrain cell. When the sample is in the grid it is used, and
    /// <paramref name="neighbourEdge"/> is not called. terrain.pcx has no sample at cell 256;
    /// heights.f32 does, and that sample is the far edge the new client draws.
    /// </summary>
    public static int CellHeight(int[,] heights, int cellX, int cellY, Func<int, int, int> neighbourEdge) {
      if (Contains(heights, cellX, cellY))
        return heights[cellX, cellY];
      return neighbourEdge(cellX, cellY);
    }

    /// <summary>
    /// Sample index along one axis. A 257-grid covers 256 cells, so its last sample sits on the far
    /// edge. Any other length is addressed the way terrain.pcx was: <c>local * samples / span</c>,
    /// clamped, which leaves a 256-grid's far edge on sample 255.
    /// </summary>
    public static int SampleIndex(int samples, float zoneLocal, int zoneSpan) {
      if (samples <= 0)
        return 0;
      int cells = samples == Samples ? samples - 1 : samples;
      int index = zoneSpan == 0 ? 0 : (int) (zoneLocal * cells / zoneSpan);
      if (index < 0)
        return 0;
      if (index >= samples)
        return samples - 1;
      return index;
    }

    /// <summary>
    /// Ground height in DAoC units at a zone-local position. Same triangle split HeightMap uses
    /// for terrain.pcx. Samples sit every zoneWidth / 256 units.
    /// </summary>
    public static float Interpolate(int[,] heights, float zoneX, float zoneY, int zoneWidth) {
      int samples = heights.GetLength(0);
      float loc2Px = (samples - 1) / (float) zoneWidth;
      float px = zoneX * loc2Px;
      float py = zoneY * loc2Px;
      int left = (int) px;
      int right = (int) (px + 1);
      int top = (int) py;
      int bottom = (int) (py + 1);
      float xPercent = px % 1.0f;
      float yPercent = py % 1.0f;

      float bottomLeft = At(heights, left, bottom);
      float topRight = At(heights, right, top);
      bool firstTri = (xPercent + yPercent) <= 1.0f;
      if (firstTri) {
        float edge = At(heights, left, top);
        float y1 = Lerp(edge, bottomLeft, yPercent);
        float y2 = Lerp(topRight, bottomLeft, yPercent);
        return Lerp(y1, y2, xPercent);
      }
      xPercent = 1.0f - xPercent;
      yPercent = 1.0f - yPercent;
      float far = At(heights, right, bottom);
      float y3 = Lerp(far, topRight, yPercent);
      float y4 = Lerp(bottomLeft, topRight, yPercent);
      return Lerp(y3, y4, xPercent);
    }

    private static ushort OnlyZone(IReadOnlyCollection<ushort> zonesInThisBuild) {
      bool any = false;
      ushort only = 0;
      foreach (ushort id in zonesInThisBuild) {
        if (any)
          throw new ArgumentException(
            "heights.f32 replaces one zone. Pass --heights=<zone>=<path>, for example --heights=253=<path to heights.f32>.");
        only = id;
        any = true;
      }
      if (!any)
        throw new ArgumentException(
          "heights.f32 replaces one zone. Pass --zones=<zone> --heights=<zone>=<path to heights.f32>.");
      return only;
    }

    private static bool Contains(IReadOnlyCollection<ushort> zones, ushort zoneId) {
      foreach (ushort id in zones) {
        if (id == zoneId)
          return true;
      }
      return false;
    }

    private static int At(int[,] heights, int x, int y) {
      int width = heights.GetLength(0);
      int height = heights.GetLength(1);
      if (x < 0)
        x = 0;
      if (y < 0)
        y = 0;
      if (x >= width)
        x = width - 1;
      if (y >= height)
        y = height - 1;
      return heights[x, y];
    }

    private static float Lerp(float start, float end, float percent) {
      return start * (1.0f - percent) + end * percent;
    }
  }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CEM.Client {
  /// <summary>
  /// A swapped zone (Killaloe in zone 253) keeps the classic client's zone id, but its fixtures, water and NIFs are a
  /// later era's. --zone-files=&lt;zone&gt;=&lt;dir&gt; reads that zone's csv and dat files (fixtures.csv, nifs.csv,
  /// bound.csv, sector.dat, water.pcx...) from a folder first. --nif-dirs=&lt;dir&gt;;&lt;dir&gt; adds NIF folders
  /// (loose .nif files or .npk archives), searched first for an overridden zone and last for every other zone.
  /// </summary>
  internal static class ZoneFileOverrides {
    private static readonly Dictionary<ushort, string> Dirs = new Dictionary<ushort, string>();
    private static readonly List<string> NifDirList = new List<string>();

    public static IReadOnlyList<string> NifDirs => NifDirList;

    public static void Clear() {
      Dirs.Clear();
      NifDirList.Clear();
    }

    /// <summary>Reads the two arguments. Empty arguments change nothing. Throws ArgumentException on a bad value.</summary>
    public static void Use(string? zoneFiles, string? nifDirs) {
      Clear();
      foreach (string part in Split(zoneFiles)) {
        int eq = part.IndexOf('=');
        if (eq <= 0 || !ushort.TryParse(part.Substring(0, eq), out ushort zone))
          throw new ArgumentException($"--zone-files expects <zone>=<folder>, got '{part}'.");
        string dir = part.Substring(eq + 1).Trim().Trim('"');
        if (!Directory.Exists(dir))
          throw new ArgumentException($"--zone-files folder for zone {zone} does not exist: {dir}");
        Dirs[zone] = dir;
      }
      foreach (string part in Split(nifDirs)) {
        string dir = part.Trim('"');
        if (!Directory.Exists(dir))
          throw new ArgumentException($"--nif-dirs folder does not exist: {dir}");
        NifDirList.Add(dir);
      }
    }

    public static bool Overrides(ushort zoneId) => Dirs.ContainsKey(zoneId);

    /// <summary>The override folder's copy of one zone file, or null to use the client's.</summary>
    public static string? FilePath(ushort zoneId, string file) {
      if (!Dirs.TryGetValue(zoneId, out string? dir))
        return null;
      return FindIgnoringCase(dir, file);
    }

    /// <summary>
    /// Where a NIF is in the extra folders: (loose file, null) or (archive, entry name). (null, null) if nowhere.
    /// A NIF "GateHouse_lvl4.nif" is looked up as that file, then inside "GateHouse_lvl4.npk".
    /// </summary>
    public static (string? File, string? Entry) NifLocation(string nif) {
      string name = Path.GetFileName(nif);
      string archive = Path.ChangeExtension(name, ".npk");
      foreach (string dir in NifDirList) {
        string? loose = FindIgnoringCase(dir, name);
        if (loose != null)
          return (loose, null);
        string? npk = FindIgnoringCase(dir, archive);
        if (npk != null)
          return (npk, name);
      }
      return (null, null);
    }

    public static string? FindIgnoringCase(string dir, string file) {
      string exact = Path.Combine(dir, file);
      if (File.Exists(exact))
        return exact;
      return Directory.EnumerateFiles(dir).FirstOrDefault(p => string.Equals(Path.GetFileName(p), file, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Split(string? value) =>
      string.IsNullOrWhiteSpace(value) ? Enumerable.Empty<string>() :
        value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);
  }
}

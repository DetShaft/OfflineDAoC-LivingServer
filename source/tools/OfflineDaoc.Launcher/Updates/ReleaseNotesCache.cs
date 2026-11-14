#nullable enable
using System;
using System.IO;

namespace OfflineDaoc.Updates;

// The last good GitHub /releases JSON for the changelog tab. Nothing sensitive; only what the public API returns.
public static class ReleaseNotesCache
{
    public static string PathFor(string updatesFolder) => System.IO.Path.Combine(updatesFolder, ReleaseFeed.ReleaseListCacheFileName);

    public static void Save(string updatesFolder, string json)
    {
        try
        {
            Directory.CreateDirectory(updatesFolder);
            File.WriteAllText(PathFor(updatesFolder), json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A cache write is optional; the tab still shows what it just fetched.
        }
    }

    public static string? TryRead(string updatesFolder)
    {
        try
        {
            string path = PathFor(updatesFolder);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// What a gamebot on a battleground tour does between fights (owner, 2026-11-21). Before, every bot in a battleground
/// either went for the central keep or roamed between the keep and the enemy portal approaches, and every bot charged
/// any enemy it could see within 5,000 units, so on the open field bots ran straight across the map at the player.
/// Now each bot spends its tour in phases of 10-16 minutes, and each phase is one of three activities:
/// <list type="bullet">
/// <item><b>Grind</b>: hunt a mob camp in the battleground for experience, sharing the camp with up to three other
/// grinders of its realm. It only takes on enemies close by (or firing on its realm), so campers are prey for the
/// hunters below, the way players ganked campers in the battlegrounds.</item>
/// <item><b>Keep fights</b>: the realm's main force. It lurks around the central keep for fights, goes in with the
/// realm's assault wave when another realm holds the keep, and defends it when its realm holds it.</item>
/// <item><b>Outskirt hunt</b>: a solo hunter that roams the mob camps and the edges of the map looking for small fights
/// and lower-level campers, and leaves enemy groups alone when it would be outnumbered.</item>
/// </list>
/// The bracket's lower levels mostly grind, its top levels mostly fight or hunt. Realm is never an input, so the
/// rules are the same in every realm. Pure functions are tested directly and by the battleground goal simulation
/// (Tests/BotSim/SimBattleground.cs).
/// </summary>
public static class AutonomousBattlegroundActivity
{
    public enum Activity { Grind, KeepFights, OutskirtHunt }

    public enum Tier { Low, Mid, High }

    public const long PhaseMinMilliseconds = 10 * 60_000;
    public const long PhaseSpreadMilliseconds = 6 * 60_000;

    /// <summary>The farthest a bot looks for an enemy to start a fight with, by activity (was 5,000 for every bot).</summary>
    public const int GrindEngageRadius = 1_800;
    public const int KeepFightsEngageRadius = 3_500;
    public const int HuntEngageRadius = 5_000;

    /// <summary>Hunters: the radius that counts an enemy group around a target, and the friends around the hunter.</summary>
    public const int GroupRadius = 1_200;

    /// <summary>At most this many grinders of one realm share a camp.</summary>
    public const int MaxGrindersPerCamp = 4;

    /// <summary>Keep fighters lurk this far around the central keep when there is nothing to assault.</summary>
    public const int KeepLurkMin = 2_000, KeepLurkMax = 4_000;

    /// <summary>Hunters stay this far from enemy portal keeps (their guards and fresh arrivals).</summary>
    public const int HuntPortalClearance = 3_000;

    /// <summary>Hunters prefer points at least this share of the field radius out from the central keep.</summary>
    public const double OutskirtShare = 0.45;

    /// <summary>The bracket's levels split into thirds: Killaloe 15-16 low, 17 mid, 18-19 high.</summary>
    public static Tier TierOf(int level, int minLevel, int maxLevel)
    {
        if (maxLevel <= minLevel) return Tier.Mid;
        double position = (double)(Math.Clamp(level, minLevel, maxLevel) - minLevel) / (maxLevel - minLevel);
        return position < 0.4 ? Tier.Low : position >= 0.6 ? Tier.High : Tier.Mid;
    }

    /// <summary>Chance in percent of (grind, keep fights, outskirt hunt) for a phase; the three add up to 100.</summary>
    public static (int Grind, int KeepFights, int Hunt) Shares(Tier tier) => tier switch
    {
        Tier.Low => (65, 25, 10),
        Tier.Mid => (35, 35, 30),
        _ => (10, 45, 45),
    };

    /// <summary>The bot's phase number at <paramref name="nowMilliseconds"/>; each bot's phases have their own length and offset.</summary>
    public static long PhaseIndex(long nowMilliseconds, long botKey)
    {
        ulong hash = Mix((ulong)botKey);
        long length = PhaseMinMilliseconds + (long)(hash % (ulong)PhaseSpreadMilliseconds);
        long offset = (long)((hash >> 20) % (ulong)length);
        return (Math.Max(0, nowMilliseconds) + offset) / length;
    }

    /// <summary>The activity for one phase of one bot (deterministic, so a bot keeps it for the whole phase).</summary>
    public static Activity Choose(Tier tier, long botKey, long phase)
    {
        (int grind, int keep, _) = Shares(tier);
        int roll = (int)(Mix(Mix((ulong)botKey) ^ (ulong)phase * 0x9E3779B97F4A7C15UL) % 100);
        return roll < grind ? Activity.Grind : roll < grind + keep ? Activity.KeepFights : Activity.OutskirtHunt;
    }

    public static Activity ChooseNow(int level, int minLevel, int maxLevel, long botKey, long nowMilliseconds) =>
        Choose(TierOf(level, minLevel, maxLevel), botKey, PhaseIndex(nowMilliseconds, botKey));

    public static int EngageRadius(Activity activity) => activity switch
    {
        Activity.Grind => GrindEngageRadius,
        Activity.KeepFights => KeepFightsEngageRadius,
        _ => HuntEngageRadius,
    };

    /// <summary>
    /// A hunter leaves an enemy alone when the enemy stands in a group of three or more and the hunter's side near it
    /// would be outnumbered by more than one.
    /// </summary>
    public static bool HunterAvoids(int enemiesAroundTarget, int friendsAroundHunter) =>
        enemiesAroundTarget >= 3 && enemiesAroundTarget > friendsAroundHunter + 1;

    /// <summary>
    /// A hunter's order of targets, lowest first: campers fighting a monster and lower-level enemies come before the
    /// nearest. In units of distance.
    /// </summary>
    public static double HuntScore(double distance, int hunterLevel, int targetLevel, bool targetFightingMonster, int enemiesAroundTarget) =>
        distance
        - (targetFightingMonster ? 2_000 : 0)
        - Math.Clamp(hunterLevel - targetLevel, -4, 4) * 300
        + Math.Max(0, enemiesAroundTarget - 1) * 600;

    /// <summary>
    /// The camp a grinder takes: a camp that already has one to three grinders of its realm first (so grinders end up
    /// in small parties), then any camp with room, picked by <paramref name="roll"/>. -1 when every camp is full.
    /// </summary>
    public static int PickGrindCamp(IReadOnlyList<int> realmGrindersAtCamp, double roll)
    {
        var shared = new List<int>();
        var open = new List<int>();
        for (int index = 0; index < realmGrindersAtCamp.Count; index++)
        {
            int count = realmGrindersAtCamp[index];
            if (count >= MaxGrindersPerCamp) continue;
            (count > 0 ? shared : open).Add(index);
        }
        List<int> pool = shared.Count > 0 ? shared : open;
        if (pool.Count == 0) return -1;
        return pool[Math.Clamp((int)(roll * pool.Count), 0, pool.Count - 1)];
    }

    /// <summary>A lurk point around the central keep for a keep fighter.</summary>
    public static Vector2 KeepLurkPoint(Vector2 keep, double angleRoll, double distanceRoll)
    {
        double angle = angleRoll * Math.PI * 2;
        double distance = KeepLurkMin + distanceRoll * (KeepLurkMax - KeepLurkMin);
        return keep + new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }

    /// <summary>
    /// A hunter's next point: one of the mob camps out on the edges of the field (where campers are), else a point on
    /// the outer ring, never near an enemy portal keep. <paramref name="fieldRadius"/> is the keep-to-portal distance.
    /// </summary>
    public static Vector2 HuntPoint(Vector2 keep, IReadOnlyList<Vector2> enemyPortals, IReadOnlyList<Vector2> camps, double fieldRadius,
        Vector2? avoid, Random random)
    {
        bool Clear(Vector2 point) =>
            enemyPortals.All(portal => Vector2.Distance(point, portal) >= HuntPortalClearance) &&
            (!avoid.HasValue || Vector2.Distance(point, avoid.Value) >= 3_000);
        Vector2[] outskirtCamps = camps.Where(camp => Vector2.Distance(camp, keep) >= fieldRadius * OutskirtShare && Clear(camp)).ToArray();
        // Two of three picks go to a camp when there is one; the rest sweep the ring between them.
        if (outskirtCamps.Length > 0 && random.Next(3) != 0)
            return outskirtCamps[random.Next(outskirtCamps.Length)];
        for (int attempt = 0; attempt < 12; attempt++)
        {
            double angle = random.NextDouble() * Math.PI * 2;
            double distance = fieldRadius * (OutskirtShare + random.NextDouble() * (0.85 - OutskirtShare));
            Vector2 point = keep + new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
            if (Clear(point)) return point;
        }
        return outskirtCamps.Length > 0 ? outskirtCamps[random.Next(outskirtCamps.Length)] : keep;
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    // --- Live state: which activity each bot inside a battleground has now, and which camp each grinder holds. ---

    private static readonly ConcurrentDictionary<long, Activity> Current = new();
    private static readonly ConcurrentDictionary<long, (ushort Region, eRealm Realm, string Camp, long Seen)> GrindCamps = new();

    /// <summary>A grinder that has not renewed its camp for this long (logged out, died and released) no longer counts.</summary>
    public const long GrindHoldMilliseconds = 2 * 60_000;

    public static long KeyOf(GameBot bot) => bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;

    public static void Report(long botKey, Activity activity) => Current[botKey] = activity;

    public static void Forget(long botKey)
    {
        Current.TryRemove(botKey, out _);
        GrindCamps.TryRemove(botKey, out _);
    }

    /// <summary>True when the bot counts toward its realm's keep assault wave (keep fighters, and bots not on a tour phase).</summary>
    public static bool JoinsKeepFights(long botKey) =>
        !Current.TryGetValue(botKey, out Activity activity) || activity == Activity.KeepFights;

    public static void HoldCamp(long botKey, ushort region, eRealm realm, string campId, long nowMilliseconds) =>
        GrindCamps[botKey] = (region, realm, campId, nowMilliseconds);

    public static void ReleaseCamp(long botKey) => GrindCamps.TryRemove(botKey, out _);

    public static int GrindersAt(ushort region, eRealm realm, string campId, long exceptBotKey, long nowMilliseconds) =>
        GrindCamps.Count(pair => pair.Key != exceptBotKey && pair.Value.Region == region && pair.Value.Realm == realm &&
            nowMilliseconds - pair.Value.Seen < GrindHoldMilliseconds &&
            string.Equals(pair.Value.Camp, campId, StringComparison.Ordinal));
}

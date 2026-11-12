using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS;

/// <summary>
/// A /spawn helper you add as a friend lives on as an autonomous gamebot (owner 2026-11-11: "if the companion bots are
/// added as friends, then they get converted into actual bots ... right now once they are spawned, they are gone").
/// - Adding it (/friend target, the Friends window's ADD TARGET) writes its offline_world_bots row and saves its gear,
///   marked online so the population controller does not log a second copy in while it is with you.
/// - It keeps helping until it leaves (dismissed, group left, owner logged off): GameBot.Delete calls
///   <see cref="Release"/>, which saves it once more where it stands and marks it offline. The population controller
///   then logs it in there as a gamebot with its name, class, race, level, specs, build and gear (owner 2026-11-11:
///   it keeps its gear, an exception to gamebots starting without armor, for befriended helpers only).
/// - If the server stops without that last save, it still logs in from the first one.
/// </summary>
public static class BefriendedHelpers
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BefriendedHelpers));
    private static readonly ConcurrentDictionary<GameBot, OfflineWorldBotRecord> Pending = new(ReferenceEqualityComparer.Instance);
    // Released helpers' first session as a gamebot (issue #39): logged in at once and kept online this long, whatever the
    // Living Server schedule says, so the owner can still invite the friend they just made. Then the schedule applies.
    private static readonly ConcurrentDictionary<long, DateTime> FirstSessionUntil = new();
    public static readonly TimeSpan FirstSession = TimeSpan.FromHours(1);

    /// <summary>A just-released helper's gamebot: the population controller logs it in now, ahead of the numbers.</summary>
    public static bool LogsInNow(long botId, DateTime nowUtc) => FirstSessionUntil.TryGetValue(botId, out DateTime until) && nowUtc < until;

    /// <summary>A just-released helper's gamebot stays online through its first session (AutonomousBotSchedule.WantsOnline).</summary>
    public static bool KeepsOnline(long botId, DateTime nowUtc) => LogsInNow(botId, nowUtc);

    /// <summary>Whether the target can become a gamebot for this player: their own /spawn helper of their realm.</summary>
    public static bool IsConvertible(bool temporaryHelper, bool epicSpecialForce, bool autonomous, bool ownedByPlayer, bool sameRealm) =>
        temporaryHelper && !epicSpecialForce && !autonomous && ownedByPlayer && sameRealm;

    public static bool IsPending(GameBot helper) => helper != null && Pending.ContainsKey(helper);

    /// <summary>A befriended helper still with its owner, by name, for the friends window and notices.</summary>
    public static GameBot FindLive(string name, eRealm realm)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = name.Trim();
        return Pending.Keys.FirstOrDefault(helper => helper.ObjectState == GameObject.eObjectState.Active && helper.Realm == realm &&
                                                     string.Equals(helper.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>No player character and no gamebot (any realm, retired too) has this name.</summary>
    public static bool IsNameFree(IObjectDatabase database, string name) =>
        !string.IsNullOrWhiteSpace(name) && !BotFriends.HasCharacter(database, name) &&
        !database.SelectObjects<OfflineWorldBotRecord>(DB.Column("Name").IsLike(name.Trim()))
            .Any(record => string.Equals(record.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Writes the helper's gamebot row and gear. False, with the reason in <paramref name="message"/>, when it cannot.
    /// </summary>
    public static bool TryBefriend(GamePlayer owner, GameBot helper, out string message)
    {
        message = string.Empty;
        if (owner == null || helper == null)
            return false;
        if (IsPending(helper))
            return true;

        if (!IsNameFree(GameServer.Database, helper.Name))
        {
            message = $"A character or gamebot named {helper.Name} already exists, so {helper.Name} can't stay on as a gamebot.";
            return false;
        }

        var record = new OfflineWorldBotRecord();
        try
        {
            helper.CaptureAsGamebot(record);
            Describe(record, owner, online: true);
            record.BindRegionId = owner.BindRegion != 0 ? owner.BindRegion : record.RegionId;
            record.BindX = owner.BindRegion != 0 ? owner.BindXpos : record.X;
            record.BindY = owner.BindRegion != 0 ? owner.BindYpos : record.Y;
            record.BindZ = owner.BindRegion != 0 ? owner.BindZpos : record.Z;

            bool added;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                added = GameServer.Database.AddObject(record);
            if (!added || record.BotId <= 0)
            {
                message = $"{helper.Name} could not be saved as a gamebot.";
                return false;
            }

            Pending[helper] = record;
            if (!helper.SaveGearAs(AutonomousBotEconomy.GetOwnerId(record.BotId)))
                Log.Warn($"BEFRIENDED_HELPER_GEAR_NOT_SAVED bot=\"{helper.Name}\" id={record.BotId} (saved again when it leaves)");
            Log.Info($"BEFRIENDED_HELPER bot=\"{helper.Name}\" id={record.BotId} class=\"{record.ClassName}\" level={record.Level} " +
                     $"owner=\"{owner.Name}\"");
            return true;
        }
        catch (Exception exception)
        {
            Pending.TryRemove(helper, out _);
            Log.Error($"Could not save the befriended helper {helper.Name} as a gamebot", exception);
            message = $"{helper.Name} could not be saved as a gamebot.";
            return false;
        }
    }

    /// <summary>
    /// The helper is leaving (GameBot.Delete): its gamebot row and gear as they are now, then offline, so the
    /// population controller logs the gamebot in where the helper stood. Never stops the helper's removal.
    /// </summary>
    public static void Release(GameBot helper)
    {
        if (helper == null || !Pending.TryRemove(helper, out OfflineWorldBotRecord record))
            return;

        try
        {
            GamePlayer owner = helper.Owner;
            helper.CaptureAsGamebot(record);
            Describe(record, owner, online: false);
            string ownerId = AutonomousBotEconomy.GetOwnerId(record.BotId);
            bool saved;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                // Gear first: the gamebot may be logged in as soon as the row reads offline.
                saved = SaveGear(helper, ownerId);
                saved &= GameServer.Database.SaveObject(record);
            }

            FirstSessionUntil[record.BotId] = DateTime.UtcNow + FirstSession;
            AutonomousPopulationController.RefreshRosterSoon();
            Log.Info($"BEFRIENDED_HELPER_RELEASED bot=\"{record.Name}\" id={record.BotId} level={record.Level} " +
                     $"region={record.RegionId} at={record.X},{record.Y},{record.Z} saved={saved}");
            if (owner?.Client != null && owner.ObjectState == GameObject.eObjectState.Active)
            {
                // The friends list had the helper online (BotFriends.OnlineBots); it is offline until the gamebot logs in,
                // and BotFriends.OnBotOnline announces that. Without this the new client kept showing it ONLINE (#39).
                owner.Out.SendRemoveFriends([record.Name]);
                owner.Out.SendMessage($"{record.Name} leaves your group and carries on as a gamebot. They are still on your friend list.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Could not release the befriended helper {helper.Name} as a gamebot", exception);
        }
    }

    /// <summary>
    /// The gear the helper wears now. A level sync re-rolls a helper's kit into a new inventory, so rows saved for
    /// its earlier kit are deleted, or the gamebot would load two items for one slot.
    /// </summary>
    private static bool SaveGear(GameBot helper, string ownerId)
    {
        HashSet<string> current = new((helper.Inventory?.AllItems ?? Array.Empty<DbInventoryItem>())
            .Where(item => item != null && !string.IsNullOrEmpty(item.ObjectId))
            .Select(item => item.ObjectId), StringComparer.Ordinal);
        DbInventoryItem[] stale = GameServer.Database.SelectObjects<DbInventoryItem>(DB.Column("OwnerID").IsEqualTo(ownerId))
            .Where(item => !current.Contains(item.ObjectId))
            .ToArray();
        bool deleted = stale.Length == 0 || GameServer.Database.DeleteObject(stale);
        return deleted && helper.SaveGearAs(ownerId);
    }

    private static void Describe(OfflineWorldBotRecord record, GamePlayer owner, bool online)
    {
        string ownerName = owner?.Name ?? "a friend";
        record.IsOnline = online;
        record.IsRetired = false;
        record.Activity = online ? $"Grouped with {ownerName}" : $"Left {ownerName}'s group";
        record.CurrentGoal = online ? $"Helping {ownerName}" : "Queued for staggered login";
        record.ObjectiveProgress = $"Was {ownerName}'s /spawn companion; added as a friend";
    }

    /// <summary>Tests only.</summary>
    public static void ResetForTests()
    {
        Pending.Clear();
        FirstSessionUntil.Clear();
    }

    /// <summary>Tests only: a released helper's gamebot in its first session until <paramref name="untilUtc"/>.</summary>
    public static void SetFirstSessionForTests(long botId, DateTime untilUtc) => FirstSessionUntil[botId] = untilUtc;
}

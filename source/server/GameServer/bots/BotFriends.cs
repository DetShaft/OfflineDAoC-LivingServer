using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.Database;
using DOL.GS.Friends;
using DOL.GS.PacketHandler;
using DOL.Network;

namespace DOL.GS;

// Ported from Cancain/OfflineDAoC-LivingServer (GPL-3.0), Beta 1.1.2.
/// <summary>
/// Autonomous gamebots on the friends list (/friend). Kept out of upstream's FriendsManager and /friend command,
/// which only call in here, so they stay easy to merge.
/// - A bot is found by its exact name (any case), as /send finds it: online through AutonomousBotRegistry.TryGetOnline,
///   offline ("Parked" in the launcher) through its offline_world_bots row. Same realm only, never a retired bot.
/// - A player character of the same name always wins: that name is never treated as a bot.
/// - The list itself stays names in DbCoreCharacter.SerializedFriendsList, as for players.
/// </summary>
public static class BotFriends
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotFriends));

    /// <summary>/friend name, when no player was found: the bot's own name.</summary>
    public static bool TryResolveName(string name, eRealm realm, out string botName) =>
        TryResolveName(GameServer.Database, name, realm, out botName);

    public static bool TryResolveName(IObjectDatabase database, string name, eRealm realm, out string botName)
    {
        botName = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        if (HasCharacter(database, name))
            return false;

        botName = AutonomousBotRegistry.TryGetOnline(name, realm, out GameBot bot) ? bot.Name : FindRecord(database, name, realm)?.Name;
        return botName != null;
    }

    /// <summary>The bot's offline_world_bots row: exact name in any case, same realm, not retired.</summary>
    public static OfflineWorldBotRecord FindRecord(IObjectDatabase database, string name, eRealm realm)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = name.Trim();
        return database.SelectObjects<OfflineWorldBotRecord>(DB.Column("Name").IsLike(name)
                .And(DB.Column("Realm").IsEqualTo((int)realm))
                .And(DB.Column("IsRetired").IsEqualTo(false)))
            .FirstOrDefault(record => IsFriendRecord(record, name, realm));
    }

    public static bool IsFriendRecord(OfflineWorldBotRecord record, string name, eRealm realm) =>
        record != null && !record.IsRetired && record.Realm == (int)realm &&
        string.Equals(record.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>What the Friends window's ADD TARGET button (/friend target) does with your current target.</summary>
    public enum TargetFriend
    {
        NoTarget,
        Self,
        Companion,
        NotACharacter,
        OtherRealm,
        AlreadyFriend,
        Add,
    }

    /// <summary>
    /// /friend target: your current target on your friends list, never off it (typing /friend name takes a friend off).
    /// - Players and autonomous gamebots of your realm only; a gamebot whose name a player character has is that player.
    /// - Companion bots (/bot, group helpers, special forces) are yours already and never go on the list.
    /// </summary>
    public static void AddTarget(GamePlayer player)
    {
        if (player == null)
            return;

        GameObject target = player.TargetObject;
        GameBot bot = target as GameBot;
        bool companion = bot != null && (!bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.IsEpicSpecialForce);
        string name = target?.Name?.Trim() ?? string.Empty;
        // A gamebot by the name its offline_world_bots row has; one a player character shares is no bot (TryResolveName).
        bool gamebot = bot != null && !companion && TryResolveName(name, bot.Realm, out name);

        TargetFriend result = ClassifyTarget(target != null, target == player, target is GamePlayer, gamebot, companion,
            target is GameLiving living && living.Realm == player.Realm,
            player.GetFriends().Contains(name, StringComparer.OrdinalIgnoreCase));
        string shown = string.IsNullOrEmpty(name) ? target?.Name ?? string.Empty : name;
        if (result == TargetFriend.Add)
        {
            if (player.AddFriend(name))
                Say(player, $"{name} was added to your friend list!");
            return;
        }

        Say(player, TargetMessage(result, shown));
    }

    /// <summary>The choice behind <see cref="AddTarget"/>, kept free of the world so it is unit-tested.</summary>
    public static TargetFriend ClassifyTarget(bool hasTarget, bool isSelf, bool isPlayer, bool isGamebot, bool isCompanion,
        bool sameRealm, bool alreadyFriend)
    {
        if (!hasTarget)
            return TargetFriend.NoTarget;
        if (isSelf)
            return TargetFriend.Self;
        if (isCompanion)
            return TargetFriend.Companion;
        if (!isPlayer && !isGamebot)
            return TargetFriend.NotACharacter;
        if (!sameRealm)
            return TargetFriend.OtherRealm;
        return alreadyFriend ? TargetFriend.AlreadyFriend : TargetFriend.Add;
    }

    public static string TargetMessage(TargetFriend result, string name) => result switch
    {
        TargetFriend.NoTarget => "Target a player or a gamebot of your realm first, then press ADD TARGET.",
        TargetFriend.Self => "You can't add yourself!",
        TargetFriend.Companion => $"{name} is one of your companions; only players and gamebots go on your friend list.",
        TargetFriend.NotACharacter => $"{name} is not a player or a gamebot, so it can't go on your friend list.",
        TargetFriend.OtherRealm => $"{name} is not of your realm, so you cannot add them.",
        TargetFriend.AlreadyFriend => $"{name} is already on your friend list.",
        _ => string.Empty,
    };

    private static void Say(GamePlayer player, string text)
    {
        if (!string.IsNullOrEmpty(text))
            player.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }

    /// <summary>The offline status of a bot just added (FriendsManager.AddFriendToPlayerList); null if it is no bot.</summary>
    public static FriendStatus OfflineStatus(string name, eRealm realm)
    {
        OfflineWorldBotRecord record = FindRecord(GameServer.Database, name, realm);
        return record == null ? null : ToStatus(record);
    }

    /// <summary>The offline statuses of the friends that are no player character, for the /friend window
    /// (FriendsManager.AddPlayerFriendsListToCache).</summary>
    public static IEnumerable<FriendStatus> OfflineStatuses(IEnumerable<string> friends, IEnumerable<string> characterNames, eRealm realm) =>
        OfflineStatuses(GameServer.Database, friends, characterNames, realm);

    public static FriendStatus[] OfflineStatuses(IObjectDatabase database, IEnumerable<string> friends, IEnumerable<string> characterNames, eRealm realm)
    {
        string[] others = friends.Except(characterNames, StringComparer.OrdinalIgnoreCase).ToArray();
        if (others.Length == 0)
            return [];

        return database.SelectObjects<OfflineWorldBotRecord>(DB.Column("Name").IsIn(others))
            .Where(record => IsFriendRecord(record, record.Name, realm))
            .Select(ToStatus)
            .ToArray();
    }

    public static FriendStatus ToStatus(OfflineWorldBotRecord record) =>
        new(record.Name, record.Level, record.ClassId, LastPlayed(record));

    /// <summary>When the bot was last in the world: the later of its last save and its last status update.</summary>
    public static DateTime LastPlayed(OfflineWorldBotRecord record)
    {
        DateTime last = DateTime.MinValue;
        foreach (string stamp in new[] { record.LastSavedUtc, record.LastUpdateUtc })
        {
            if (DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime when) && when.ToUniversalTime() > last)
                last = when.ToUniversalTime();
        }

        return last == DateTime.MinValue ? last : last.ToLocalTime();
    }

    /// <summary>The friends list sent on entering the game (FriendsManager.SendPlayerFriendsList): the online players plus
    /// the online bots, in one AddFriend packet. A second packet would read as "just entered the game" for every bot.</summary>
    public static string[] WithOnlineBots(GamePlayer player, string[] onlinePlayers)
    {
        string[] bots = OnlineBots(player, player.GetFriends().Except(onlinePlayers, StringComparer.OrdinalIgnoreCase))
            .Select(bot => bot.Name)
            .ToArray();
        return bots.Length == 0 ? onlinePlayers : onlinePlayers.Concat(bots).ToArray();
    }

    /// <summary>/friend window (FriendsManager.SendPlayerFriendsSocial): an "F" line for each online bot among the friends
    /// not listed yet, as upstream writes one for an online player. A listed bot leaves <paramref name="unlisted"/>, so it
    /// is not shown as offline too. The quoted field carries zone plus a short activity label for the modern friends UI.</summary>
    public static int SendOnlineBots(GamePlayer player, List<string> unlisted, int index)
    {
        foreach (GameBot bot in OnlineBots(player, unlisted.ToArray()))
        {
            unlisted.RemoveAll(name => string.Equals(name, bot.Name, StringComparison.OrdinalIgnoreCase));
            string zone = bot.CurrentZone?.Description ?? string.Empty;
            player.Out.SendMessage(SocialLine(index++, bot.Name, bot.Level, bot.CharacterClass?.ID ?? 0,
                    WhereWithActivity(zone, ActivityLabel(bot))),
                eChatType.CT_SocialInterface, eChatLoc.CL_SystemWindow);
        }

        return index;
    }

    /// <summary>A friend's line in the social window, in upstream's format: F,place,name,level,class,"zone or last played".</summary>
    public static string SocialLine(int index, string name, int level, int classId, string where) =>
        string.Format("F,{0},{1},{2},{3},\"{4}\"", index, name, level, classId, where);

    /// <summary>Zone field with a short activity tag for online friends (classic client still shows one quoted string).</summary>
    public static string WhereWithActivity(string zone, string activity)
    {
        zone = zone?.Trim() ?? string.Empty;
        activity = activity?.Trim() ?? string.Empty;
        if (activity.Length == 0)
            return zone;
        if (zone.Length == 0)
            return activity;
        return $"{zone} — {activity}";
    }

    /// <summary>
    /// Short activity snapshot for the friends window: grinding, in group, idle in town, frontier, in town, or online.
    /// Pure over the bot's group flag and PersistentRecord text so it is unit-tested without a world.
    /// </summary>
    public static string ActivityLabel(GameBot bot)
    {
        if (bot == null)
            return "offline";
        return ActivityLabel(bot.Group != null, bot.IsPlayerLedGroup,
            bot.PersistentRecord?.Activity, bot.PersistentRecord?.CurrentGoal);
    }

    public static string ActivityLabel(bool inGroup, bool playerLed, string activity, string goal)
    {
        if (inGroup)
            return playerLed ? "in group" : "in group";

        string hay = $"{activity} {goal}".ToLowerInvariant();
        if (string.Equals(activity, "Town idling", StringComparison.OrdinalIgnoreCase) ||
            hay.Contains("town idle") || hay.Contains("idle in"))
            return "idle in town";
        if (hay.Contains("grind") || hay.Contains("camp") || hay.Contains("dungeon") || hay.Contains("pull"))
            return "grinding";
        if (hay.Contains("rvr") || hay.Contains("frontier") || hay.Contains("siege") || hay.Contains("relic") ||
            hay.Contains("keep"))
            return "frontier";
        if (hay.Contains("vendor") || hay.Contains("trainer") || hay.Contains("bank") || hay.Contains("exchange") ||
            hay.Contains("craft") || hay.Contains("stable") || hay.Contains("town"))
            return "in town";
        if (string.IsNullOrWhiteSpace(activity) && string.IsNullOrWhiteSpace(goal))
            return "online";

        string text = string.IsNullOrWhiteSpace(activity) ? goal : activity;
        text = text.Trim();
        return text.Length <= 28 ? text.ToLowerInvariant() : text[..28].Trim().ToLowerInvariant();
    }

    private static GameBot[] OnlineBots(GamePlayer player, IEnumerable<string> names)
    {
        GameBot[] bots = names
            .Select(name => AutonomousBotRegistry.TryGetOnline(name, player.Realm, out GameBot bot) ? bot : null)
            .Where(bot => bot != null)
            .ToArray();
        if (bots.Length == 0)
            return bots;

        HashSet<string> characters = CharacterNames(GameServer.Database, bots.Select(bot => bot.Name));
        return bots.Where(bot => !characters.Contains(bot.Name)).ToArray();
    }

    /// <summary>A bot entered the world (AutonomousBotRegistry.Register): the players who have it on their list get the
    /// AddFriend update, as FriendsManager sends when a player enters the game. The clients show the notice.</summary>
    public static void OnBotOnline(GameBot bot) => Notify(bot, true);

    /// <summary>A bot left the world (AutonomousBotRegistry.Unregister): RemoveFriend, as for a player quitting.</summary>
    public static void OnBotOffline(GameBot bot) => Notify(bot, false);

    private static void Notify(GameBot bot, bool online)
    {
        // A friends notice must never stop a bot's login or logout.
        try
        {
            if (bot == null || bot.IsTemporaryGroupHelper || bot.IsEpicSpecialForce || ClientService.Instance == null)
                return;

            GamePlayer[] listeners = ClientService.Instance.GetPlayers()
                .Where(player => IsListener(player.Realm, player.GetFriends(), bot.Realm, bot.Name))
                .ToArray();
            if (listeners.Length == 0 || HasCharacter(GameServer.Database, bot.Name))
                return;

            string[] update = [bot.Name];
            foreach (GamePlayer player in listeners)
            {
                if (online)
                    SendEntered(player, bot.Name);
                else
                    player.Out.SendRemoveFriends(update);
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"BotFriends: could not tell friends that {bot?.Name} went {(online ? "online" : "offline")}", exception);
        }
    }

    /// <summary>
    /// A friend entered the game, told so that both clients show it once (in game 2026-10-28, old client: a one-name
    /// AddFriend left only that bot in Friends Monitor and printed nothing).
    /// - The old client (game.dll's 0xC6 handler) prints "Your friend %s just entered the game." and lists the name for a
    ///   RemoveFriend whose first byte is 1 (0 is "just left"). Its AddFriend (0xC5) replaces the whole Friends Monitor
    ///   list and prints nothing.
    /// - The new client (server.gd) reads any RemoveFriend as "left", for a name it lists, which this one is not yet, and
    ///   prints "just entered" for each name a later AddFriend adds.
    /// So: RemoveFriend with 1 and the name first, then every friend online now as one AddFriend.
    /// </summary>
    private static void SendEntered(GamePlayer player, string name) => SendEntered(player, name, OnlineFriendNames(player));

    private static void SendEntered(GamePlayer player, string name, string[] online)
    {
        using (var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(AbstractPacketLib.GetPacketCode(eServerPackets.RemoveFriend)))
        {
            WriteFriendNotice(pak, true, name);
            player.Out.SendTCP(pak);
        }

        player.Out.SendAddFriends(online);
    }

    /// <summary>
    /// A friend was just added (FriendsManager.AddFriendToPlayerList; upstream sent AddFriend with that one name, even
    /// offline, which left the old client's Friends Monitor with only that name). Friends Monitor gets every friend online
    /// now, and the "just entered" notice comes only when the new friend really is online.
    /// </summary>
    public static void OnFriendAdded(GamePlayer player, string friend)
    {
        string[] online = OnlineFriendNames(player);
        if (IsOnlineFriend(online, friend))
            SendEntered(player, friend, online);
        else
            player.Out.SendAddFriends(online);
    }

    public static bool IsOnlineFriend(IEnumerable<string> online, string friend) =>
        !string.IsNullOrWhiteSpace(friend) && online.Contains(friend.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The old client's friend notice: a byte, 1 entered or 0 left, then the name.</summary>
    public static void WriteFriendNotice(PacketOut pak, bool entered, string name)
    {
        pak.WriteByte((byte)(entered ? 1 : 0));
        pak.WritePascalStringIntLE(name);
    }

    /// <summary>Every friend online now, players and bots, as FriendsManager lists them on entering the game.</summary>
    public static string[] OnlineFriendNames(GamePlayer player)
    {
        string[] friends = player.GetFriends();
        string[] players = ClientService.Instance.GetPlayers()
            .Where(other => other != player && !other.IsAnonymous && friends.Contains(other.Name))
            .Select(other => other.Name)
            .ToArray();
        return WithOnlineBots(player, players);
    }

    public static bool IsListener(eRealm playerRealm, IEnumerable<string> friends, eRealm botRealm, string botName) =>
        playerRealm == botRealm && friends.Contains(botName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a player character has this name (any case): such a name is never a bot.</summary>
    public static bool HasCharacter(IObjectDatabase database, string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        database.SelectObjects<DbCoreCharacter>(DB.Column("Name").IsLike(name.Trim()))
            .Any(character => string.Equals(character.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static HashSet<string> CharacterNames(IObjectDatabase database, IEnumerable<string> names)
    {
        string[] wanted = names.ToArray();
        HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);
        if (wanted.Length == 0)
            return found;

        foreach (DbCoreCharacter character in database.SelectObjects<DbCoreCharacter>(DB.Column("Name").IsIn(wanted)))
            found.Add(character.Name);
        return found;
    }
}

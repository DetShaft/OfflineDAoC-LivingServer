using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// A player's saved static party of autonomous gamebots (change plan #2): 4–8 favorite names,
/// persisted per character, invited on login or with /static invite. Busy bots still answer in
/// character through BotGroupInvite / AutonomousLivingServer as a normal /invite would.
/// </summary>
public static class PlayerStaticParty
{
    public const int MinSlots = 4;
    public const int MaxSlots = 8;
    public const string NamesKey = "static_party_names";
    public const string AutoKey = "static_party_auto";
    public const int LoginInviteDelayMs = 8_000;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    [GameServerStartedEvent]
    public static void OnServerStart(DOLEvent e, object sender, EventArgs args) =>
        GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));

    [GameServerStoppedEvent]
    public static void OnServerStop(DOLEvent e, object sender, EventArgs args) =>
        GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));

    private static void OnPlayerEntered(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is not GamePlayer player || !AutoInviteEnabled(player))
            return;
        string[] names = List(player);
        if (names.Length == 0)
            return;

        // Delay so the friends list and nearby bots settle after login (same idea as guild greetings).
        _ = new ECSGameTimer(player, _ =>
        {
            if (player.ObjectState != GameObject.eObjectState.Active)
                return 0;
            InviteAll(player, announceMissing: false);
            return 0;
        }, LoginInviteDelayMs);
    }

    /// <summary>Saved names for this character, order preserved, empty slots dropped.</summary>
    public static string[] List(GamePlayer player) => ParseNames(ReadParam(player, NamesKey));

    public static bool AutoInviteEnabled(GamePlayer player) =>
        string.Equals(ReadParam(player, AutoKey), "1", StringComparison.Ordinal);

    public static void SetAutoInvite(GamePlayer player, bool on) =>
        WriteParam(player, AutoKey, on ? "1" : "0");

    public static bool TryAdd(GamePlayer player, string name, out string message)
    {
        message = null;
        if (player == null || string.IsNullOrWhiteSpace(name))
        {
            message = "Usage: /static add <gamebot name>";
            return false;
        }

        name = name.Trim();
        if (!BotFriends.TryResolveName(name, player.Realm, out string botName))
        {
            if (!AutonomousBotRegistry.TryGetOnline(name, player.Realm, out GameBot online))
            {
                message = $"No gamebot of your realm named {name}.";
                return false;
            }
            botName = online.Name;
        }
        List<string> party = List(player).ToList();
        if (party.Any(n => string.Equals(n, botName, StringComparison.OrdinalIgnoreCase)))
        {
            message = $"{botName} is already on your static party.";
            return false;
        }

        if (party.Count >= MaxSlots)
        {
            message = $"Your static party is full ({MaxSlots}). Remove someone first.";
            return false;
        }

        party.Add(botName);
        SaveNames(player, party);
        message = $"{botName} added to your static party ({party.Count}/{MaxSlots}).";
        return true;
    }

    public static bool TryRemove(GamePlayer player, string name, out string message)
    {
        message = null;
        if (player == null || string.IsNullOrWhiteSpace(name))
        {
            message = "Usage: /static remove <gamebot name>";
            return false;
        }

        name = name.Trim();
        List<string> party = List(player).ToList();
        int removed = party.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            message = $"{name} is not on your static party.";
            return false;
        }

        SaveNames(player, party);
        message = $"{name} removed from your static party ({party.Count}/{MaxSlots}).";
        return true;
    }

    /// <summary>Invite every saved name that is online and free; busy bots keep today's in-character decline.</summary>
    public static int InviteAll(GamePlayer player, bool announceMissing = true)
    {
        if (player == null)
            return 0;

        string[] party = List(player);
        if (party.Length == 0)
        {
            if (announceMissing)
                ChatUtil.SendSystemMessage(player, "Your static party is empty. Use /static add <name>.");
            return 0;
        }

        if (player.Group != null && player.Group.Leader != player)
        {
            ChatUtil.SendSystemMessage(player, "You are not the leader of your group.");
            return 0;
        }

        int invited = 0;
        int missing = 0;
        foreach (string name in party)
        {
            if (player.Group != null && player.Group.MemberCount >= ServerProperties.Properties.GROUP_MAX_MEMBER)
            {
                ChatUtil.SendSystemMessage(player, "The group is full.");
                break;
            }

            if (player.Group?.GetMembersInTheGroup()
                    .Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) == true)
                continue;

            if (!AutonomousBotRegistry.TryGetOnline(name, player.Realm, out GameBot bot))
            {
                missing++;
                if (announceMissing)
                    ChatUtil.SendSystemMessage(player, $"{name} is not online.");
                continue;
            }

            // 1.1.14: a friend on the way counts as invited; it joins on arrival (BotFriendTravel).
            if (BotFriendTravel.IsOnTheWay(bot, player))
                continue;
            if (BotFriendTravel.Travels(player, bot))
            {
                if (BotFriendTravel.TryStart(player, bot))
                    invited++;
                continue;
            }
            if (!BotGroupInvite.CloseEnoughToInviteByName(player, bot))
            {
                ChatUtil.SendSystemMessage(player, $"{bot.Name} is too far away to join you. Meet up first.");
                AutonomousLivingServer.OnInviteTooFar(player, bot);
                continue;
            }

            if (BotGroupInvite.TryInvite(player, bot))
                invited++;
        }

        if (announceMissing || invited > 0)
            ChatUtil.SendSystemMessage(player, $"Static party: invited {invited} of {party.Length}" +
                (missing > 0 ? $" ({missing} offline)." : "."));
        return invited;
    }

    public static string[] ParseNames(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        return raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSlots)
            .ToArray();
    }

    public static string SerializeNames(IEnumerable<string> names) =>
        string.Join(",", (names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Take(MaxSlots));

    private static void SaveNames(GamePlayer player, IEnumerable<string> names) =>
        WriteParam(player, NamesKey, SerializeNames(names));

    private static string ReadParam(GamePlayer player, string key)
    {
        if (player == null || string.IsNullOrEmpty(player.ObjectId))
            return string.Empty;
        try
        {
            string cached = player.TempProperties.GetProperty<string>(key);
            if (cached != null)
                return cached;

            DbCoreCharacterXCustomParam record = DOLDB<DbCoreCharacterXCustomParam>.SelectObject(
                DB.Column("DOLCharactersObjectId").IsEqualTo(player.ObjectId)
                    .And(DB.Column("KeyName").IsEqualTo(key)));
            string value = record?.Value ?? string.Empty;
            player.TempProperties.SetProperty(key, value);
            return value;
        }
        catch (Exception ex)
        {
            Log.Warn($"PlayerStaticParty: could not read {key} for {player.Name}", ex);
            return string.Empty;
        }
    }

    private static void WriteParam(GamePlayer player, string key, string value)
    {
        if (player == null || string.IsNullOrEmpty(player.ObjectId))
            return;
        value ??= string.Empty;
        player.TempProperties.SetProperty(key, value);
        try
        {
            DbCoreCharacterXCustomParam record = DOLDB<DbCoreCharacterXCustomParam>.SelectObject(
                DB.Column("DOLCharactersObjectId").IsEqualTo(player.ObjectId)
                    .And(DB.Column("KeyName").IsEqualTo(key)));
            if (record == null)
                GameServer.Database.AddObject(new DbCoreCharacterXCustomParam(player.ObjectId, key, value));
            else
            {
                record.Value = value;
                GameServer.Database.SaveObject(record);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"PlayerStaticParty: could not save {key} for {player.Name}", ex);
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Friends;

namespace DOL.GS;

/// <summary>
/// A friend comes to you (Beta 1.1.14, the owner's request: "inviting friends that aren't in the same area as you will have them
/// move to your location to join your group after a delay").
/// - Only an autonomous gamebot on the player's friends list (/friend) does this. Anyone else invited from too far away still
///   answers "meet up first", as before (BotGroupInvite.CloseEnoughToInviteByName).
/// - The invite is checked as a normal one first (realm, leader, full group, the bot's duties). If it would be refused, it is
///   refused now, with the same words.
/// - The player is told the friend is on the way, and the friend says so by tell. After <see cref="TravelDelayMs"/> the friend
///   arrives beside the player and joins the group, and the player is told it arrived.
/// - The wait stands in for the journey, as the wait of a released groupmate in another region does
///   (BotGroupRelease.ReturnToLeaderDelayMilliseconds): the bot is moved, it does not walk the realm.
/// - On arrival everything is checked again. A friend in a fight waits for it to end (up to <see cref="CombatWaits"/> more
///   checks); one that got a duty, died, logged off or joined another player says so, and nobody is moved.
/// </summary>
public static class BotFriendTravel
{
    /// <summary>The journey of a friend in the player's region.</summary>
    public const int SameRegionDelayMs = 20_000;

    /// <summary>The journey of a friend in another region (another zone set, across the realm).</summary>
    public const int OtherRegionDelayMs = 30_000;

    /// <summary>How long a friend still fighting on arrival (or a player who is dead or between regions) waits before the next check.</summary>
    public const int CombatWaitMs = 5_000;

    /// <summary>How many times a friend still fighting on arrival waits before it gives up.</summary>
    public const int CombatWaits = 6;

    /// <summary>A trip left behind (its timer lost with its player) blocks no new invite after this.</summary>
    public const long StaleTripMs = OtherRegionDelayMs + CombatWaits * CombatWaitMs + 60_000;

    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotFriendTravel));

    private sealed record Trip(GamePlayer Player, long StartedTick, long DueTick);

    private static readonly ConcurrentDictionary<long, Trip> Trips = new();

    /// <summary>The journey's wait. Pure.</summary>
    public static int TravelDelayMs(bool sameRegion) => sameRegion ? SameRegionDelayMs : OtherRegionDelayMs;

    /// <summary>Whole seconds as the player is told them ("about 30 seconds"), at least one. Pure.</summary>
    public static int Seconds(long ms) => (int)Math.Max(1, (ms + 999) / 1000);

    /// <summary>Whether an invite from too far away sends the bot on its way rather than "meet up first". Pure.</summary>
    public static bool Travels(bool autonomous, bool helper, bool friend, bool closeEnough) =>
        autonomous && !helper && friend && !closeEnough;

    /// <summary>Whether a trip started at <paramref name="startedTick"/> still holds the bot at <paramref name="now"/>. Pure.</summary>
    public static bool StillUnderway(long startedTick, long now) => now - startedTick < StaleTripMs;

    /// <summary>What the player is told when the friend sets out. Pure.</summary>
    public static string OnTheWayMessage(string name, int seconds) =>
        $"{name} is on the way to you and will join your group on arrival (about {seconds} seconds).";

    /// <summary>What the player is told when the friend is already on the way. Pure.</summary>
    public static string AlreadyOnTheWayMessage(string name, int secondsLeft) =>
        secondsLeft > 0
            ? $"{name} is already on the way to you (about {secondsLeft} seconds left)."
            : $"{name} is already on the way to you and should arrive any moment.";

    /// <summary>What the player is told when the friend arrives and joins. Pure.</summary>
    public static string ArrivedMessage(string name) => $"{name} has arrived and joins your group.";

    /// <summary>What the player is told when the trip ends without the friend. Pure.</summary>
    public static string CalledOffMessage(string name, string why) => $"{name} could not reach you: {why}.";

    /// <summary>The friend's tell as it sets out ("omw"). Pure.</summary>
    public static string OnMyWayLine(string zone, bool sameZone, Random random)
    {
        if (sameZone || string.IsNullOrWhiteSpace(zone))
            return random.Next(4) switch
            {
                0 => "omw",
                1 => "on my way, be there in a bit",
                2 => "coming, give me a sec",
                _ => "omw, hang on",
            };
        string z = AutonomousChatFieldTalk.ShortZone(zone, random);
        return random.Next(3) switch
        {
            0 => $"omw, coming from {z}",
            1 => $"heading over from {z}, give me a bit",
            _ => $"on my way, im out in {z} so give me a min",
        };
    }

    /// <summary>The friend's word to the group on arrival. Pure.</summary>
    public static string ArrivalLine(Random random) => random.Next(4) switch
    {
        0 => "here",
        1 => "made it, whats the plan",
        2 => "hey, im here",
        _ => "ok here, lets go",
    };

    /// <summary>Whether the bot is an autonomous gamebot on the player's friends list (case-insensitive, as /friend stores it).</summary>
    public static bool IsFriend(GamePlayer player, GameBot bot)
    {
        if (player == null || bot?.Name == null)
            return false;
        string[] friends;
        try
        {
            friends = GameServer.Instance?.PlayerManager?.Friends != null ? player.GetFriends() : player.SerializedFriendsList;
        }
        catch (Exception)
        {
            friends = player.SerializedFriendsList;
        }
        return friends?.Any(name => string.Equals(name?.Trim(), bot.Name, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>Whether this invite from too far away sends the bot to the player (a friend), rather than "meet up first".</summary>
    public static bool Travels(GamePlayer player, GameBot bot) =>
        player != null && bot != null &&
        Travels(bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper, IsFriend(player, bot), BotGroupInvite.CloseEnoughToInviteByName(player, bot));

    /// <summary>Whether the bot is on its way to this player now.</summary>
    public static bool IsOnTheWay(GameBot bot, GamePlayer player = null) =>
        bot != null && Trips.TryGetValue(bot.DatabaseID, out Trip trip) && StillUnderway(trip.StartedTick, GameLoop.GameLoopTime) &&
        (player == null || trip.Player == player);

    /// <summary>
    /// Sends a friend invited from too far away on its way to the player. False (and the player told why) when the invite would be
    /// refused anyway, as BotGroupInvite.TryInvite refuses it.
    /// </summary>
    public static bool TryStart(GamePlayer player, GameBot bot)
    {
        if (player?.Client == null || bot == null)
            return false;

        if (player.Realm != bot.Realm)
        {
            ChatUtil.SendSystemMessage(player, "Only a bot of your realm will accept the invitation.");
            return false;
        }
        if (player.Group != null && player.Group.Leader != player)
        {
            ChatUtil.SendSystemMessage(player, "You are not the leader of your group.");
            return false;
        }
        if (player.Group != null && player.Group.MemberCount >= ServerProperties.Properties.GROUP_MAX_MEMBER)
        {
            ChatUtil.SendSystemMessage(player, "The group is full.");
            return false;
        }

        BotGroupInvite.Duty duty = BotGroupInvite.DutyOf(bot);
        if (BotGroupInvite.BusyMessage(bot.Name, duty) is { } busy)
        {
            ChatUtil.SendSystemMessage(player, busy);
            AutonomousLivingServer.OnInviteDeclined(player, bot, duty, false);
            return false;
        }
        if (bot.PlayerGroupLeader != null && bot.PlayerGroupLeader != player)
        {
            ChatUtil.SendSystemMessage(player, $"{bot.Name} is already following another player-led group.");
            AutonomousLivingServer.OnInviteDeclined(player, bot, BotGroupInvite.Duty.None, true);
            return false;
        }
        if (player.CurrentRegion?.IsInstance == true)
        {
            ChatUtil.SendSystemMessage(player, $"{bot.Name} can't reach you here. Invite again once you are out in the world.");
            return false;
        }

        long now = GameLoop.GameLoopTime;
        if (Trips.TryGetValue(bot.DatabaseID, out Trip underway) && StillUnderway(underway.StartedTick, now))
        {
            if (underway.Player == player)
                ChatUtil.SendSystemMessage(player, AlreadyOnTheWayMessage(bot.Name, Seconds(Math.Max(0, underway.DueTick - now))));
            else
                ChatUtil.SendSystemMessage(player, $"{bot.Name} is on the way to someone else right now.");
            return false;
        }

        int delay = TravelDelayMs(bot.CurrentRegionID == player.CurrentRegionID);
        Trips[bot.DatabaseID] = new Trip(player, now, now + delay);
        string from = AutonomousLivingServer.PlaceOf(bot);
        ChatUtil.SendSystemMessage(player, OnTheWayMessage(bot.Name, Seconds(delay)));
        Log.Info($"BOT_FRIEND_TRAVEL bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{player.Name}\" result=started delay_ms={delay} " +
                 $"from={bot.CurrentRegionID}:{bot.X},{bot.Y},{bot.Z} zone=\"{from}\" to={player.CurrentRegionID} zone=\"{AutonomousLivingServer.PlaceOf(player)}\"");

        // The friend answers as a player would. Always, not only with bot tells on: it is the confirmation the player asked for.
        string line = AutonomousBotVoices.StyleFor(bot, OnMyWayLine(from, bot.CurrentZone != null && bot.CurrentZone == player.CurrentZone, Random.Shared));
        AutonomousLivingServer.Later(player, 1_500 + Random.Shared.Next(1_500), () => AutonomousLivingServer.IsSpeaker(bot) && player.Client != null,
            () => AutonomousLivingServer.Tell(bot, player, line));

        After(player, delay, () => Arrive(player, bot, 0));
        return true;
    }

    /// <summary>Runs an action after a delay whatever the player's state then (a player between regions is briefly out of the
    /// world, and AutonomousLivingServer.Later would drop the arrival). The action checks everything itself.</summary>
    private static void After(GamePlayer player, int delayMs, Action action) =>
        _ = new ECSGameTimer(player, _ =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Log.Warn($"BOT_FRIEND_TRAVEL arrival for {player?.Name} failed", exception);
            }
            return 0;
        }, Math.Max(1, delayMs));

    /// <summary>The end of the journey: checked again, then the friend is moved beside the player and joins the group.</summary>
    private static void Arrive(GamePlayer player, GameBot bot, int waits)
    {
        if (!Trips.TryGetValue(bot.DatabaseID, out Trip trip) || trip.Player != player)
            return;

        // Worth waiting out: the friend's fight, the player's death, the player between regions.
        bool between = player.Client?.ClientState == GameClient.eClientState.Playing &&
                       (player.ObjectState != GameObject.eObjectState.Active || player.CurrentZone == null);
        string why = between ? WhyNotBot(player, bot) : WhyNot(player, bot);
        if (why == null && (between || bot.InCombat || !player.IsAlive))
        {
            if (waits < CombatWaits)
            {
                After(player, CombatWaitMs, () => Arrive(player, bot, waits + 1));
                return;
            }
            why = between ? "you were still on the move" : bot.InCombat ? "they got held up in a fight" : "you are dead";
        }
        if (why == null && !MoveBeside(player, bot))
            why = "there is no way to your spot";

        if (why != null)
        {
            Trips.TryRemove(bot.DatabaseID, out _);
            Log.Info($"BOT_FRIEND_TRAVEL bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{player.Name}\" result=called_off reason=\"{why}\"");
            if (player.Client != null)
                ChatUtil.SendSystemMessage(player, CalledOffMessage(bot.Name, why));
            return;
        }

        bool joined = BotGroupInvite.TryInvite(player, bot, ArrivedMessage(bot.Name));
        Trips.TryRemove(bot.DatabaseID, out _);
        Log.Info($"BOT_FRIEND_TRAVEL bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{player.Name}\" result={(joined ? "arrived" : "arrived_not_joined")} " +
                 $"at={bot.CurrentRegionID}:{bot.X},{bot.Y},{bot.Z}");
        if (joined)
            AutonomousLivingServer.Later(bot, 1_500 + Random.Shared.Next(1_500), () => bot.Group != null && bot.Group == player.Group,
                () => AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Group, AutonomousBotVoices.StyleFor(bot, ArrivalLine(Random.Shared))));
    }

    /// <summary>Why the friend can't come now (null when it can), in the words the player reads.</summary>
    private static string WhyNot(GamePlayer player, GameBot bot)
    {
        if (player.ObjectState != GameObject.eObjectState.Active || player.CurrentZone == null)
            return "you left the game";
        return WhyNotBot(player, bot);
    }

    /// <summary>The checks that don't need the player in the world.</summary>
    private static string WhyNotBot(GamePlayer player, GameBot bot)
    {
        if (player.Client?.ClientState != GameClient.eClientState.Playing)
            return "you left the game";
        if (bot.ObjectState != GameObject.eObjectState.Active)
            return "they logged off";
        if (!bot.IsAlive)
            return "they died on the way";
        if (bot.Realm != player.Realm)
            return "they are not of your realm";
        if (player.Group != null && player.Group.Leader != player)
            return "you are no longer the leader of your group";
        if (player.Group != null && bot.Group != player.Group && player.Group.MemberCount >= ServerProperties.Properties.GROUP_MAX_MEMBER)
            return "your group is full";
        if (bot.PlayerGroupLeader != null && bot.PlayerGroupLeader != player)
            return "they joined another player's group";
        if (player.CurrentRegion?.IsInstance == true)
            return "they can't get into the place you are now";
        return BotGroupInvite.DutyOf(bot) switch
        {
            BotGroupInvite.Duty.None => null,
            BotGroupInvite.Duty.Siege => "they got pulled into a siege",
            BotGroupInvite.Duty.Relic => "they are carrying a relic",
            BotGroupInvite.Duty.RvrEvent => "they marched off with a realm force",
            BotGroupInvite.Duty.RealmRaid => "they signed up for a realm raid",
            BotGroupInvite.Duty.StableRoute => "they got on a stable master route",
            BotGroupInvite.Duty.Helper => "they are helping another player",
            BotGroupInvite.Duty.Dead => "they died on the way",
            _ => "something came up",
        };
    }

    /// <summary>Moves the friend to a free spot beside the player, as a released groupmate joins its leader (BotGroupRelease).</summary>
    private static bool MoveBeside(GamePlayer player, GameBot bot)
    {
        if (bot.CurrentRegionID == player.CurrentRegionID && BotGroupInvite.CloseEnoughToInviteByName(player, bot))
            return true;   // it got here on its own (or the player came to it)
        bot.StopAttack();
        bot.StopFollowing();
        bot.StopMovingOnPath();
        bot.StopMoving();
        bot.StopCurrentSpellcast();
        Vector3 spot = TemporaryGroupStableTravel.FormationPoint(player, bot.ObjectID % 6);
        if (!bot.MoveTo(player.CurrentRegionID, (int)Math.Round(spot.X), (int)Math.Round(spot.Y), (int)Math.Round(spot.Z), player.Heading))
            return false;
        bot.EndReturnJourney();
        bot.WakeRecoveryRest();
        (bot.Brain as BotBrain)?.Disengage();
        return true;
    }
}

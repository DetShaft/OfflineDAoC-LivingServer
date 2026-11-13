using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Friends;

namespace DOL.GS;

/// <summary>
/// A friend comes to you (Beta 1.1.14, the owner's request: "inviting friends that aren't in the same area as you will have them
/// move to your location to join your group after a delay"). Issue #55: a bot the find-group window is offering does this too.
/// The window's invite is the same /invite, so the trip starts from the bot's LFG state, not from a separate button.
/// - An autonomous gamebot does this when it is on the player's friends list (/friend), or when it is looking for a group
///   in the player's realm and is not already in a group of its own. Looking for a group means a fresh LFG ad
///   (<see cref="BotLfgAds"/>) or a solo waiting on matchmaking, which is the other row the find-group window lists
///   (<see cref="LfgBrowser.ShouldListBot"/>). Anyone else invited from too far away still answers "meet up first"
///   (BotGroupInvite.CloseEnoughToInviteByName).
/// - The invite is checked as a normal one first (realm, leader, full group, the bot's duties). If it would be refused, it is
///   refused now, with the same words. A stable-route or raid bot never starts this trip: the invite says it is busy, and the
///   find-group window does not list it.
/// - The player is told the bot is on the way, and the bot says so by tell. After <see cref="TravelDelayMs"/> (30 seconds,
///   and 30 more for each zone between them) it arrives beside the player and joins the group, and the player is told it
///   arrived. An LFG bot takes the same journey and the same arrival checks as a friend.
/// - A friend in a group of its own turns the first invite down and asks to be asked again; an invite a minute or more later
///   takes it out of that group (<see cref="PassesRegroup"/>). This holds for a friend nearby too. It stays friends-only:
///   an LFG bot that is not a friend, and is already in a group of its own, is not sent on this trip.
/// - The wait stands in for the journey, as the wait of a released groupmate in another region does
///   (BotGroupRelease.ReturnToLeaderDelayMilliseconds): the bot is moved, it does not walk the realm.
/// - On arrival everything is checked again. A bot in a fight waits for it to end (up to <see cref="CombatWaits"/> more
///   checks); one that got a duty, died, logged off or joined another player says so, and nobody is moved.
/// </summary>
public static class BotFriendTravel
{
    /// <summary>The journey of a friend in the player's zone (Jay, 2026-11-11: "30 seconds if they are in my region").</summary>
    public const int BaseDelayMs = 30_000;

    /// <summary>Added for each zone between the friend and the player (Jay: "30 seconds extra per zone they are away").</summary>
    public const int PerZoneDelayMs = 30_000;

    /// <summary>
    /// Zones counted for a friend in another region (another zone set: the frontiers, a dungeon, the capital). Regions have no
    /// shared grid to count zones across, so a friend there counts as this many zones away.
    /// </summary>
    public const int OtherRegionZones = 3;

    /// <summary>The size of a standard zone on its region's grid (8 x 8192 units).</summary>
    public const int ZoneSize = 65_536;

    /// <summary>The longest journey, however far the friend is.</summary>
    public const int MaxDelayMs = 5 * 60_000;

    /// <summary>
    /// A friend in a group of its own turns down the first invite (Jay, 2026-11-11) and accepts one that comes at least this long
    /// after it.
    /// </summary>
    public const long RegroupWaitMs = 60_000;

    /// <summary>After this long a turned-down invite is forgotten: the next one is a first invite again.</summary>
    public const long RegroupMemoryMs = 15 * 60_000;

    /// <summary>How long a friend still fighting on arrival (or a player who is dead or between regions) waits before the next check.</summary>
    public const int CombatWaitMs = 5_000;

    /// <summary>How many times a friend still fighting on arrival waits before it gives up.</summary>
    public const int CombatWaits = 6;

    /// <summary>A trip left behind (its timer lost with its player) blocks no new invite after this.</summary>
    public const long StaleTripMs = MaxDelayMs + CombatWaits * CombatWaitMs + 60_000;

    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotFriendTravel));

    private sealed record Trip(GamePlayer Player, long StartedTick, long DueTick);

    private static readonly ConcurrentDictionary<long, Trip> Trips = new();

    /// <summary>When a friend in a group turned down this player's invite: (bot, player) to the tick.</summary>
    private static readonly ConcurrentDictionary<(long BotId, string Player), long> Declined = new();

    /// <summary>The journey's wait: 30 seconds, 30 more for each zone away, at most <see cref="MaxDelayMs"/>. Pure.</summary>
    public static int TravelDelayMs(int zonesAway) =>
        (int)Math.Min(MaxDelayMs, BaseDelayMs + (long)PerZoneDelayMs * Math.Max(0, zonesAway));

    /// <summary>
    /// How many zones lie between two zones of one region, from their places on the region's grid: 0 for the same zone, 1 for a
    /// neighbour, and one more for every zone width between them on each axis (a diagonal neighbour is 2, as the way there
    /// crosses two borders). Pure.
    /// </summary>
    public static int ZonesBetween(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
    {
        if (ax == bx && ay == by)
            return 0;
        return Axis(ax, aw, bx, bw) + Axis(ay, ah, by, bh);

        static int Axis(int a, int aSize, int b, int bSize)
        {
            long gap = Math.Max((long)b - ((long)a + Math.Max(1, aSize)), (long)a - ((long)b + Math.Max(1, bSize)));
            return gap < 0 ? 0 : 1 + (int)(gap / ZoneSize);
        }
    }

    /// <summary>How many zones the friend is from the player (<see cref="OtherRegionZones"/> from another region).</summary>
    public static int ZonesAway(GamePlayer player, GameBot bot)
    {
        if (bot.CurrentRegionID != player.CurrentRegionID)
            return OtherRegionZones;
        Zone a = bot.CurrentZone, b = player.CurrentZone;
        if (a == null || b == null)
            return 1;
        if (a == b)
            return 0;
        return Math.Max(1, ZonesBetween(a.XOffset, a.YOffset, a.Width, a.Height, b.XOffset, b.YOffset, b.Width, b.Height));
    }

    /// <summary>What a friend in a group does with an invite. Pure.</summary>
    public enum RegroupAnswer { Join, DeclineFirst, StillWrappingUp }

    /// <summary>
    /// A friend in a group of its own (not the player's) turns down the first invite, asks to be asked again, and takes an invite
    /// that comes <see cref="RegroupWaitMs"/> or more after that one. An invite too soon is turned down again without starting the
    /// wait over; after <see cref="RegroupMemoryMs"/> the turned-down invite is forgotten. Pure.
    /// </summary>
    public static RegroupAnswer Regroup(bool inOwnGroup, long? declinedTick, long now)
    {
        if (!inOwnGroup)
            return RegroupAnswer.Join;
        if (declinedTick is not { } at || now - at >= RegroupMemoryMs)
            return RegroupAnswer.DeclineFirst;
        return now - at >= RegroupWaitMs ? RegroupAnswer.Join : RegroupAnswer.StillWrappingUp;
    }

    /// <summary>What the player is told when a friend in a group turns down the first invite. Pure.</summary>
    public static string InAGroupMessage(string name, int seconds) =>
        $"{name} is in a group right now and declines. Ask again in about {Duration(seconds)}.";

    /// <summary>What the player is told when the next invite comes too soon. Pure.</summary>
    public static string StillWrappingUpMessage(string name, int secondsLeft) =>
        $"{name} is still wrapping up with their group. Ask again in about {Duration(secondsLeft)}.";

    /// <summary>The friend's tell when it turns down the first invite. Pure.</summary>
    public static string InAGroupLine(Random random) => random.Next(4) switch
    {
        0 => "in a group rn, give me a min to wrap up and ask again",
        1 => "cant atm, grouped. ask me again in a minute",
        2 => "finishing something with my group, inv me again in a bit",
        _ => "one sec, still with my group. try me again in a min",
    };

    /// <summary>The friend's word as it leaves its group for the player. Pure.</summary>
    public static string LeavingGroupLine(Random random) => random.Next(3) switch
    {
        0 => "ok, done here. coming",
        1 => "alright, said bye to my group, omw",
        _ => "ok im free now",
    };

    /// <summary>Whether the bot is in a group of its own: another group than the player's, with someone else in it.</summary>
    public static bool InOwnGroup(GamePlayer player, GameBot bot) =>
        bot.Group != null && bot.Group != player.Group && bot.Group.MemberCount > 1;

    /// <summary>
    /// The regroup rule for a friend's invite (near or far): true when it may go ahead. Otherwise the player has been told, and on
    /// a first invite the friend says so by tell.
    /// </summary>
    public static bool PassesRegroup(GamePlayer player, GameBot bot)
    {
        if (player == null || bot == null || !bot.IsAutonomousWorldBot || !IsFriend(player, bot))
            return true;
        long now = GameLoop.GameLoopTime;
        var key = (bot.DatabaseID, player.Name);
        long? at = Declined.TryGetValue(key, out long tick) ? tick : null;
        RegroupAnswer answer = Regroup(InOwnGroup(player, bot), at, now);
        switch (answer)
        {
            case RegroupAnswer.DeclineFirst:
                Declined[key] = now;
                ChatUtil.SendSystemMessage(player, InAGroupMessage(bot.Name, Seconds(RegroupWaitMs)));
                Log.Info($"BOT_FRIEND_TRAVEL bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{player.Name}\" result=declined_in_group");
                string line = AutonomousBotVoices.StyleFor(bot, InAGroupLine(Random.Shared));
                AutonomousLivingServer.Later(player, 1_500 + Random.Shared.Next(1_500), () => AutonomousLivingServer.IsSpeaker(bot) && player.Client != null,
                    () => AutonomousLivingServer.Tell(bot, player, line));
                return false;
            case RegroupAnswer.StillWrappingUp:
                ChatUtil.SendSystemMessage(player, StillWrappingUpMessage(bot.Name, Seconds(Math.Max(0, RegroupWaitMs - (now - at.Value)))));
                return false;
            default:
                if (at != null)
                {
                    Declined.TryRemove(key, out _);
                    if (InOwnGroup(player, bot))
                    {
                        // It leaves its own group now, so that group doesn't pull it into a fight on the way.
                        if (bot.PlayerGroupLeader == null)
                            bot.Group.RemoveMember(bot);
                        string bye = AutonomousBotVoices.StyleFor(bot, LeavingGroupLine(Random.Shared));
                        AutonomousLivingServer.Later(player, 1_000, () => AutonomousLivingServer.IsSpeaker(bot) && player.Client != null,
                            () => AutonomousLivingServer.Tell(bot, player, bye));
                    }
                }
                return true;
        }
    }

    /// <summary>Whole seconds as the player is told them ("about 30 seconds"), at least one. Pure.</summary>
    public static int Seconds(long ms) => (int)Math.Max(1, (ms + 999) / 1000);

    /// <summary>
    /// Whether an invite from too far away sends the bot on its way rather than "meet up first". A friend does. So does a
    /// same-realm bot that is looking for a group and is not already in a group of its own (issue #55). Pure.
    /// </summary>
    public static bool Travels(bool autonomous, bool helper, bool friend, bool closeEnough,
        bool lfg = false, bool sameRealm = true, bool inOwnGroup = false) =>
        autonomous && !helper && !closeEnough && (friend || (lfg && sameRealm && !inOwnGroup));

    /// <summary>Whether a trip started at <paramref name="startedTick"/> still holds the bot at <paramref name="now"/>. Pure.</summary>
    public static bool StillUnderway(long startedTick, long now) => now - startedTick < StaleTripMs;

    /// <summary>What the player is told when the friend sets out. Pure.</summary>
    public static string OnTheWayMessage(string name, int seconds) =>
        $"{name} is on the way to you and will join your group on arrival (about {Duration(seconds)}).";

    /// <summary>"30 seconds", "1 minute 30 seconds", "2 minutes". Pure.</summary>
    public static string Duration(int seconds)
    {
        if (seconds < 90)
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        int minutes = seconds / 60, rest = seconds % 60;
        string m = minutes == 1 ? "1 minute" : $"{minutes} minutes";
        return rest == 0 ? m : $"{m} {rest} seconds";
    }

    /// <summary>What the player is told when the friend is already on the way. Pure.</summary>
    public static string AlreadyOnTheWayMessage(string name, int secondsLeft) =>
        secondsLeft > 0
            ? $"{name} is already on the way to you (about {Duration(secondsLeft)} left)."
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

    /// <summary>
    /// Whether this invite from too far away sends the bot to the player (a friend, or a same-realm bot the find-group
    /// window is offering), rather than "meet up first".
    /// </summary>
    public static bool Travels(GamePlayer player, GameBot bot) =>
        player != null && bot != null &&
        Travels(bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper, IsFriend(player, bot),
            BotGroupInvite.CloseEnoughToInviteByName(player, bot), LookingForGroup(bot), player.Realm == bot.Realm, InOwnGroup(player, bot));

    /// <summary>
    /// The find-group window is offering this bot: a fresh LFG chat ad, or a solo waiting on group matchmaking.
    /// Realm and "already in a group" are checked by the pure <c>Travels</c> rule.
    /// </summary>
    public static bool LookingForGroup(GameBot bot) =>
        bot != null && (BotLfgAds.IsAdvertising(bot) || AutonomousObjectiveAssignments.IsAwaitingGroupMatchmaking(bot));

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

        // A friend in a group of its own turns the first invite down and takes a later one.
        if (!PassesRegroup(player, bot))
            return false;

        int zones = ZonesAway(player, bot);
        int delay = TravelDelayMs(zones);
        Trips[bot.DatabaseID] = new Trip(player, now, now + delay);
        string from = AutonomousLivingServer.PlaceOf(bot);
        ChatUtil.SendSystemMessage(player, OnTheWayMessage(bot.Name, Seconds(delay)));
        Log.Info($"BOT_FRIEND_TRAVEL bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{player.Name}\" result=started delay_ms={delay} zones_away={zones} " +
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

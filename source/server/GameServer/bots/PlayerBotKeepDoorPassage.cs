using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Issue #114 (Killaloe, v1.1.20): the bots in a real player's group (autonomous gamebots and companions) got stuck
    /// in keep gateways. The player passes a keep gate by clicking it (classic client: moved through; new client: the
    /// gate swings open) and PR #90 only carried bots within 1,200 units of the door at that moment. Bots further back,
    /// bots whose follow walk ends in the gate passage, and bots facing a gate that was rammed open but still blocked
    /// their way froze in the passage while the player went on.
    ///
    /// Now, when such a bot and the player who leads its group are on opposite sides of a keep door the bot may use,
    /// the bot is moved through that door the same way a player is (GameKeepDoor.TraverseDoor):
    /// - at once when the door is closed and belongs to the bot's realm (a closed gate is never walked through), and
    /// - when the bot made no headway for <see cref="StallMilliseconds"/> near any passable door (its realm's, or any
    ///   door that is open, such as a rammed one).
    /// Doors of an enemy realm that are still closed are never passed. Solo gamebots and bot-only groups are left to
    /// their own controllers. Each pass is logged as PLAYER_BOT_KEEP_DOOR_PASS.
    /// </summary>
    public static class PlayerBotKeepDoorPassage
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(PlayerBotKeepDoorPassage));

        /// <summary>A bot this far from a door (2D) may be passed through it. A gatehouse passage is about 600 long.</summary>
        public const int SearchRadius = 900;
        /// <summary>A closed door of the bot's own realm is passed at once from this close.</summary>
        public const int ClosedDoorReach = 350;
        /// <summary>The straight line from the bot to its leader passes this close to the door (it is the door between them).</summary>
        public const int CorridorWidth = 400;
        /// <summary>The leader is at most this far from the door (the door plane is endless; the door is not).</summary>
        public const int LeaderRadius = 3000;
        public const int MaxDoorHeightDifference = 250;
        /// <summary>No headway for this long near a passable door between bot and leader: the bot is passed through it.</summary>
        public const long StallMilliseconds = 3_000;
        public const int HeadwayDistance = 60;
        public const long CheckIntervalMilliseconds = 500;
        public const long CooldownMilliseconds = 3_000;
        /// <summary>A bot shooting at a target this close is fighting, not stalled.</summary>
        public const int FightRadius = 2_000;

        private sealed class State
        {
            public Vector3 At;
            public long Since;
            public long NextCheck;
            public long NextPass;
        }

        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        /// <summary>Which side of a door's plane a point is on (true: the side the door faces).</summary>
        public static bool InFront(Vector2 door, ushort heading, Vector2 point)
        {
            double angle = heading * Point2D.HEADING_TO_RADIAN;
            Vector2 facing = new((float)-Math.Sin(angle), (float)Math.Cos(angle));
            return Vector2.Dot(point - door, facing) >= 0;
        }

        /// <summary>Distance from <paramref name="point"/> to the segment <paramref name="a"/>-<paramref name="b"/>.</summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared();
            if (lengthSquared < 1f)
                return Vector2.Distance(point, a);
            float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
            return Vector2.Distance(point, a + ab * t);
        }

        /// <summary>
        /// Pure rule: the door lies between the bot and its leader (opposite sides of its plane, on the line between them,
        /// both near it).
        /// </summary>
        public static bool Separates(Vector2 door, ushort heading, Vector2 bot, Vector2 leader)
        {
            if (Vector2.Distance(bot, door) > SearchRadius || Vector2.Distance(leader, door) > LeaderRadius)
                return false;
            if (InFront(door, heading, bot) == InFront(door, heading, leader))
                return false;
            return DistanceToSegment(door, bot, leader) <= CorridorWidth;
        }

        public enum Reason { None, ClosedOwnDoor, Stalled }

        /// <summary>Pure rule: whether (and why) a separating door is passed now.</summary>
        public static Reason Decide(bool ownRealmDoor, bool doorOpen, float botToDoor, bool stalled)
        {
            if (!ownRealmDoor && !doorOpen)
                return Reason.None;
            if (ownRealmDoor && !doorOpen && botToDoor <= ClosedDoorReach)
                return Reason.ClosedOwnDoor;
            return stalled ? Reason.Stalled : Reason.None;
        }

        /// <summary>
        /// Pure rule: the bot stands still on purpose, so standing still is not a stall and it is not moved through a
        /// door: told to stay, casting, holding its healer post, or fighting (melee in reach or a ranged attack).
        /// </summary>
        public static bool HoldsPosition(bool toldToStay, bool casting, bool holdingHealerPost, bool fighting) =>
            toldToStay || casting || holdingHealerPost || fighting;

        /// <summary>From BotBrain.Think. True when the bot was moved through a door this turn.</summary>
        public static bool TryPass(GameBot bot)
        {
            if (bot == null || !bot.IsPlayerLedGroup || !bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active ||
                bot.IsStunned || bot.IsMezzed || bot.IsOnStableMasterRoute || bot.CurrentRegion == null)
                return false;
            GamePlayer leader = bot.PlayerGroupLeader;
            if (leader == null || !leader.IsAlive || leader.ObjectState != GameObject.eObjectState.Active ||
                leader.CurrentRegionID != bot.CurrentRegionID || bot.Group == null || bot.Group != leader.Group)
                return false;

            long now = GameLoop.GameLoopTime;
            State state = States.GetOrCreateValue(bot);
            Vector3 position = new(bot.X, bot.Y, bot.Z);
            if (state.Since == 0 || Vector3.Distance(position, state.At) >= HeadwayDistance)
            {
                state.At = position;
                state.Since = now;
            }
            if (now < state.NextCheck || now < state.NextPass)
                return false;
            state.NextCheck = now + CheckIntervalMilliseconds;

            // A bot standing still on purpose stays where it is (Squire review on PR #129): told to stay, casting,
            // holding its healer post, or fighting something on its own side of the door, in melee or at range.
            BotBrain brain = bot.Brain as BotBrain;
            bool fighting = bot.IsAttacking && bot.TargetObject is GameLiving target && target.IsAlive &&
                            bot.IsWithinRadius(target, bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance
                                ? FightRadius : Math.Max(bot.MeleeAttackRange, 200) + 100);
            bool casting = bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true;
            if (HoldsPosition(brain?.WalkState == eWalkState.Stay, casting, brain?.IsHoldingHealerPost == true, fighting))
            {
                state.At = position;
                state.Since = now;
                return false;
            }

            bool stalled = now - state.Since >= StallMilliseconds;
            Vector2 bot2 = new(bot.X, bot.Y), leader2 = new(leader.X, leader.Y);
            if (GameServer.KeepManager == null)
                return false;
            foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID))
            {
                foreach (GameKeepDoor door in keep.Doors.Values)
                {
                    if (door?.Component?.Keep == null || door.ObjectState != GameObject.eObjectState.Active ||
                        Math.Abs(door.Z - bot.Z) > MaxDoorHeightDifference)
                        continue;
                    Vector2 door2 = new(door.X, door.Y);
                    if (!Separates(door2, door.Heading, bot2, leader2))
                        continue;
                    bool own = door.Realm == bot.Realm;
                    bool open = door.State == eDoorState.Open;
                    Reason reason = Decide(own, open, Vector2.Distance(bot2, door2), stalled);
                    if (reason == Reason.None)
                        continue;
                    Vector3 from = position;
                    if (!door.PassFollower(bot, InFront(door2, door.Heading, leader2)))
                        continue;
                    state.NextPass = now + CooldownMilliseconds;
                    state.At = new Vector3(bot.X, bot.Y, bot.Z);
                    state.Since = now;
                    bot.movementComponent?.ForcePathReplot();
                    if (Log.IsInfoEnabled)
                        Log.Info($"PLAYER_BOT_KEEP_DOOR_PASS bot=\"{bot.Name}\" id={bot.DatabaseID} autonomous={bot.IsAutonomousWorldBot} " +
                                 $"leader=\"{leader.Name}\" door=\"{door.Name}\" doorId={door.DoorId} doorRealm={door.Realm} " +
                                 $"state={door.State} reason={reason} region={bot.CurrentRegionID} " +
                                 $"from={from.X:0},{from.Y:0},{from.Z:0} to={bot.X},{bot.Y},{bot.Z} leaderAt={leader.X},{leader.Y},{leader.Z}");
                    return true;
                }
            }
            return false;
        }
    }
}

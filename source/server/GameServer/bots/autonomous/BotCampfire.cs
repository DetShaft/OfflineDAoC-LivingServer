using System;

namespace DOL.GS
{
    /// <summary>
    /// A campfire a resting bot party lights at its camp (#56, idea 1). It is the client's own stock campfire
    /// (objects.csv model 2656 "Campfire", the one the world's WorldObject rows already use, so both clients draw it);
    /// nothing is added to the client. It burns while the party rests and a little after, then goes out by itself.
    /// The party's spread spots (GroupFormation) stand around it. Outdoors only: dungeons get no fire.
    /// </summary>
    public sealed class BotCampfire : GameStaticItem
    {
        public const ushort CampfireModel = 2656;
        /// <summary>The rest (CampAnchorPolicy.HoldMilliseconds) plus a minute of embers.</summary>
        public const long BurnMilliseconds = CampAnchorPolicy.HoldMilliseconds + 60_000;

        private long _outAt;
        private ECSGameTimer _timer;

        private BotCampfire() { }

        /// <summary>Whether a party's camp gets a fire: an outdoor camp only.</summary>
        public static bool Wanted(bool dungeonCamp, bool regionIsDungeon) => !dungeonCamp && !regionIsDungeon;

        /// <summary>Lights a fire at the spot, or keeps the given one burning longer when it still burns there.</summary>
        public static BotCampfire LightOrRekindle(BotCampfire current, ushort regionId, int x, int y, int z, ushort heading)
        {
            long now = GameLoop.GameLoopTime;
            if (current != null && current.ObjectState == eObjectState.Active && current.CurrentRegionID == regionId &&
                current.IsWithinRadius(new Point3D(x, y, z), 300))
            {
                current._outAt = now + BurnMilliseconds;
                return current;
            }
            Region region = WorldMgr.GetRegion(regionId);
            if (region == null)
                return null;
            var fire = new BotCampfire
            {
                Name = "campfire",
                Model = CampfireModel,
                CurrentRegion = region,
                X = x,
                Y = y,
                Z = z,
                Heading = heading,
                LoadedFromScript = true,
                _outAt = now + BurnMilliseconds
            };
            if (!fire.AddToWorld())
                return null;
            fire._timer = new ECSGameTimer(fire, fire.Burn, 5_000);
            return fire;
        }

        private int Burn(ECSGameTimer timer)
        {
            if (GameLoop.GameLoopTime < _outAt && ObjectState == eObjectState.Active)
                return timer.Interval;
            Delete();
            return 0;
        }

        public override void Delete()
        {
            _timer?.Stop();
            base.Delete();
        }
    }
}

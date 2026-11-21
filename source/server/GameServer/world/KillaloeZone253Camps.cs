using System;
using System.Linq;
using System.Numerics;
using DOL.Events;
using DOL.GS.DatabaseUpdate;

namespace DOL.GS
{
    /// <summary>
    /// Puts the Killaloe camp mobs (docs/KILLALOE-CAMPS.md) on the ground once the world has loaded. Their SQL Z is
    /// a guess from the nearby field spawns, because real zone files never go into the repo. Each camp mob is moved
    /// to the closest land polygon of the zone 253 navmesh under it and its spawn point follows, so respawns and
    /// roaming use the same spot. A camp mob with no land under it (water, cliff, outside the mesh) is removed for
    /// this run; its row stays in the database. Without a navmesh the guesses are left as they are.
    /// </summary>
    public static class KillaloeZone253Camps
    {
        public const ushort RegionId = 253;
        /// <summary>Half extents of the ground search: a little sideways, far up and down (cliffs and valleys).</summary>
        public const float SearchHorizontal = 48f;
        public const float SearchVertical = 2500f;
        /// <summary>Closer than this to the found floor counts as already on the ground.</summary>
        public const float OnGroundTolerance = 8f;

        /// <summary>Walkable polygons only, never water or disabled ones.</summary>
        private static readonly EDtPolyFlags[] LandOnly = [EDtPolyFlags.Walk, EDtPolyFlags.Swim | EDtPolyFlags.Disabled];

        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(KillaloeZone253Camps));

        public enum Placement
        {
            Keep,
            Move,
            Drop,
        }

        /// <summary>What to do with a camp mob standing at <paramref name="at"/> given the floor the navmesh found.</summary>
        public static Placement Decide(Vector3 at, Vector3? floor)
        {
            if (floor is not Vector3 ground)
                return Placement.Drop;
            if (Math.Abs(ground.Z - at.Z) <= OnGroundTolerance &&
                Vector2.Distance(new Vector2(ground.X, ground.Y), new Vector2(at.X, at.Y)) <= OnGroundTolerance)
                return Placement.Keep;
            return Placement.Move;
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                PlaceOnGround();
            }
            catch (Exception ex)
            {
                if (log.IsErrorEnabled)
                    log.Error("KILLALOE_CAMPS failed to put the camp mobs on the ground", ex);
            }
        }

        public static void PlaceOnGround()
        {
            GameNPC[] camps = WorldMgr.GetNPCsFromRegion(RegionId)
                .Where(npc => npc != null && npc.PackageID == KillaloeZone253Seeds.CampPackageId)
                .ToArray();
            if (camps.Length == 0)
                return;

            IPathfindingMgr nav = PathfindingProvider.Instance;
            int kept = 0, moved = 0, dropped = 0, noMesh = 0;
            foreach (GameNPC npc in camps)
            {
                Zone zone = npc.CurrentZone;
                if (!nav.IsAvailable || zone == null || !nav.HasNavmesh(zone))
                {
                    noMesh++;
                    continue;
                }

                Point3D spawn = npc.SpawnPoint ?? new Point3D(npc.X, npc.Y, npc.Z);
                Vector3 at = new(spawn.X, spawn.Y, spawn.Z);
                Vector3? floor = nav.GetClosestPoint(zone, at, SearchHorizontal, SearchHorizontal, SearchVertical, LandOnly);
                switch (Decide(at, floor))
                {
                    case Placement.Keep:
                        kept++;
                        break;
                    case Placement.Move:
                        Vector3 ground = floor.Value;
                        npc.SpawnPoint = new Point3D((int)ground.X, (int)ground.Y, (int)Math.Round(ground.Z));
                        npc.MoveTo(npc.CurrentRegionID, (int)ground.X, (int)ground.Y, (int)Math.Round(ground.Z), npc.Heading);
                        moved++;
                        break;
                    default:
                        if (log.IsInfoEnabled)
                            log.Info($"KILLALOE_CAMP_DROPPED id={npc.InternalID} name=\"{npc.Name}\" at={spawn.X},{spawn.Y},{spawn.Z} (no land on the navmesh)");
                        npc.Delete();
                        dropped++;
                        break;
                }
            }

            if (log.IsInfoEnabled)
                log.Info($"KILLALOE_CAMPS placed={camps.Length - dropped - noMesh} kept={kept} moved={moved} dropped={dropped} noMesh={noMesh}");
        }
    }
}

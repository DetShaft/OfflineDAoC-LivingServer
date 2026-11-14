using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;

namespace DOL.GS.Keeps
{
    /// <summary>
    /// Guards for battleground portal keeps the world has none for (issue #75). A battleground keep loads only its doors
    /// from KeepPosition (GameKeepComponent.LoadPositions); its guards are Mob rows, and the world has them at the
    /// Thidranki and Caledonia portal keeps but none at Murdaigean. Each realm's portal keep stands at the same spot in
    /// those battlegrounds (the Mob rows of the Thidranki and Caledonia portal keep guards are the same coordinates too),
    /// so a guardless portal keep takes the guards of its realm's portal keep in another battleground, at the same places.
    /// They are spawned at server start and never saved.
    /// Killaloe (region 253) is left alone: its keep staff are world GameNPC rows, which GameKeepGuard.LoadFromDatabase
    /// does not count, so keep.Guards stays 0. Copying Thidranki placeholders onto those pads would stack them on the
    /// captured staff. Murdaigean (region 251) still takes the copy.
    /// </summary>
    public static class BattlegroundPortalKeepGuards
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const int SameSpotTolerance = 64;
        /// <summary>Killaloe / the 15-19 battleground. Portal keeps here keep their own staff; they are never filled from another battleground.</summary>
        public const ushort KillaloeRegion = 253;
        private const string KeyPrefix = "battleground-portal-guard:";

        public readonly record struct PortalKeep(int KeepId, ushort Region, eRealm Realm, int X, int Y, int Z, ushort Heading, int Guards);

        /// <summary>True when this battleground's portal keeps must not receive copied Thidranki/Caledonia guards.</summary>
        public static bool SkipsPortalGuardCopy(ushort region) => region == KillaloeRegion;

        /// <summary>
        /// The portal keep whose guards a guardless battleground portal keep takes: the same realm's portal keep in
        /// another battleground, at the same spot and heading; the one with the most guards, then the lowest keep id.
        /// Region 253 (Killaloe) never takes a copy.
        /// </summary>
        public static PortalKeep? Donor(PortalKeep target, IEnumerable<PortalKeep> portalKeeps)
        {
            if (target.Guards > 0 || SkipsPortalGuardCopy(target.Region) || !BattlegroundBrackets.IsBattlegroundRegion(target.Region))
                return null;
            return portalKeeps
                .Where(keep => keep.Guards > 0 && keep.Region != target.Region && BattlegroundBrackets.IsBattlegroundRegion(keep.Region) &&
                               keep.Realm == target.Realm && keep.Heading == target.Heading &&
                               Math.Abs(keep.X - target.X) <= SameSpotTolerance &&
                               Math.Abs(keep.Y - target.Y) <= SameSpotTolerance &&
                               Math.Abs(keep.Z - target.Z) <= SameSpotTolerance)
                .OrderByDescending(keep => keep.Guards)
                .ThenBy(keep => keep.KeepId)
                .Select(keep => (PortalKeep?)keep)
                .FirstOrDefault();
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                Fill();
            }
            catch (Exception ex)
            {
                if (log.IsErrorEnabled)
                    log.Error("BG_PORTAL_GUARDS failed", ex);
            }
        }

        private static void Fill()
        {
            AbstractGameKeep[] keeps = GameServer.KeepManager?.GetAllKeeps()
                .Where(keep => keep.IsPortalKeep && BattlegroundBrackets.IsBattlegroundRegion(keep.Region))
                .ToArray() ?? [];
            // Taken before any keep is filled, so a filled keep never gives its guards on.
            PortalKeep[] spots = keeps.Select(Describe).ToArray();

            for (int index = 0; index < keeps.Length; index++)
            {
                AbstractGameKeep keep = keeps[index];
                if (spots[index].Guards > 0 || SkipsPortalGuardCopy(keep.Region))
                    continue;
                if (Donor(spots[index], spots) is not { } donorSpot)
                {
                    if (log.IsWarnEnabled)
                        log.Warn($"BG_PORTAL_GUARDS keep={keep.KeepID} name=\"{keep.Name}\" region={keep.Region} realm={keep.Realm} " +
                                 "no guards and no portal keep of its realm at the same spot in another battleground");
                    continue;
                }

                AbstractGameKeep donor = keeps.First(candidate => candidate.KeepID == donorSpot.KeepId);
                KeyValuePair<string, GameKeepGuard>[] sources;
                lock (donor.Guards)
                    sources = donor.Guards.Where(pair => IsPlacedGuard(pair.Value)).ToArray();

                int added = sources.Count(pair => Spawn(pair.Value, pair.Key, donor, keep));
                if (log.IsInfoEnabled)
                    log.Info($"BG_PORTAL_GUARDS keep={keep.KeepID} name=\"{keep.Name}\" region={keep.Region} realm={keep.Realm} " +
                             $"from_keep={donor.KeepID} from_region={donor.Region} guards={added}/{sources.Length}");
            }
        }

        private static PortalKeep Describe(AbstractGameKeep keep)
        {
            int guards;
            lock (keep.Guards)
                guards = keep.Guards.Count;
            return new(keep.KeepID, keep.Region, keep.OriginalRealm, keep.X, keep.Y, keep.Z, keep.Heading, guards);
        }

        // A guard of the keep's own Mob rows: not a patrol, a hookpoint guard or one spawned by a script.
        private static bool IsPlacedGuard(GameKeepGuard guard) =>
            guard != null && !guard.LoadedFromScript && guard.PatrolGroup == null && guard.HookPoint == null && guard.Position == null;

        private static bool Spawn(GameKeepGuard source, string sourceKey, AbstractGameKeep from, AbstractGameKeep to)
        {
            try
            {
                if (Activator.CreateInstance(source.GetType()) is not GameKeepGuard guard)
                    return false;
                Point3D spot = source.SpawnPoint;
                guard.X = spot.X + to.X - from.X;
                guard.Y = spot.Y + to.Y - from.Y;
                guard.Z = spot.Z + to.Z - from.Z;
                guard.Heading = source.SpawnHeading;
                guard.CurrentRegionID = to.Region;
                guard.Flags = source.Flags;
                guard.Component = new GameKeepComponent { Keep = to };
                guard.RefreshTemplate();
                guard.AddAbility(SkillBase.GetAbility(Abilities.ConfusionImmunity));
                lock (to.Guards)
                    to.Guards[KeyPrefix + sourceKey] = guard;
                return guard.AddToWorld();
            }
            catch (Exception ex)
            {
                if (log.IsErrorEnabled)
                    log.Error($"BG_PORTAL_GUARDS keep={to.KeepID} could not spawn {source.GetType().Name} from keep {from.KeepID}", ex);
                return false;
            }
        }
    }
}

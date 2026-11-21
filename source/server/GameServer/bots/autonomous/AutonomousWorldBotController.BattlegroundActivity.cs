using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>
    /// The battleground tour's activities (AutonomousBattlegroundActivity): grinding a battleground mob camp with other
    /// grinders of the realm, and hunting the outskirts for campers and small fights. Keep fights are the older tour
    /// behaviour in AutonomousWorldBotController.Battleground.cs.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        private CampDestination _battlegroundGrindCamp;
        private long _nextBattlegroundGrindPlan;
        private Vector2? _battlegroundAvoid;
        // The activity the phase chose; _battlegroundActivity is what the bot does (keep fights when a grind phase finds no camp).
        private AutonomousBattlegroundActivity.Activity? _battlegroundChosen;

        private AutonomousBattlegroundActivity.Activity UpdateBattlegroundActivity(GameBot bot, BattlegroundBrackets.Bracket bracket)
        {
            long key = AutonomousBattlegroundActivity.KeyOf(bot);
            AutonomousBattlegroundActivity.Activity activity = AutonomousBattlegroundActivity.ChooseNow(bot.Level,
                bracket.MinLevel, bracket.MaxLevel, key, GameLoop.GameLoopTime);
            // Compared with the phase's chosen activity, not a grind phase's keep-fights fallback (Squire's review of
            // #187): otherwise every turn of a fallback counted as a change and re-rolled the lurk point.
            if (_battlegroundChosen != activity)
            {
                Log.Info($"BG_ACTIVITY bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} " +
                         $"battleground={bracket.Name} tier={AutonomousBattlegroundActivity.TierOf(bot.Level, bracket.MinLevel, bracket.MaxLevel)} " +
                         $"activity={activity} previous={_battlegroundChosen?.ToString() ?? "none"}");
                _battlegroundRoamPoint = null;
                _battlegroundAvoid = null;
                _battlegroundChosen = activity;
                _battlegroundActivity = activity;
                AutonomousBattlegroundActivity.Report(key, activity);
            }
            return activity;
        }

        /// <summary>Out of the battleground (or out of its bracket): no activity, no grind camp.</summary>
        private void EndBattlegroundActivity(GameBot bot)
        {
            DropBattlegroundGrindCamp(bot);
            _battlegroundActivity = null;
            _battlegroundChosen = null;
            _battlegroundAvoid = null;
            _battlegroundSiegeResponse = AutonomousBattlegroundActivity.SiegeResponse.None;
            if (AutonomousBattlegroundParties.IsParty(bot.Group))
                bot.Group.RemoveMember(bot);
            AutonomousBattlegroundActivity.Forget(AutonomousBattlegroundActivity.KeyOf(bot));
        }

        private void DropBattlegroundGrindCamp(GameBot bot)
        {
            if (_battlegroundGrindCamp == null) return;
            if (_camp != null && string.Equals(_camp.Id, _battlegroundGrindCamp.Id, StringComparison.Ordinal))
            {
                AutonomousGoalDiagnostics.End(bot, GoalAttemptEnd.Reassigned, "Battleground phase changed");
                _camp = null;
                _campStartedTick = 0;
                _emptyCampSinceTick = 0;
                _patrolDestination = null;
            }
            _battlegroundGrindCamp = null;
            AutonomousBattlegroundActivity.ReleaseCamp(AutonomousBattlegroundActivity.KeyOf(bot));
        }

        /// <summary>
        /// A grinding phase: a battleground mob camp in level (green to the solo maximum), shared with up to three
        /// other grinders of the realm. Null when no camp is available, so the phase fights at the keep instead.
        /// </summary>
        private bool? GrindBattleground(BotBrain brain, GameBot bot, BattlegroundBrackets.Bracket bracket)
        {
            long now = GameLoop.GameLoopTime;
            long key = AutonomousBattlegroundActivity.KeyOf(bot);
            // The camp work below abandons an empty or outlevelled camp by clearing _camp.
            if (_battlegroundGrindCamp != null && (_camp == null || !string.Equals(_camp.Id, _battlegroundGrindCamp.Id, StringComparison.Ordinal)))
            {
                _battlegroundGrindCamp = null;
                AutonomousBattlegroundActivity.ReleaseCamp(key);
                _nextBattlegroundGrindPlan = Math.Max(_nextBattlegroundGrindPlan, now + 15_000);
            }
            if (_battlegroundGrindCamp == null)
            {
                if (now < _nextBattlegroundGrindPlan) return null;
                _nextBattlegroundGrindPlan = now + 30_000 + bot.ObjectID % 5_000;
                CampDestination picked = PickBattlegroundGrindCamp(bot, bracket, out int sharing);
                if (picked == null) return null;
                _battlegroundGrindCamp = picked;
                _camp = picked;
                _campStartedTick = now;
                _emptyCampSinceTick = 0;
                _patrolDestination = null;
                AutonomousBattlegroundActivity.HoldCamp(key, bracket.RegionId, bot.Realm, picked.Id, now);
                BeginCampDiagnostics(bot);
                Log.Info($"BG_GRIND_CAMP bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} " +
                         $"battleground={bracket.Name} camp=\"{picked.Id}\" monster=\"{picked.MonsterName}\" at={picked.X},{picked.Y} sharing={sharing}");
            }
            CampDestination camp = _battlegroundGrindCamp;
            AutonomousBattlegroundActivity.HoldCamp(key, bracket.RegionId, bot.Realm, camp.Id, now);
            if (Distance(bot.X, bot.Y, camp.X, camp.Y) > CampArrivalRadius)
            {
                IssueVariedRvrPath(bot, new Vector3(camp.X, camp.Y, camp.Z));
                int others = AutonomousBattlegroundActivity.GrindersAt(bracket.RegionId, bot.Realm, camp.Id, key, now);
                SetBattlegroundStatus(bot, bracket, $"Heading to grind {camp.MonsterName}",
                    others > 0 ? $"{others} of our realm grind there" : "Grinding the battleground's mobs between fights", camp.MonsterName);
                return true;
            }
            bool result = WorkCamp(brain, bot);
            if (_camp == null)
            {
                _battlegroundGrindCamp = null;
                AutonomousBattlegroundActivity.ReleaseCamp(key);
            }
            return result;
        }

        private CampDestination PickBattlegroundGrindCamp(GameBot bot, BattlegroundBrackets.Bracket bracket, out int sharing)
        {
            sharing = 0;
            long now = GameLoop.GameLoopTime;
            long key = AutonomousBattlegroundActivity.KeyOf(bot);
            ConColor maximum = NaturalMaximumTargetCon(1);
            bool InLevel(int level)
            {
                ConColor con = ConLevels.GetConColor(ConLevels.GetConLevel(bot.EffectiveLevel, level));
                return con >= ConColor.GREEN && con <= maximum;
            }
            CampCatalogCell[] cells = CampCatalogSnapshot()
                .Where(cell => cell.RegionId == bracket.RegionId && !cell.IsDungeon &&
                               (!_rejectedDungeonCamps.TryGetValue(cell.Id, out long until) || until <= now) &&
                               !AutonomousCampRouteQuarantine.IsQuarantined(cell.Id) &&
                               !AutonomousCampDangerBench.IsBenched(cell.Id, cell.RegionId, cell.MonsterName) &&
                               cell.Levels.Count(InLevel) * 2 >= cell.Levels.Length)
                .ToArray();
            if (cells.Length == 0) return null;
            int[] counts = cells.Select(cell => AutonomousBattlegroundActivity.GrindersAt(bracket.RegionId, bot.Realm, cell.Id, key, now)).ToArray();
            int index = AutonomousBattlegroundActivity.PickGrindCamp(counts, Random.Shared.NextDouble());
            if (index < 0) return null;
            sharing = counts[index];
            CampCatalogCell chosen = cells[index];
            return new CampDestination(chosen.Id, chosen.MonsterName, chosen.ZoneName, chosen.RegionId,
                chosen.X, chosen.Y, chosen.Z, chosen.LiveMobCount, false, true);
        }

        /// <summary>
        /// An outskirt hunt: roam the mob camps and the edge of the field for campers and small fights, away from the
        /// enemy portal keeps and from an enemy group the hunter just turned away from (FilterBattlegroundThreats).
        /// </summary>
        private bool HuntBattleground(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep central)
        {
            long now = GameLoop.GameLoopTime;
            Vector3 current = new(bot.X, bot.Y, bot.Z);
            if (!_battlegroundRoamPoint.HasValue || now >= _nextBattlegroundRoamTick ||
                Vector3.DistanceSquared(current, _battlegroundRoamPoint.Value) < 250 * 250)
            {
                Vector2[] enemyPortals = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
                    .Where(realm => realm != bot.Realm)
                    .Select(realm => BattlegroundBrackets.BotArrival(bracket, realm))
                    .Where(entry => entry != null)
                    .Select(entry => new Vector2(entry.X, entry.Y))
                    .ToArray();
                GameLocation own = BattlegroundBrackets.BotArrival(bracket, bot.Realm);
                Vector2 keep = central != null ? new Vector2(central.X, central.Y)
                    : enemyPortals.Append(own != null ? new Vector2(own.X, own.Y) : new Vector2(bot.X, bot.Y))
                        .Aggregate(Vector2.Zero, (sum, spot) => sum + spot) / (enemyPortals.Length + 1);
                double fieldRadius = enemyPortals.Append(own != null ? new Vector2(own.X, own.Y) : keep)
                    .Average(spot => Vector2.Distance(spot, keep));
                Vector2[] camps = CampCatalogSnapshot().Where(cell => cell.RegionId == bracket.RegionId && !cell.IsDungeon)
                    .Select(cell => new Vector2(cell.X, cell.Y)).ToArray();
                Vector2 huntPoint = AutonomousBattlegroundActivity.HuntPoint(keep, enemyPortals, camps, Math.Max(4_000, fieldRadius),
                    _battlegroundAvoid, Random.Shared);
                Vector3 anchor = new(huntPoint.X, huntPoint.Y, central?.Z ?? bot.Z);
                Zone zone = bot.CurrentRegion?.GetZone((int)anchor.X, (int)anchor.Y) ?? bot.CurrentZone;
                IPathfindingMgr nav = PathfindingProvider.Instance;
                if (zone != null && nav.IsAvailable && nav.HasNavmesh(zone))
                {
                    anchor = nav.GetClosestPoint(zone, anchor, 700, 700, 6000, nav.DefaultFilters) ?? anchor;
                    anchor = nav.GetRandomPoint(zone, anchor, 500, nav.DefaultFilters) ?? anchor;
                }
                _battlegroundRoamPoint = anchor;
                _nextBattlegroundRoamTick = now + 90_000 + bot.ObjectID % 30_000;
            }
            IssueVariedRvrPath(bot, _battlegroundRoamPoint.Value);
            SetBattlegroundStatus(bot, bracket, "Hunting the outskirts",
                _battlegroundAvoid.HasValue ? "Steering clear of an enemy group; looking for campers and small fights"
                    : "Looking for campers and small fights");
            return true;
        }

        private AutonomousBattlegroundActivity.SiegeResponse _battlegroundSiegeResponse;

        /// <summary>
        /// An active siege of the battleground's central keep (AutonomousBattlegroundActivity.RespondToSiege): the realm's
        /// own keep is defended at its posts, and a third realm's siege of another realm's keep draws half the hunters to
        /// its edges. Null when this bot does not respond, so its phase carries on.
        /// </summary>
        private bool? RespondToBattlegroundSiege(GameBot bot, BattlegroundBrackets.Bracket bracket, AbstractGameKeep central,
            AutonomousBattlegroundActivity.Activity activity)
        {
            long now = GameLoop.GameLoopTime;
            long key = AutonomousBattlegroundActivity.KeyOf(bot);
            bool own = central.Realm == bot.Realm;
            bool active = own
                ? AutonomousBattlegroundActivity.SiegeActive(AutonomousRvrEventLayer.LastKeepPressure(central), now)
                : central.Realm != eRealm.None && new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
                    .Where(realm => realm != bot.Realm && realm != central.Realm)
                    .Any(realm => AutonomousBattlegroundActivity.SiegeActive(AutonomousRvrEventLayer.LastKeepPressureBy(central, realm), now));
            double distance = Math.Sqrt(Vector2.DistanceSquared(new(bot.X, bot.Y), new(central.X, central.Y)));
            AutonomousBattlegroundActivity.SiegeResponse response = active
                ? AutonomousBattlegroundActivity.RespondToSiege(activity, own, distance, key)
                : AutonomousBattlegroundActivity.SiegeResponse.None;
            if (response != _battlegroundSiegeResponse)
            {
                Log.Info($"BG_SIEGE_RESPONSE bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} level={bot.Level} " +
                         $"keep=\"{central.Name}\" holder={(central.Realm == eRealm.None ? "none" : GlobalConstants.RealmToName(central.Realm))} " +
                         $"activity={activity} response={response} previous={_battlegroundSiegeResponse} distance={(int)distance}");
                // Back to the phase's own activity once the defense ends.
                if (response == AutonomousBattlegroundActivity.SiegeResponse.None && _battlegroundActivity != activity)
                {
                    _battlegroundActivity = activity;
                    AutonomousBattlegroundActivity.Report(key, activity);
                }
                _battlegroundSiegeResponse = response;
            }
            if (response == AutonomousBattlegroundActivity.SiegeResponse.None) return null;

            if (response == AutonomousBattlegroundActivity.SiegeResponse.Defend)
            {
                DropBattlegroundGrindCamp(bot);
                if (_battlegroundActivity != AutonomousBattlegroundActivity.Activity.KeepFights)
                {
                    _battlegroundActivity = AutonomousBattlegroundActivity.Activity.KeepFights;
                    AutonomousBattlegroundActivity.Report(key, AutonomousBattlegroundActivity.Activity.KeepFights);
                }
                _rvrIntent = AutonomousRvrEventLayer.Intent.Roam;
                SetBattlegroundStatus(bot, bracket, $"Defending {central.Name}", "Our keep is under siege");
                if (distance > 3_500)
                    return TravelToDefensivePost(bot, central, new(central.X, central.Y, central.Z));
                return HoldDefensiveKeepPost(bot, central);
            }

            // A third realm is sieging another realm's keep: come in on its edges; the hunter's own scan picks targets.
            _rvrIntent = AutonomousRvrEventLayer.Intent.Roam;
            if (distance > 3_000)
            {
                IssueVariedRvrPath(bot, new Vector3(central.X, central.Y, central.Z));
                SetBattlegroundStatus(bot, bracket, $"Heading for the siege at {central.Name}", "Picking off the edges of another realm's siege");
                return true;
            }
            return RoamBattleground(bot, bracket, central);
        }

        /// <summary>A party member stays with its leader (formation point; the leader keeps the party's plan).</summary>
        private bool FollowBattlegroundParty(GameBot bot, BattlegroundBrackets.Bracket bracket, GameBot leader,
            AutonomousBattlegroundActivity.Activity activity)
        {
            Vector3 point = AutonomousBotGroupCoordinator.FormationPoint(bot, new Vector3(leader.X, leader.Y, leader.Z), false);
            if (bot.IsWithinRadius(leader, 450) && !leader.IsMoving)
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
            }
            else IssuePath(bot, point, retargetWhileMoving: true);
            SetBattlegroundStatus(bot, bracket, $"With {leader.Name}'s party",
                activity == AutonomousBattlegroundActivity.Activity.OutskirtHunt ? "Hunting the outskirts as a small group"
                    : "Fighting around the central keep as a group");
            return true;
        }
    }
}

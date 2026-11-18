using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        // Issue #48: during a fight a grouped back-line support (Cleric, Druid, Healer, Shaman, Bard) holds a post out of
        // danger within casting range of the group (HealerPost) instead of trailing its formation slot behind the leader.

        private Vector3? _healerPost;
        private ushort _healerPostRegion;
        private long _nextHealerPostCheck;

        /// <summary>True while the bot walks to its healer post (the autonomous group controller leaves that walk alone).</summary>
        public bool IsWalkingToHealerPost
        {
            get
            {
                // A post left over from a fight the bot no longer holds a post in (it left the group, joined a raid,
                // stopped being support) must not keep the controller from stopping later movement.
                if (_healerPost == null)
                    return false;
                if (!HoldsHealerPostRole())
                {
                    _healerPost = null;
                    return false;
                }
                return Body?.IsMoving == true && Body.CurrentRegionID == _healerPostRegion;
            }
        }

        /// <summary>True while the bot holds a healer post in a fight (PlayerBotKeepDoorPassage leaves it there).</summary>
        public bool IsHoldingHealerPost =>
            _healerPost != null && Body?.CurrentRegionID == _healerPostRegion && HoldsHealerPostRole();

        /// <summary>A grouped back-line support outside a companion raid (raid squads keep their own posts).</summary>
        internal bool HoldsHealerPostRole() =>
            BotBody != null && BotPartyRoles.IsSupport(BotBody) && !CompanionRaid.IsMember(BotBody);

        private bool GroupFightingForHealerPost()
        {
            if (Body.InCombat || HasAggro)
                return true;
            if (AssistedPlayer is { IsAlive: true } leader && (leader.InCombat || leader.IsAttacking))
                return true;
            return Body.Group?.GetMembersInTheGroup().Any(member =>
                member != Body && member.IsAlive && (member.InCombat || member.IsAttacking)) == true;
        }

        /// <summary>
        /// Takes or keeps this fight's healer post. True when the post owns the bot's movement this turn (it stands at,
        /// or walks to, its post, or holds still while casting); false hands movement back to the caller: not a
        /// back-line support, no fight, nobody to cover, or no good post (the group is spread too wide).
        /// </summary>
        private bool HoldHealerPost()
        {
            if (!HoldsHealerPostRole() || !GroupFightingForHealerPost() || Body.CurrentRegion == null)
            {
                _healerPost = null;
                return false;
            }

            List<GameLiving> members = Body.Group.GetMembersInTheGroup()
                .Where(member => member != null && member != Body && member.IsAlive &&
                                 member.ObjectState == GameObject.eObjectState.Active &&
                                 member.CurrentRegionID == Body.CurrentRegionID &&
                                 Body.IsWithinRadius(member, GROUP_DEFENSE_ASSIST_RADIUS))
                .ToList();
            if (members.Count == 0)
            {
                _healerPost = null;
                return false;
            }

            // A cast in progress is never walked out of (a mobile song may walk).
            if ((Body.IsCasting || Body.castingComponent?.HasPendingSkillRequests == true) &&
                !BotSongTwistPolicy.HasMobileSongCast(Body))
                return true;

            bool tight = Body.CurrentRegion.IsDungeon || Body.CurrentZone?.IsDungeon == true;
            Vector2[] memberPoints = members.Select(member => new Vector2(member.X, member.Y)).ToArray();
            Vector2[] enemyPoints = HealerPostEnemies().Select(enemy => new Vector2(enemy.X, enemy.Y)).ToArray();

            long now = GameLoop.GameLoopTime;
            if (_healerPost is Vector3 held && _healerPostRegion == Body.CurrentRegionID &&
                (now < _nextHealerPostCheck || HealerPost.Keep(new Vector2(held.X, held.Y), memberPoints, enemyPoints, tight)))
            {
                WalkToHealerPost(held);
                return true;
            }

            _nextHealerPostCheck = now + HealerPost.RecheckMilliseconds;
            Vector2? pick = HealerPost.Pick(new Vector2(Body.X, Body.Y), memberPoints, enemyPoints, tight);
            if (pick is not Vector2 chosen)
            {
                _healerPost = null;
                return false;
            }

            Vector3 post = GroundHealerPost(chosen);
            _healerPost = post;
            _healerPostRegion = Body.CurrentRegionID;
            WalkToHealerPost(post);
            return true;
        }

        /// <summary>The enemies in this fight: members' targets, this bot's aggro, and monsters on a member or on this bot.</summary>
        private List<GameLiving> HealerPostEnemies()
        {
            var enemies = new HashSet<GameLiving>();
            void Consider(GameObject candidate)
            {
                if (candidate is GameLiving living && living != Body && living.IsAlive &&
                    living.ObjectState == GameObject.eObjectState.Active &&
                    living.CurrentRegionID == Body.CurrentRegionID &&
                    Body.Group?.IsInTheGroup(living) != true &&
                    Body.IsWithinRadius(living, GROUP_DEFENSE_ASSIST_RADIUS) &&
                    GameServer.ServerRules?.IsAllowedToAttack(Body, living, true) == true)
                    enemies.Add(living);
            }

            foreach (GameLiving member in Body.Group?.GetMembersInTheGroup() ?? [])
                Consider(member?.TargetObject);
            foreach (GameLiving aggro in AggroList.Keys)
                Consider(aggro);
            foreach (GameNPC npc in Body.GetNPCsInRadius(GROUP_DEFENSE_ASSIST_RADIUS))
            {
                if (npc?.TargetObject is GameLiving onWhom && (onWhom == Body || Body.Group?.IsInTheGroup(onWhom) == true))
                    Consider(npc);
            }
            return enemies.ToList();
        }

        /// <summary>The post on walkable ground, from where the bot stands (no point inside a wall or off a ledge).</summary>
        private Vector3 GroundHealerPost(Vector2 point)
        {
            var from = new Vector3(Body.X, Body.Y, Body.Z);
            var desired = new Vector3(point.X, point.Y, Body.Z);
            Zone zone = Body.CurrentRegion?.GetZone((int)point.X, (int)point.Y);
            if (zone == null || !PathfindingProvider.Instance.IsAvailable)
                return desired;
            return AutonomousNavigationSurface.MoveAlongGround(PathfindingProvider.Instance, zone, from, desired) ?? desired;
        }

        private void WalkToHealerPost(Vector3 post)
        {
            // A chase on an enemy or a follow on the leader would drag the bot off its post.
            StopSupportEnemyChase();
            if (Body.FollowTarget != null)
                Body.StopFollowing();
            if (Vector2.Distance(new Vector2(Body.X, Body.Y), new Vector2(post.X, post.Y)) <= HealerPost.ArriveTolerance)
                return;
            if (PathfindingProvider.Instance.IsAvailable &&
                Vector2.Distance(new Vector2(Body.X, Body.Y), new Vector2(post.X, post.Y)) > 300)
                Body.PathTo(post, Body.MaxSpeed);
            else
                Body.WalkTo(post, Body.MaxSpeed);
        }
    }
}

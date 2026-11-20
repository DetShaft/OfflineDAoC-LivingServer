using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>
    /// Issue #165: fight mode for the bots in a real player's group (friended or guildmate gamebots and /spawn
    /// helpers alike). Before it, these bots only mirrored the player's attack: a mezzed player, or one who had
    /// targeted an enemy without swinging yet, left them standing in FOLLOW beside legal targets (1.1.23 siege,
    /// 2026-11-19 17:47: four of Ferac's bots idle with no target while three others fought 3,000 units away).
    ///
    /// One state per group, scanned at most twice a second by whichever bot thinks first:
    /// - enter when a groupmate is attacked or an enemy-realm player, bot or pet is near the player;
    /// - keep a shared focus for the damage dealers (<see cref="GroupFightPolicy.PickFocus"/>);
    /// - leave <see cref="GroupFightPolicy.ExitDelayMilliseconds"/> after the last hostile.
    /// Frontier, battlegrounds and the neutral raid dungeons (Darkness Falls, Summoner's Hall), where realms
    /// fight on sight (AutonomousWorldBotController.IsBattleground). Only enemy-realm targets are picked or start the
    /// mode, so PvE pulls stay the player's. /raid 40 and /raid 80 keep their own
    /// engagement and leash rules (CompanionPvpEngagement, TemporaryCompanionRecovery), so they are left out.
    /// The keep lord's bot shield (#157, #160) is honored: a shielded lord is never a candidate, and
    /// BotBrain.AddToAggroList refuses it anyway.
    ///
    /// #170 (bot-sim findings): fight mode keeps running when the player dies, around a group anchor (the living
    /// groupmate nearest the player's body, <see cref="Anchor"/>); a better-role focus replaces a sticky one
    /// (<see cref="GroupFightPolicy.ShouldReplaceFocus"/>).
    /// </summary>
    public static class PlayerGroupFightMode
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(PlayerGroupFightMode));

        public const int ScanIntervalMilliseconds = 500;

        private sealed class GroupState
        {
            public bool Active;
            public long EnteredAt, LastHostile, NextScan;
            public GameLiving Focus;
            public GameLiving[] Candidates = Array.Empty<GameLiving>();
            public readonly Dictionary<GameLiving, long> Threats = new();
            /// <summary>#176: when the anchor last saw each enemy (line of sight), for <see cref="GroupFightPolicy.StillInSight"/>.</summary>
            public readonly Dictionary<GameLiving, long> LastSeen = new();
        }

        private static readonly ConditionalWeakTable<Group, GroupState> Groups = new();

        public static bool Enabled => ServerProperties.Properties.GROUP_FIGHT_MODE;

        public static int ChaseDistance => GroupFightPolicy.ChaseDistance(ServerProperties.Properties.GROUP_FIGHT_CHASE_DISTANCE);

        /// <summary>
        /// The real player who leads this bot's group, when the bot is with the group; otherwise null. The player may be
        /// dead (#170): the bot then stays within the assist radius of the group's anchor instead. With
        /// <paramref name="requireNear"/> false the bot may be any distance away in the same region, so the chase leash
        /// can still call back a bot that ran past the assist radius.
        /// </summary>
        public static GamePlayer LeaderOf(GameBot bot, bool requireNear = true)
        {
            if (bot?.IsPlayerLedGroup != true || bot.PlayerGroupLeader is not { } leader || !bot.IsAlive ||
                bot.ObjectState != GameObject.eObjectState.Active || bot.Group == null || bot.Group != leader.Group ||
                !bot.Group.IsInTheGroup(bot) || bot.CurrentRegionID != leader.CurrentRegionID ||
                bot.IsOnStableMasterRoute || bot.IsReturningAfterRelease)
                return null;
            GameLiving anchor = Anchor(leader);
            return anchor != null && (!requireNear || bot.IsWithinRadius(anchor, DOL.AI.Brain.BotBrain.GROUP_DEFENSE_ASSIST_RADIUS)) ? leader : null;
        }

        /// <summary>
        /// The point fight mode measures from: the player while alive; once the player is dead (and not released), the
        /// living groupmate nearest the player's body, so the group keeps fighting together where the player fell
        /// (#170: the 8v8 harness saw every bot go idle 13 s in, "leader is dead; not attacked itself").
        /// </summary>
        public static GameLiving Anchor(GamePlayer leader)
        {
            if (leader?.Group == null || leader.ObjectState != GameObject.eObjectState.Active) return null;
            if (leader.IsAlive) return leader;
            GameLiving[] members = leader.Group.GetMembersInTheGroup()
                .Where(m => m != leader && m.CurrentRegionID == leader.CurrentRegionID && m.ObjectState == GameObject.eObjectState.Active)
                .ToArray();
            int index = GroupFightPolicy.AnchorIndex(members.Select(m => m.IsAlive).ToArray(),
                members.Select(m => (int)leader.GetDistanceTo(m)).ToArray());
            return index < 0 ? null : members[index];
        }

        /// <summary>Fight mode runs for this player's group: on, grouped, anchored in a battleground, not a companion raid.</summary>
        public static bool InScope(GamePlayer leader) =>
            Enabled && leader?.Group is { IsCompanionRaid: false } && Anchor(leader) is { } anchor &&
            AutonomousWorldBotController.IsBattleground(anchor);

        /// <summary>A groupmate was attacked (BotBrain.NotifyNearbyGroupBots): remember the attacker and enter fight mode.</summary>
        public static void OnMemberAttacked(GameLiving member, AttackData attack)
        {
            if (!Enabled || member?.Group is not Group group || attack?.Attacker is not GameLiving attacker ||
                !attack.CausesCombat || !attacker.IsAlive || attacker == member)
                return;
            GamePlayer leader = LeaderOfGroup(group);
            // Enemy realms only (players, gamebots, their pets, keep guards): a monster hitting the group is ordinary
            // PvE defense and stays with the aggro rules, so fight mode never turns a PvE fight into free target picks.
            GameLiving anchor = Anchor(leader);
            if (!InScope(leader) || attacker.CurrentRegionID != anchor.CurrentRegionID ||
                !BotSiegeRuntime.LegalEnemy(anchor, attacker))
                return;
            GroupState state = Groups.GetOrCreateValue(group);
            long now = GameLoop.GameLoopTime;
            lock (state)
            {
                if (state.Threats.Count >= 64 && !state.Threats.ContainsKey(attacker))
                    state.Threats.Remove(state.Threats.MinBy(p => p.Value).Key);
                state.Threats[attacker] = now + GroupFightPolicy.ThreatMemoryMilliseconds;
                state.LastHostile = now;
                if (!state.Active)
                    Enter(state, leader, anchor, now, $"member_attacked member=\"{member.Name}\" attacker=\"{attacker.Name}\"");
            }
        }

        /// <summary>Is fight mode on for this bot's group right now (after this think's scan)?</summary>
        public static bool Observe(GameBot bot, GamePlayer leader, out GameLiving focus)
        {
            focus = null;
            if (bot?.Group == null || !InScope(leader) || bot.Group != leader.Group) return false;
            GroupState state = Groups.GetOrCreateValue(bot.Group);
            lock (state)
            {
                Scan(state, leader, Anchor(leader), GameLoop.GameLoopTime);
                focus = state.Focus;
                return state.Active;
            }
        }

        /// <summary>
        /// #170 follow-up: fight mode is on for this bot's group, so it chooses the bots' targets. The companion group
        /// focus (CompanionPvpEngagement.Observe) then stays out: both ordered pulls every half second and the bots flipped
        /// between the two targets (the leader-mezzed harness saw casters alternate targets on every cast).
        /// </summary>
        public static bool ActiveFor(GameBot bot)
        {
            if (bot?.Group == null || !Enabled || !Groups.TryGetValue(bot.Group, out GroupState state)) return false;
            lock (state) return state.Active;
        }

        /// <summary>The group's shared focus, so crowd control never mezzes the target the group is about to hit.</summary>
        public static bool IsFocus(GameLiving actor, GameLiving target)
        {
            if (target == null || CompanionPvpEngagement.Character(actor) is not GameBot bot || bot.Group == null ||
                !Groups.TryGetValue(bot.Group, out GroupState state))
                return false;
            lock (state) return state.Active && state.Focus == target;
        }

        /// <summary>The live candidates of this think (threats and visible enemies near the player).</summary>
        public static GameLiving[] Candidates(GameBot bot)
        {
            if (bot?.Group == null || !Groups.TryGetValue(bot.Group, out GroupState state)) return Array.Empty<GameLiving>();
            lock (state) return state.Candidates;
        }

        /// <summary>
        /// The player's target, first in fight mode while the player is attacking it
        /// (PlayerLedPullCoordinator.FindLeaderTarget). Owner's decision 2026-11-20 (#170): a target the player has only
        /// selected (mezzed, or not swinging yet) no longer comes first; the bots pick by role instead. Putting it first
        /// won 6 of 18 leader-mezzed harness fights, against 18 of 18 without.
        /// </summary>
        public static GameLiving LeaderTarget(GamePlayer leader) => PlayerLedPullCoordinator.FindLeaderTarget(leader);

        public static GroupFightPolicy.Candidate Describe(GameLiving anchor, Group group, GameLiving target)
        {
            ICharacterClass cls = target is GamePlayer player ? player.CharacterClass : (target as GameBot)?.CharacterClass;
            eCharacterClass? id = cls == null ? null : (eCharacterClass)cls.ID;
            return new GroupFightPolicy.Candidate(
                GroupFightPolicy.RoleOf(id, cls?.ClassType == eClassType.ListCaster),
                target.HealthPercent,
                anchor == null ? 0 : (int)anchor.GetDistanceTo(target),
                target.IsMezzed,
                HittingBackLine(group, target));
        }

        /// <summary>The enemy is hitting or casting at one of the group's healers or casters.</summary>
        public static bool HittingBackLine(Group group, GameLiving enemy)
        {
            if (group == null || !(enemy.IsAttacking || enemy.IsCasting) || enemy.TargetObject is not GameLiving victim ||
                !group.IsInTheGroup(victim))
                return false;
            return IsBackLine(victim);
        }

        /// <summary>A groupmate who fights from the back: a healer or support class, or a list caster.</summary>
        public static bool IsBackLine(GameLiving member)
        {
            if (member is GameBot bot)
                return BotPartyRoles.IsSupport(bot) || bot.CharacterClass?.ClassType == eClassType.ListCaster;
            if (member is GamePlayer player && player.CharacterClass is { } cls)
                return cls.ClassType == eClassType.ListCaster || BotPartyRoles.HoldsBackLine((eCharacterClass)cls.ID);
            return false;
        }

        /// <summary>
        /// #177: the distance from <paramref name="target"/> to the group's nearest living back-line member (not
        /// <paramref name="self"/>) within the assist radius of the anchor; <see cref="int.MaxValue"/> when there is none.
        /// </summary>
        public static int DistanceToBackLine(GameLiving self, GameLiving anchor, GameLiving target)
        {
            if (self?.Group == null || anchor == null || target == null) return int.MaxValue;
            int best = int.MaxValue;
            foreach (GameLiving member in self.Group.GetMembersInTheGroup())
            {
                if (member == null || member == self || !member.IsAlive || member.ObjectState != GameObject.eObjectState.Active ||
                    member.CurrentRegionID != anchor.CurrentRegionID || target.CurrentRegionID != member.CurrentRegionID ||
                    !anchor.IsWithinRadius(member, DOL.AI.Brain.BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) || !IsBackLine(member))
                    continue;
                best = Math.Min(best, (int)member.GetDistanceTo(target));
            }
            return best;
        }

        /// <summary>
        /// #177: the nearest living groupmate fighting in melee (not back line, attacking) within the assist radius of the
        /// anchor, or null.
        /// </summary>
        public static GameLiving NearestFightingMelee(GameLiving self, GameLiving anchor)
        {
            if (self?.Group == null || anchor == null) return null;
            GameLiving best = null;
            double bestDistance = double.MaxValue;
            foreach (GameLiving member in self.Group.GetMembersInTheGroup())
            {
                if (member == null || member == self || !member.IsAlive || member.ObjectState != GameObject.eObjectState.Active ||
                    !member.IsAttacking || member.CurrentRegionID != self.CurrentRegionID || IsBackLine(member) ||
                    !anchor.IsWithinRadius(member, DOL.AI.Brain.BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
                    continue;
                double distance = self.GetDistanceTo(member);
                if (distance < bestDistance)
                {
                    best = member;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// Attackable for the player's group: alive, active, same region, not a shielded keep lord. Judged from the
        /// anchor (the server rules refuse a dead attacker, so a dead player cannot judge it).
        /// </summary>
        public static bool Hostile(GameLiving anchor, GameLiving target) =>
            anchor?.IsAlive == true && target?.IsAlive == true && target.ObjectState == GameObject.eObjectState.Active &&
            target.CurrentRegionID == anchor.CurrentRegionID && target is not GuardLord { ShieldedFromBots: true } &&
            GameServer.ServerRules.IsAllowedToAttack(anchor, target, true);

        private static GamePlayer LeaderOfGroup(Group group)
        {
            // A player alone with no bots has nothing for fight mode to drive.
            foreach (GameLiving member in group.GetMembersInTheGroup())
                if (member is GameBot { PlayerGroupLeader: { } led } && group.IsInTheGroup(led)) return led;
            return null;
        }

        private static void Scan(GroupState state, GamePlayer leader, GameLiving anchor, long now)
        {
            if (now < state.NextScan || anchor == null) return;
            state.NextScan = now + ScanIntervalMilliseconds;
            int chase = ChaseDistance;
            Group group = leader.Group;

            foreach (GameLiving old in state.Threats.Where(p => p.Value <= now || !p.Key.IsAlive ||
                         p.Key.ObjectState != GameObject.eObjectState.Active).Select(p => p.Key).ToArray())
                state.Threats.Remove(old);

            var candidates = new List<GameLiving>();
            foreach (GameLiving threat in state.Threats.Keys)
                if (Hostile(anchor, threat) && anchor.IsWithinRadius(threat, chase)) candidates.Add(threat);
            // Proactive: enemy-realm players, gamebots and their pets in sight of the anchor (not monsters or guards:
            // those join through the threat list once they hit someone).
            IEnumerable<GameLiving> near = anchor.GetPlayersInRadius((ushort)chase).Cast<GameLiving>()
                .Concat(anchor.GetNPCsInRadius((ushort)chase).Where(BotPvpCrowdControl.PlayerLike));
            // #176: an enemy that stepped behind terrain stays a candidate for GroupFightPolicy.SightMemoryMilliseconds, so
            // a ridge between the player and the focus no longer empties or flips the focus and idles the group.
            foreach (GameLiving gone in state.LastSeen.Where(p => !GroupFightPolicy.StillInSight(false, now, p.Value) ||
                         !p.Key.IsAlive || p.Key.ObjectState != GameObject.eObjectState.Active).Select(p => p.Key).ToArray())
                state.LastSeen.Remove(gone);
            foreach (GameLiving enemy in near)
            {
                if (enemy == leader || enemy == anchor || enemy.IsStealthed || candidates.Contains(enemy) ||
                    !BotSiegeRuntime.LegalEnemy(anchor, enemy) || !Hostile(anchor, enemy))
                    continue;
                bool visible = BotSiegeRuntime.Visible(anchor, enemy);
                if (visible)
                {
                    if (state.LastSeen.Count >= 128 && !state.LastSeen.ContainsKey(enemy))
                        state.LastSeen.Remove(state.LastSeen.MinBy(p => p.Value).Key);
                    state.LastSeen[enemy] = now;
                }
                if (GroupFightPolicy.StillInSight(visible, now, state.LastSeen.TryGetValue(enemy, out long seen) ? seen : -1))
                    candidates.Add(enemy);
            }
            state.Candidates = candidates.ToArray();

            if (candidates.Count > 0)
            {
                state.LastHostile = now;
                if (!state.Active)
                {
                    GameLiving first = candidates.OrderBy(anchor.GetDistanceTo).First();
                    Enter(state, leader, anchor, now, $"enemy_near enemy=\"{first.Name}\" distance={(int)anchor.GetDistanceTo(first)}");
                }
            }
            else if (state.Active && GroupFightPolicy.ShouldExit(now, state.LastHostile))
            {
                state.Active = false;
                state.Focus = null;
                state.LastSeen.Clear();
                Log.Info($"GROUP_FIGHT_MODE event=exit leader=\"{leader.Name}\" seconds={(now - state.EnteredAt) / 1000} " +
                         $"quiet_ms={now - state.LastHostile} region={anchor.CurrentRegionID}");
                return;
            }
            if (!state.Active || candidates.Count == 0) return;

            // A sticky focus, so damage stays on one target: kept while it is a live, unmezzed candidate, unless a
            // better-role enemy has come into reach (#170: the first focus was the enemy melee that charged in first,
            // and the group never turned to the healers and casters behind it).
            GroupFightPolicy.Candidate[] described = candidates.Select(t => Describe(anchor, group, t)).ToArray();
            int best = GroupFightPolicy.PickFocus(described);
            GameLiving focus = state.Focus;
            int current = focus == null ? -1 : Array.IndexOf(state.Candidates, focus);
            if (current >= 0 && described[current].Mezzed) current = -1;
            string why = current < 0 ? (focus == null ? "new" : "lost") : "upgrade";
            if (best >= 0 && (current < 0 || GroupFightPolicy.ShouldReplaceFocus(described[current], described[best])))
            {
                GameLiving previous = focus;
                focus = candidates[best];
                GroupFightPolicy.Candidate c = described[best];
                Log.Info($"GROUP_FIGHT_FOCUS leader=\"{leader.Name}\" target=\"{focus.Name}\" role={GroupFightPolicy.Label(c.Role)} " +
                         $"hp={c.HealthPercent} distance={c.DistanceFromLeader} candidates={candidates.Count} " +
                         $"mezzed={described.Count(d => d.Mezzed)} why={why} previous=\"{previous?.Name ?? "-"}\" " +
                         $"leaderAlive={leader.IsAlive} anchor=\"{anchor.Name}\"");
            }
            else if (current < 0)
                focus = null;
            state.Focus = focus;
        }

        private static void Enter(GroupState state, GamePlayer leader, GameLiving anchor, long now, string reason)
        {
            state.Active = true;
            state.EnteredAt = now;
            state.Focus = null;
            Log.Info($"GROUP_FIGHT_MODE event=enter leader=\"{leader.Name}\" reason={reason} leaderAlive={leader.IsAlive} " +
                     $"anchor=\"{anchor.Name}\" chase={ChaseDistance} region={anchor.CurrentRegionID} at={anchor.X},{anchor.Y},{anchor.Z}");
        }
    }
}

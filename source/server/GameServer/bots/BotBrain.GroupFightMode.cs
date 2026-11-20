using System.Linq;
using DOL.GS;
using DOL.GS.Keeps;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private GameLiving _lastFightPick;
        private GroupFightPolicy.PickReason _lastFightReason;
        private GameLiving _lastLeashDrop;

        /// <summary>
        /// The target of the player's last /pull (PlayerLedPullCoordinator.Begin). The leash never drops it, at any
        /// distance or age, unlike the companion focus that /pull also sets (2,000 units, 30 s).
        /// </summary>
        internal GameLiving PlayerPullOrder { get; set; }

        /// <summary>
        /// Issue #165: this bot's fight-mode turn in the player's group. Runs the group scan, keeps the bot within the
        /// chase distance of the player, and, while fight mode is on, gives an idle bot a target of its own:
        /// the player's target first while they attack it, a tank's peel, then the group focus.
        /// Healers go through AssistPlayerAttack, which keeps them support-first (#48). Crowd control keeps its own
        /// turn (#152) and skips the focus (<see cref="PlayerGroupFightMode.IsFocus"/>).
        /// Returns true when the bot used this think to close up on its melee (#177); the caller then ends the think.
        /// </summary>
        private bool ThinkGroupFightMode()
        {
            GameBot bot = BotBody;
            // Found at any distance: the leash must still reach a bot that chased past the assist radius.
            GamePlayer leader = PlayerGroupFightMode.LeaderOf(bot, requireNear: false);
            if (leader == null || !PlayerGroupFightMode.InScope(leader) ||
                FSM.GetCurrentState() == FSM.GetState(eFSMStateType.PASSIVE))
                return false;
            bool active = PlayerGroupFightMode.Observe(bot, leader, out GameLiving focus);
            // The player, or once the player is dead the groupmate nearest their body (#170).
            GameLiving anchor = PlayerGroupFightMode.Anchor(leader);
            if (anchor == null) return false;
            // A bot with a siege job (engineer, ram crew) answers to its job, not to the chase distance.
            if (BotSiegeRuntime.Assigned(bot)) return false;

            // The leash never drops the player's selected target or their /pull order. In fight mode it covers every
            // target; once fight mode has ended (the enemy ran out of range, so no hostile is seen near the player) it
            // keeps holding enemy players and bots only, so a bot cannot chase a runner across the zone (the harness
            // saw bots 8,600 units out) while PvE pulls stay untouched.
            EnforceGroupFightLeash(bot, leader, anchor, leader.IsAlive ? leader.TargetObject as GameLiving : null, enemyPlayersOnly: !active);
            if (!active || !bot.IsWithinRadius(anchor, GROUP_DEFENSE_ASSIST_RADIUS)) return false;
            GameLiving leaderTarget = PlayerGroupFightMode.LeaderTarget(leader);
            // Never cut a cast short (a heal, a mez) to take a new target.
            if (Body.IsCasting) return false;
            if (CloseUpOnMelee(bot, anchor)) return true;
            // A door the player is hitting is the door crew's: a caster that cannot hurt it takes the focus instead (#133).
            if (leaderTarget != null && !CanAggroTarget(leaderTarget)) leaderTarget = null;

            GameLiving pick = null;
            GroupFightPolicy.PickReason reason = GroupFightPolicy.PickReason.None;
            if (leaderTarget != null && GroupFightPolicy.FollowsLeaderTarget(true, leaderTarget.IsMezzed))
            {
                pick = leaderTarget;
                reason = GroupFightPolicy.PickReason.Leader;
            }
            else
            {
                bool tank = BotPartyRoles.IsTank(bot);
                GameLiving current = LiveCombatTarget(anchor);
                GameLiving[] candidates = PlayerGroupFightMode.Candidates(bot);
                if (tank && (current == null || !PlayerGroupFightMode.HittingBackLine(bot.Group, current)))
                {
                    GroupFightPolicy.Candidate[] described = candidates
                        .Select(t => PlayerGroupFightMode.Describe(anchor, bot.Group, t)).ToArray();
                    int peel = GroupFightPolicy.PickPeel(described);
                    if (peel >= 0)
                    {
                        pick = candidates[peel];
                        reason = GroupFightPolicy.PickReason.Peel;
                    }
                }
                if (pick == null && focus != null && focus != current)
                {
                    // An idle bot takes the focus. A busy one takes it only when the focus is of a better role (#170:
                    // a bot with any live target used to keep it and never reached the healer fight mode had picked).
                    // A tank on a peel target stays on it.
                    bool peeling = tank && current != null && PlayerGroupFightMode.HittingBackLine(bot.Group, current);
                    if (current == null || !peeling && GroupFightPolicy.ShouldSwitchToFocus(
                            PlayerGroupFightMode.Describe(anchor, bot.Group, current).Role,
                            PlayerGroupFightMode.Describe(anchor, bot.Group, focus).Role))
                    {
                        pick = focus;
                        reason = GroupFightPolicy.PickReason.Focus;
                    }
                }
                // Otherwise: already fighting something legal near the group, no switching back and forth.
            }
            if (pick == null || !Body.IsWithinRadius(pick, GROUP_DEFENSE_ASSIST_RADIUS)) return false;
            // #177: never pick what the support leash would drop at once (the player's target is exempt there too).
            if (reason != GroupFightPolicy.PickReason.Leader && !PlayerGroupFightMode.IsBackLine(bot) &&
                GroupFightPolicy.BeyondSupport(PlayerGroupFightMode.DistanceToBackLine(bot, anchor, pick), PlayerGroupFightMode.ChaseDistance,
                    false, HittingGroup(bot, anchor, pick, PlayerGroupFightMode.ChaseDistance)))
                return false;
            if (Body.TargetObject == pick && HasAggro) return false;
            // Healers stay support-first (#48): they join only when the idle-assist rule allows it, and are otherwise
            // left to heal (AssistPlayerAttack would stop their casts to hold support).
            if (BotPartyRoles.IsSupport(bot) && !MaySupportIdleCombatAssist(pick)) return false;

            // A switch must outweigh the aggro the old target has built up, or the attack turn keeps the old one
            // (CleanUpAggroListAndGetHighestModifiedThreat keeps the current target on a tie or better).
            if (HasAggro && Body.TargetObject is GameLiving old && old != pick)
                AddToAggroList(pick, AggroList.Values.Select(a => a.Base).DefaultIfEmpty(0).Max() + 500);
            AssistPlayerAttack(pick);
            if (pick != _lastFightPick || reason != _lastFightReason)
            {
                _lastFightPick = pick;
                _lastFightReason = reason;
                GroupFightPolicy.Candidate c = PlayerGroupFightMode.Describe(anchor, bot.Group, pick);
                log.Info($"GROUP_FIGHT_TARGET bot=\"{bot.Name}\" class=\"{bot.ClassName}\" role={BotRoleLabel(bot)} " +
                         $"leader=\"{leader.Name}\" target=\"{pick.Name}\" why={reason.ToString().ToLowerInvariant()} " +
                         $"targetRole={GroupFightPolicy.Label(c.Role)} hp={c.HealthPercent} fromLeader={c.DistanceFromLeader} " +
                         $"fromBot={(int)Body.GetDistanceTo(pick)} leaderAttacking={leader.IsAttacking} leaderMezzed={leader.IsMezzed} leaderAlive={leader.IsAlive} " +
                         $"engaged={Body.TargetObject == pick && HasAggro}");
            }
            return false;
        }

        private long _nextCloseUpLog;

        /// <summary>
        /// #177: a ranged caster (not a healer: those hold a healer post among the group) that is not under attack walks
        /// to within <see cref="GroupFightPolicy.CasterHold"/> of the nearest groupmate fighting in melee once it is
        /// farther than <see cref="GroupFightPolicy.CasterReach"/>. A caster under attack keeps its kite rules (#136).
        /// </summary>
        private bool CloseUpOnMelee(GameBot bot, GameLiving anchor)
        {
            if (BotPartyRoles.IsSupport(bot) || !PrefersCurrentSpellRange() || Body.IsCrowdControlled) return false;
            GameLiving melee = PlayerGroupFightMode.NearestFightingMelee(bot, anchor);
            int distance = melee == null ? int.MaxValue : (int)Body.GetDistanceTo(melee);
            bool casting = Body.IsCasting || Body.castingComponent?.HasPendingSkillRequests == true;
            if (!GroupFightPolicy.CasterClosesIn(casting, RecentDirectAttacker() != null, distance)) return false;
            BotBody.WakeRecoveryRest();
            Body.StopAttack();
            Body.Follow(melee, (short)GroupFightPolicy.CasterHold, (short)MAX_AGGRO_LIST_DISTANCE);
            long now = GameLoop.GameLoopTime;
            if (now >= _nextCloseUpLog)
            {
                _nextCloseUpLog = now + 5_000;
                log.Info($"GROUP_FIGHT_CLOSE_UP bot=\"{bot.Name}\" class=\"{bot.ClassName}\" melee=\"{melee.Name}\" distance={distance} " +
                         $"hold={GroupFightPolicy.CasterHold} anchor=\"{anchor.Name}\"");
            }
            return true;
        }

        /// <summary>The target is hitting this bot, or a groupmate within the chase distance of the anchor.</summary>
        private static bool HittingGroup(GameBot bot, GameLiving anchor, GameLiving target, int chase) =>
            (target.IsAttacking || target.IsCasting) && target.TargetObject is GameLiving victim &&
            (victim == bot || victim.Group == bot.Group && bot.Group != null && anchor.IsWithinRadius(victim, chase));

        private static string BotRoleLabel(GameBot bot) =>
            BotPartyRoles.IsSupport(bot) ? "support" : BotPartyRoles.IsTank(bot) ? "tank" :
            bot.CharacterClass?.ClassType == eClassType.ListCaster ? "caster" : "damage";

        /// <summary>The bot's current fight, when it is a legal target within the chase distance of the anchor.</summary>
        private GameLiving LiveCombatTarget(GameLiving anchor) =>
            HasAggro && Body.TargetObject is GameLiving current && PlayerGroupFightMode.Hostile(anchor, current) &&
            anchor.IsWithinRadius(current, PlayerGroupFightMode.ChaseDistance) ? current : null;

        /// <summary>
        /// The chase leash: an aggro entry or target past the chase distance from the player is dropped and the bot
        /// regroups, unless it is the player's selected or attacked target, an ordered pull the player stands behind
        /// (/pull), or it is hitting this bot or a groupmate near the player. Outside fight mode
        /// (<paramref name="enemyPlayersOnly"/>) only enemy-realm players and bots are leashed.
        /// Doors and siege engines are left to the siege rules.
        /// </summary>
        private void EnforceGroupFightLeash(GameBot bot, GamePlayer leader, GameLiving anchor, GameLiving leaderSelected, bool enemyPlayersOnly)
        {
            int chase = PlayerGroupFightMode.ChaseDistance;
            GameLiving leaderAttacking = PlayerLedPullCoordinator.FindLeaderTarget(leader);
            // AssistPlayerAttack goes through OrderPull, so the ordered pull is usually automatic: fight mode's own pick or
            // the companion group focus. Only one the player stands behind is exempt: their /pull (PlayerPullOrder, at
            // any distance or age), or CompanionPvpEngagement.Focused (their attack or their pet's, within 2,000 units).
            // Exempting every ordered pull let bots chase a fleeing enemy 8,600 units in the harness (#170 follow-up).
            GameLiving ordered = ActiveOrderedPullTarget;
            GameLiving playerOrdered = ordered != null && (ordered == PlayerPullOrder || CompanionPvpEngagement.Focused(bot, ordered))
                ? ordered : null;
            GameLiving[] watched = AggroList.Keys.ToArray();
            if (Body.TargetObject is GameLiving current && !watched.Contains(current)) watched = watched.Append(current).ToArray();
            foreach (GameLiving target in watched)
            {
                if (target == null || target is GameKeepDoor or GameSiegeWeapon || !target.IsAlive) continue;
                if (enemyPlayersOnly && (!BotPvpCrowdControl.PlayerLike(target) || !BotSiegeRuntime.LegalEnemy(anchor, target))) continue;
                bool sameRegion = target.CurrentRegionID == anchor.CurrentRegionID;
                int distance = sameRegion ? (int)anchor.GetDistanceTo(target) : int.MaxValue;
                if (sameRegion && distance <= chase && (enemyPlayersOnly || PlayerGroupFightMode.IsBackLine(bot))) continue;
                bool hittingGroup = sameRegion && HittingGroup(bot, anchor, target, chase);
                bool players = target == leaderSelected || target == leaderAttacking || target == playerOrdered;
                // #177: the support leash, for the front line only, in fight mode only. A caster's own targets stay
                // within its spell range anyway.
                int fromBackLine = !enemyPlayersOnly && sameRegion && distance <= chase && !PlayerGroupFightMode.IsBackLine(bot)
                    ? PlayerGroupFightMode.DistanceToBackLine(bot, anchor, target) : int.MaxValue;
                bool beyondSupport = GroupFightPolicy.BeyondSupport(fromBackLine, chase, players, hittingGroup);
                if (!beyondSupport && !GroupFightPolicy.BeyondLeash(distance, chase, players, hittingGroup)) continue;

                // An automatic ordered pull on it ends too, or the attack turn takes it straight back.
                if (target == ordered) CancelOrderedPull(target);
                RemoveFromAggroList(target);
                if (Body.TargetObject == target)
                {
                    Body.StopAttack();
                    if (Body.IsCasting && Body.castingComponent?.SpellHandler?.Spell?.IsHarmful == true && !PvpControlInFlight)
                        Body.StopCurrentSpellcast();
                    Body.TargetObject = null;
                }
                if (!HasAggro)
                {
                    Body.ControlledBrain?.Follow(Body);
                    FSM.SetCurrentState(eFSMStateType.FOLLOW);
                }
                if (_lastLeashDrop != target)
                {
                    _lastLeashDrop = target;
                    log.Info($"GROUP_FIGHT_LEASH bot=\"{bot.Name}\" leader=\"{leader.Name}\" target=\"{target.Name}\" " +
                             $"fromLeader={(sameRegion ? distance : -1)} botFromAnchor={(int)Body.GetDistanceTo(anchor)} anchor=\"{anchor.Name}\" chase={chase} " +
                             $"why={(beyondSupport ? "support" : "chase")} fromBackLine={(fromBackLine == int.MaxValue ? -1 : fromBackLine)}");
                }
            }
        }
    }
}

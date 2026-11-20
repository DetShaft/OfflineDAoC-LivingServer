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
        /// Issue #165: this bot's fight-mode turn in the player's group. Runs the group scan, keeps the bot within the
        /// chase distance of the player, and, while fight mode is on, gives an idle bot a target of its own:
        /// the player's target first (even while the player is mezzed), a tank's peel, then the group focus.
        /// Healers go through AssistPlayerAttack, which keeps them support-first (#48). Crowd control keeps its own
        /// turn (#152) and skips the focus (<see cref="PlayerGroupFightMode.IsFocus"/>).
        /// </summary>
        private void ThinkGroupFightMode()
        {
            GameBot bot = BotBody;
            GamePlayer leader = PlayerGroupFightMode.LeaderOf(bot);
            if (leader == null || !PlayerGroupFightMode.InScope(leader) ||
                FSM.GetCurrentState() == FSM.GetState(eFSMStateType.PASSIVE))
                return;
            bool active = PlayerGroupFightMode.Observe(bot, leader, out GameLiving focus);
            // The player, or once the player is dead the groupmate nearest their body (#170).
            GameLiving anchor = PlayerGroupFightMode.Anchor(leader);
            if (anchor == null) return;
            // A bot with a siege job (engineer, ram crew) answers to its job, not to the chase distance.
            if (BotSiegeRuntime.Assigned(bot)) return;

            if (!active) return;
            GameLiving leaderTarget = PlayerGroupFightMode.LeaderTarget(leader);
            // The leash runs only in fight mode, and never on the player's selected target or an ordered pull.
            EnforceGroupFightLeash(bot, leader, anchor, leader.IsAlive ? leader.TargetObject as GameLiving : null);
            // Never cut a cast short (a heal, a mez) to take a new target.
            if (Body.IsCasting) return;
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
            if (pick == null || !Body.IsWithinRadius(pick, GROUP_DEFENSE_ASSIST_RADIUS)) return;
            if (Body.TargetObject == pick && HasAggro) return;
            // Healers stay support-first (#48): they join only when the idle-assist rule allows it, and are otherwise
            // left to heal (AssistPlayerAttack would stop their casts to hold support).
            if (BotPartyRoles.IsSupport(bot) && !MaySupportIdleCombatAssist(pick)) return;

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
        }

        private static string BotRoleLabel(GameBot bot) =>
            BotPartyRoles.IsSupport(bot) ? "support" : BotPartyRoles.IsTank(bot) ? "tank" :
            bot.CharacterClass?.ClassType == eClassType.ListCaster ? "caster" : "damage";

        /// <summary>The bot's current fight, when it is a legal target within the chase distance of the anchor.</summary>
        private GameLiving LiveCombatTarget(GameLiving anchor) =>
            HasAggro && Body.TargetObject is GameLiving current && PlayerGroupFightMode.Hostile(anchor, current) &&
            anchor.IsWithinRadius(current, PlayerGroupFightMode.ChaseDistance) ? current : null;

        /// <summary>
        /// The chase leash: an aggro entry or target past the chase distance from the player is dropped and the bot
        /// regroups, unless it is the player's selected or attacked target, this bot's ordered pull (/pull, /group
        /// attack), or it is hitting this bot or a groupmate near the player. Fight mode only.
        /// Doors and siege engines are left to the siege rules.
        /// </summary>
        private void EnforceGroupFightLeash(GameBot bot, GamePlayer leader, GameLiving anchor, GameLiving leaderSelected)
        {
            int chase = PlayerGroupFightMode.ChaseDistance;
            GameLiving leaderAttacking = PlayerLedPullCoordinator.FindLeaderTarget(leader);
            GameLiving ordered = ActiveOrderedPullTarget;
            GameLiving[] watched = AggroList.Keys.ToArray();
            if (Body.TargetObject is GameLiving current && !watched.Contains(current)) watched = watched.Append(current).ToArray();
            foreach (GameLiving target in watched)
            {
                if (target == null || target is GameKeepDoor or GameSiegeWeapon || !target.IsAlive) continue;
                bool sameRegion = target.CurrentRegionID == anchor.CurrentRegionID;
                int distance = sameRegion ? (int)anchor.GetDistanceTo(target) : int.MaxValue;
                if (sameRegion && distance <= chase) continue;
                bool hittingGroup = (target.IsAttacking || target.IsCasting) && target.TargetObject is GameLiving victim &&
                    (victim == bot || victim.Group == bot.Group && bot.Group != null && anchor.IsWithinRadius(victim, chase));
                bool players = target == leaderSelected || target == leaderAttacking || target == ordered;
                if (!GroupFightPolicy.BeyondLeash(distance, chase, players, hittingGroup)) continue;

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
                             $"fromLeader={(sameRegion ? distance : -1)} botFromAnchor={(int)Body.GetDistanceTo(anchor)} anchor=\"{anchor.Name}\" chase={chase}");
                }
            }
        }
    }
}

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
            // A bot with a siege job (engineer, ram crew) answers to its job, not to the chase distance.
            if (BotSiegeRuntime.Assigned(bot)) return;

            if (!active) return;
            GameLiving leaderTarget = PlayerGroupFightMode.LeaderTarget(leader);
            // The leash runs only in fight mode, and never on the player's selected target or an ordered pull.
            EnforceGroupFightLeash(bot, leader, leader.TargetObject as GameLiving);
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
            else if (HasLiveCombatTarget(leader))
            {
                // Already fighting something legal near the player: no switching back and forth.
                return;
            }
            else
            {
                if (BotPartyRoles.IsTank(bot))
                {
                    GameLiving[] candidates = PlayerGroupFightMode.Candidates(bot);
                    GroupFightPolicy.Candidate[] described = candidates
                        .Select(t => PlayerGroupFightMode.Describe(leader, bot.Group, t)).ToArray();
                    int peel = GroupFightPolicy.PickPeel(described);
                    if (peel >= 0)
                    {
                        pick = candidates[peel];
                        reason = GroupFightPolicy.PickReason.Peel;
                    }
                }
                if (pick == null && focus != null)
                {
                    pick = focus;
                    reason = GroupFightPolicy.PickReason.Focus;
                }
            }
            if (pick == null || !Body.IsWithinRadius(pick, GROUP_DEFENSE_ASSIST_RADIUS)) return;
            if (Body.TargetObject == pick && HasAggro) return;
            // Healers stay support-first (#48): they join only when the idle-assist rule allows it, and are otherwise
            // left to heal (AssistPlayerAttack would stop their casts to hold support).
            if (BotPartyRoles.IsSupport(bot) && !MaySupportIdleCombatAssist(pick)) return;

            AssistPlayerAttack(pick);
            if (pick != _lastFightPick || reason != _lastFightReason)
            {
                _lastFightPick = pick;
                _lastFightReason = reason;
                GroupFightPolicy.Candidate c = PlayerGroupFightMode.Describe(leader, bot.Group, pick);
                log.Info($"GROUP_FIGHT_TARGET bot=\"{bot.Name}\" class=\"{bot.ClassName}\" role={BotRoleLabel(bot)} " +
                         $"leader=\"{leader.Name}\" target=\"{pick.Name}\" why={reason.ToString().ToLowerInvariant()} " +
                         $"targetRole={GroupFightPolicy.Label(c.Role)} hp={c.HealthPercent} fromLeader={c.DistanceFromLeader} " +
                         $"fromBot={(int)Body.GetDistanceTo(pick)} leaderAttacking={leader.IsAttacking} leaderMezzed={leader.IsMezzed} " +
                         $"engaged={Body.TargetObject == pick && HasAggro}");
            }
        }

        private static string BotRoleLabel(GameBot bot) =>
            BotPartyRoles.IsSupport(bot) ? "support" : BotPartyRoles.IsTank(bot) ? "tank" :
            bot.CharacterClass?.ClassType == eClassType.ListCaster ? "caster" : "damage";

        private bool HasLiveCombatTarget(GamePlayer leader) =>
            HasAggro && Body.TargetObject is GameLiving current && PlayerGroupFightMode.Hostile(leader, current) &&
            leader.IsWithinRadius(current, PlayerGroupFightMode.ChaseDistance);

        /// <summary>
        /// The chase leash: an aggro entry or target past the chase distance from the player is dropped and the bot
        /// regroups, unless it is the player's selected or attacked target, this bot's ordered pull (/pull, /group
        /// attack), or it is hitting this bot or a groupmate near the player. Fight mode only.
        /// Doors and siege engines are left to the siege rules.
        /// </summary>
        private void EnforceGroupFightLeash(GameBot bot, GamePlayer leader, GameLiving leaderSelected)
        {
            int chase = PlayerGroupFightMode.ChaseDistance;
            GameLiving leaderAttacking = PlayerLedPullCoordinator.FindLeaderTarget(leader);
            GameLiving ordered = ActiveOrderedPullTarget;
            GameLiving[] watched = AggroList.Keys.ToArray();
            if (Body.TargetObject is GameLiving current && !watched.Contains(current)) watched = watched.Append(current).ToArray();
            foreach (GameLiving target in watched)
            {
                if (target == null || target is GameKeepDoor or GameSiegeWeapon || !target.IsAlive) continue;
                bool sameRegion = target.CurrentRegionID == leader.CurrentRegionID;
                int distance = sameRegion ? (int)leader.GetDistanceTo(target) : int.MaxValue;
                if (sameRegion && distance <= chase) continue;
                bool hittingGroup = (target.IsAttacking || target.IsCasting) && target.TargetObject is GameLiving victim &&
                    (victim == bot || victim.Group == bot.Group && bot.Group != null && leader.IsWithinRadius(victim, chase));
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
                             $"fromLeader={(sameRegion ? distance : -1)} botFromLeader={(int)Body.GetDistanceTo(leader)} chase={chase}");
                }
            }
        }
    }
}

using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private GameLiving _losBlockedTarget;
        private long _losBlockedUntil;
        private long _nextLosWalkLog, _nextPetSkipLog;

        /// <summary>#176: a cast at <paramref name="target"/> failed its line of sight check (GameBot.OnCastSpellLosCheckFail).</summary>
        internal void OnSpellLineOfSightFailed(GameObject target)
        {
            if (target is not GameLiving living) return;
            _losBlockedTarget = living;
            _losBlockedUntil = GameLoop.GameLoopTime + BotLineOfSightPolicy.HoldMilliseconds;
        }

        /// <summary>
        /// #176: after a failed line of sight check on this target, a ranged bot walks toward it until it is in sight, is
        /// within <see cref="BotLineOfSightPolicy.ApproachDistance"/>, or the hold runs out. True: this think was the walk.
        /// </summary>
        private bool WalkForLineOfSight(GameLiving target)
        {
            GameLiving blocked = _losBlockedTarget;
            if (blocked == null) return false;
            long now = GameLoop.GameLoopTime;
            bool same = blocked == target;
            bool inSight = same && now < _losBlockedUntil && BotSiegeRuntime.Visible(Body, target);
            int distance = (int)Body.GetDistanceTo(target);
            if (!BotLineOfSightPolicy.Walks(now, _losBlockedUntil, same, inSight, distance))
            {
                _losBlockedTarget = null;
                return false;
            }
            if (Body.IsCrowdControlled || Body.IsCasting) return false;
            BotBody?.WakeRecoveryRest();
            Body.StopAttack();
            if (Body.FollowTarget != target)
                Body.Follow(target, (short)BotLineOfSightPolicy.ApproachDistance, (short)MAX_AGGRO_LIST_DISTANCE);
            if (now >= _nextLosWalkLog)
            {
                _nextLosWalkLog = now + 5_000;
                log.Info($"BOT_LOS_WALK bot=\"{Body.Name}\" target=\"{target.Name}\" distance={distance} " +
                         $"heldMs={_losBlockedUntil - now} group_fight={(BotBody != null && PlayerGroupFightMode.ActiveFor(BotBody))}");
            }
            return true;
        }

        /// <summary>
        /// #175: in fight mode an enemy pet (one that hit the bot, a melee peel, or the top of the aggro list) does not take
        /// the bot off its fight-mode pick, the ordered pull fight mode gave it. A tank's protection target is kept,
        /// pets included: the tank guards the back line. Returns the target the bot should fight.
        /// </summary>
        private GameLiving KeepFightModePickOverPet(GameLiving chosen, GameLiving protectionTarget)
        {
            if (chosen == null || BotBody == null || !PlayerGroupFightMode.ActiveFor(BotBody)) return chosen;
            if (chosen == protectionTarget && IsTankClass) return chosen;
            GameLiving pick = ActiveOrderedPullTarget;
            if (pick == null || pick == chosen || !pick.IsAlive || pick.IsMezzed ||
                !Body.IsWithinRadius(pick, GROUP_DEFENSE_ASSIST_RADIUS))
                return chosen;
            if (!GroupFightPolicy.PetYields(PetRole(chosen), PetRole(pick))) return chosen;
            long now = GameLoop.GameLoopTime;
            if (now >= _nextPetSkipLog)
            {
                _nextPetSkipLog = now + 5_000;
                log.Info($"GROUP_FIGHT_PET_SKIP bot=\"{Body.Name}\" class=\"{BotBody.ClassName}\" pet=\"{chosen.Name}\" " +
                         $"keeps=\"{pick.Name}\" fromBot={(int)Body.GetDistanceTo(pick)} peel={chosen == protectionTarget}");
            }
            return pick;
        }

        /// <summary>A player's or gamebot's pet (not a bot, not a monster or keep guard) counts as a pet; anything else as a fighter.</summary>
        private static GroupFightPolicy.TargetRole PetRole(GameLiving target) =>
            target is GameNPC npc and not GameBot && npc.Brain is IControlledBrain
                ? GroupFightPolicy.TargetRole.Pet
                : GroupFightPolicy.TargetRole.Fighter;
    }
}

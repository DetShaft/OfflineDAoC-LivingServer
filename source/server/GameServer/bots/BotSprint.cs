using System;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// Issue #128 (Jay, v1.1.20): the bots in a real player's group sprint as players do.
    /// - Alongside: while the player who leads the group sprints and moves, a following bot that is out of combat runs at
    ///   sprint speed too (it no longer trails behind).
    /// - Chasing: a bot running down a moving enemy that is beyond its melee reach sprints for up to
    ///   <see cref="ChaseMaxMilliseconds"/>, then rests the sprint for <see cref="ChaseCooldownMilliseconds"/>.
    /// Sprint is the player's x1.3. It needs a tenth of the bot's endurance, as a player's sprint does; it does not drain
    /// it, since bots never spend endurance on movement (GameBot.EnduranceRegenerationTimerCallback). Solo gamebots and
    /// bot-only groups are unchanged.
    /// </summary>
    public static class BotSprint
    {
        public const double Factor = 1.3;
        public const long ChaseMaxMilliseconds = 10_000;
        public const long ChaseCooldownMilliseconds = 15_000;
        /// <summary>A chase sprints only while the target is this far past the bot's melee reach.</summary>
        public const int ChaseGap = 150;
        public const int MinimumEndurancePercent = 10;

        private sealed class State
        {
            public long ChaseSince;
            public long ChaseCooldownUntil;
        }

        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        /// <summary>Pure rule: sprint speed never lowers a speed some other rule already raised.</summary>
        public static short Speed(short normal, short current, bool sprinting) =>
            !sprinting || normal <= 0 ? current : (short)Math.Min(short.MaxValue, Math.Max(current, (int)Math.Round(normal * Factor)));

        /// <summary>Pure rule: how a chase sprint runs. Returns whether it sprints now and updates the window.</summary>
        public static bool ChaseWindow(bool chasing, long now, ref long since, ref long cooldownUntil)
        {
            if (!chasing)
            {
                if (since != 0)
                    cooldownUntil = Math.Max(cooldownUntil, now + ChaseCooldownMilliseconds / 3);
                since = 0;
                return false;
            }
            if (now < cooldownUntil)
                return false;
            if (since == 0)
                since = now;
            if (now - since <= ChaseMaxMilliseconds)
                return true;
            since = 0;
            cooldownUntil = now + ChaseCooldownMilliseconds;
            return false;
        }

        private static bool CanSprint(GameBot bot) =>
            bot is { IsPlayerLedGroup: true, IsAlive: true } && !bot.IsOnStableMasterRoute && !bot.IsStealthed &&
            !bot.IsRecoveryResting && !bot.IsSitting &&
            (bot.MaxEndurance <= 0 || bot.EndurancePercent >= MinimumEndurancePercent);

        public static bool FollowsSprintingLeader(GameBot bot) =>
            CanSprint(bot) && !bot.InCombat && !bot.IsAttacking &&
            bot.PlayerGroupLeader is { IsAlive: true, IsSprinting: true, IsMoving: true } leader &&
            leader.CurrentRegionID == bot.CurrentRegionID && !leader.IsOnHorse;

        private static bool IsChasing(GameBot bot) =>
            bot.IsAttacking && bot.TargetObject is GameLiving target && target.IsAlive && target.IsMoving &&
            bot.FollowTarget == target && bot.ActiveWeaponSlot != eActiveWeaponSlot.Distance &&
            !bot.IsWithinRadius(target, bot.MeleeAttackRange + ChaseGap);

        public static bool Chases(GameBot bot)
        {
            if (!CanSprint(bot))
                return false;
            State state = States.GetOrCreateValue(bot);
            return ChaseWindow(IsChasing(bot), GameLoop.GameLoopTime, ref state.ChaseSince, ref state.ChaseCooldownUntil);
        }

        public static bool Kites(GameBot bot) =>
            bot.KiteSprintUntil > GameLoop.GameLoopTime && bot.IsAlive && !bot.IsStealthed && !bot.IsOnStableMasterRoute;

        /// <summary>For NpcMovementComponent.MaxSpeed.</summary>
        public static short Apply(GameBot bot, short normal, short current)
        {
            if (bot == null)
                return current;
            // Issue #136: a caster running from a melee attacker sprints, whoever leads it (all bot kinds).
            if (Kites(bot))
                return Speed(normal, current, true);
            if (!bot.IsPlayerLedGroup)
                return current;
            return Speed(normal, current, FollowsSprintingLeader(bot) || Chases(bot));
        }
    }
}

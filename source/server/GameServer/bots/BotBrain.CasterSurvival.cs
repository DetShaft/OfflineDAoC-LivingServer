using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        // Issue #136: casters kite player-shaped melee instead of meleeing, pets peel for their owner, and pure melee
        // and shield users peel for their healers and casters. Rules and scope: BotCasterSurvival.

        private readonly long[] _kiteLegStarts = new long[BotCasterSurvival.MaxLegsPerWindow];
        private int _kiteLegIndex;
        private GameLiving _kiteFrom;
        private long _kiteLegUntil;
        /// <summary>Set for one turn: the attacker is held, so the caster casts and does not fall back to melee.</summary>
        private bool _survivalHoldsRange;
        private long _nextPeelScan;
        private GameLiving _peelTarget;

        // The pure casters (ListCaster) that prefer spell range; Minstrels and other hybrids keep their own melee.
        private bool KitesMeleeThreats => BotBody?.CharacterClass?.ClassType == eClassType.ListCaster && PrefersCurrentSpellRange() &&
            // A Necromancer fights through its servant; the servant peels instead.
            BotBody.CharacterClass.ID != (int)eCharacterClass.Necromancer;

        /// <summary>The nearest player-shaped enemy in melee reach that is after this bot, or null.</summary>
        private GameLiving FindMeleeThreat()
        {
            int reach = Body.MeleeAttackRange + BotCasterSurvival.ThreatReachPadding;
            GameLiving recent = RecentDirectAttacker();
            IEnumerable<GameLiving> nearby = Body.GetPlayersInRadius((ushort)reach).Cast<GameLiving>()
                .Concat(Body.GetNPCsInRadius((ushort)reach).Where(BotPvpCrowdControl.PlayerLike));
            GameLiving best = null;
            double bestDistance = double.MaxValue;
            foreach (GameLiving enemy in nearby)
            {
                if (enemy == null || enemy == Body || !enemy.IsAlive || enemy.ObjectState != GameObject.eObjectState.Active ||
                    enemy.IsStealthed || enemy == Body.ControlledBrain?.Body ||
                    enemy.TargetObject != Body || !(enemy.IsAttacking || enemy == recent) ||
                    !GameServer.ServerRules.IsAllowedToAttack(Body, enemy, true))
                    continue;
                double distance = Body.GetDistanceTo(enemy);
                if (distance < bestDistance)
                {
                    best = enemy;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>The bot's pet goes after whoever is in melee on its owner (all pet classes, all bot kinds).</summary>
        private void PeelWithPet(GameLiving threat)
        {
            if (threat == null || Body.ControlledBrain?.Body is not { IsAlive: true } pet ||
                !IsValidPetCombatTarget(pet, threat))
                return;
            CommandPetAttack(threat);
        }

        private IEnumerable<Spell> KnownControlSpells(GameLiving target) =>
            (Body.Spells ?? Enumerable.Empty<Spell>())
                .Where(spell => spell != null && spell.Level <= Body.Level &&
                    BotCasterSurvival.KindOf(spell) != BotCasterSurvival.ControlKind.None &&
                    spell.Target is eSpellTarget.ENEMY or eSpellTarget.AREA or eSpellTarget.CONE &&
                    !BotSpellPower.BlocksAttackerRotation(BotBody, spell) &&
                    !BotCasterSurvival.Immune(target, BotCasterSurvival.KindOf(spell)) &&
                    Body.GetSkillDisabledDuration(spell) <= 0 &&
                    Body.IsWithinRadius(target, spell.Target == eSpellTarget.AREA && spell.Range <= 0
                        ? Math.Max(1, spell.Radius) : Math.Max(1, spell.CalculateEffectiveRange(Body))) &&
                    !LivingHasEffect(target, spell))
                .OrderBy(spell => BotCasterSurvival.KindOf(spell))
                .ThenByDescending(spell => spell.Level);

        private Spell InstantControlFor(GameLiving target) =>
            KnownControlSpells(target).FirstOrDefault(spell => spell.IsInstantCast && Body.Mana >= BotBody.PowerCost(spell));

        private Spell QuickcastControlFor(GameLiving target) => BotBody.QuickcastReady
            ? KnownControlSpells(target).FirstOrDefault(spell => spell.CastTime > 0 && Body.Mana >= 2 * BotBody.PowerCost(spell))
            : null;

        private Spell QuickcastNukeFor(GameLiving target) => BotBody.QuickcastReady
            ? (Body.HarmfulSpells ?? Enumerable.Empty<Spell>())
                .Where(spell => spell != null && spell.CastTime > 0 && spell.Level <= Body.Level &&
                    BotCasterPriority.IsDamage(spell) && spell.Target == eSpellTarget.ENEMY &&
                    Body.GetSkillDisabledDuration(spell) <= 0 && Body.Mana >= 2 * BotBody.PowerCost(spell) &&
                    !IsOutclassedDamageSpell(spell) &&
                    Body.IsWithinRadius(target, Math.Max(1, spell.CalculateEffectiveRange(Body))))
                .OrderByDescending(spell => spell.Level).FirstOrDefault()
            : null;

        private bool CastSurvivalSpell(GameLiving target, Spell spell, bool quickcast)
        {
            if (spell == null)
                return false;
            if (quickcast && !BotBody.ArmQuickcast(spell))
                return false;
            GameObject previous = Body.TargetObject;
            Body.TargetObject = target;
            if (spell.CastTime > 0)
            {
                Body.StopAttack();
                Body.StopMovingOnPath();
                Body.StopMoving();
            }
            bool started = CheckOffensiveSpells(spell);
            Body.TargetObject = previous;
            if (!started && quickcast)
                BotBody.DisarmQuickcast();
            if (started && log.IsDebugEnabled)
                log.Debug($"BOT_KITE bot={Body.Name} action={(quickcast ? "quickcast" : "instant")} spell={spell.Name} on={target.Name}");
            return started;
        }

        private bool StartKiteLeg(GameLiving threat, bool countsToBudget)
        {
            Vector3? destination = null;
            Zone zone = Body.CurrentZone;
            var nav = PathfindingProvider.Instance;
            bool navmesh = zone != null && nav.IsAvailable && nav.HasNavmesh(zone);
            double from = Body.GetDistanceTo(threat);
            foreach (double turn in BotCasterSurvival.LegTurns)
            {
                (int x, int y) = BotCasterSurvival.AwayPoint(Body.X, Body.Y, threat.X, threat.Y,
                    BotCasterSurvival.LegDistance, turn);
                Vector3 probe = new(x, y, Body.Z);
                if (navmesh)
                {
                    if (nav.GetClosestPoint(zone, probe, 128, 128, 256, nav.DefaultFilters) is not Vector3 floor)
                        continue;
                    probe = floor;
                }
                if (Vector2.Distance(new(probe.X, probe.Y), new(threat.X, threat.Y)) < from + 250)
                    continue;
                destination = probe;
                break;
            }
            if (destination == null)
                return false;

            long now = GameLoop.GameLoopTime;
            if (countsToBudget)
            {
                _kiteLegStarts[_kiteLegIndex] = now;
                _kiteLegIndex = (_kiteLegIndex + 1) % _kiteLegStarts.Length;
            }
            _kiteFrom = threat;
            _kiteLegUntil = now + BotCasterSurvival.LegMaxMilliseconds;
            BotBody.KiteSprintUntil = _kiteLegUntil;
            BotBody.WakeRecoveryRest();
            StopTwistedSong();
            Body.StopAttack();
            Body.StopFollowing();
            Body.PathTo(destination.Value, Body.MaxSpeed);
            if (log.IsDebugEnabled)
                log.Debug($"BOT_KITE bot={Body.Name} action=run from={threat.Name} held={!countsToBudget}");
            return true;
        }

        private void EndKiteLeg()
        {
            _kiteLegUntil = 0;
            _kiteFrom = null;
            if (BotBody != null)
                BotBody.KiteSprintUntil = 0;
            if (Body.IsMoving)
            {
                Body.StopMovingOnPath();
                Body.StopMoving();
            }
        }

        /// <summary>
        /// A caster's turn against a player-shaped enemy in melee reach. True when it used the turn (a control spell,
        /// a Quickcast, or a kite leg); false for the ordinary turn, where melee stays the last resort.
        /// </summary>
        private bool TryCasterSurvival()
        {
            _survivalHoldsRange = false;
            if (BotBody == null || Body.IsIncapacitated)
                return false;

            GameLiving threat = KitesMeleeThreats || Body.ControlledBrain != null ? FindMeleeThreat() : null;
            PeelWithPet(threat);
            if (!KitesMeleeThreats)
                return false;

            long now = GameLoop.GameLoopTime;
            if (_kiteLegUntil > 0)
            {
                GameLiving from = _kiteFrom;
                bool running = now < _kiteLegUntil && Body.IsMoving && from?.IsAlive == true &&
                    Body.IsWithinRadius(from, Body.MeleeAttackRange + BotCasterSurvival.SafeGap);
                if (running)
                {
                    // Instants go off on the run; anything with a cast time waits for the turn.
                    if (!BotCasterSurvival.Held(from, Body))
                        CastSurvivalSpell(from, InstantControlFor(from), false);
                    return true;
                }
                // The leg is over: stop, turn and cast (the ordinary turn).
                EndKiteLeg();
            }

            if (threat == null || Body.IsCasting || Body.castingComponent?.HasPendingSkillRequests == true)
                return false;

            bool held = BotCasterSurvival.Held(threat, Body);
            Spell instant = held ? null : InstantControlFor(threat);
            Spell quickcastControl = held || instant != null ? null : QuickcastControlFor(threat);
            bool canRun = BotCasterSurvival.CanRun(BotBody);
            bool legsLeft = BotCasterSurvival.LegsLeft(_kiteLegStarts, now);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                BotKiteAction action = BotCasterSurvival.Decide(true, held, instant != null, quickcastControl != null,
                    canRun, legsLeft, BotBody.QuickcastReady && QuickcastNukeFor(threat) != null);
                switch (action)
                {
                    case BotKiteAction.InstantControl:
                        if (CastSurvivalSpell(threat, instant, false))
                            return true;
                        instant = null;
                        quickcastControl = QuickcastControlFor(threat);
                        continue;
                    case BotKiteAction.QuickcastControl:
                        if (CastSurvivalSpell(threat, quickcastControl, true))
                            return true;
                        quickcastControl = null;
                        continue;
                    case BotKiteAction.Run:
                        if (StartKiteLeg(threat, !held))
                            return true;
                        canRun = false;
                        continue;
                    case BotKiteAction.HoldAndCast:
                        _survivalHoldsRange = true;
                        return false;
                    case BotKiteAction.QuickcastNuke:
                        if (CastSurvivalSpell(threat, QuickcastNukeFor(threat), true))
                            return true;
                        return false;
                    default:
                        return false;
                }
            }
            return false;
        }

        /// <summary>
        /// A pure melee or shield-using group member's peel: the player-shaped enemy in melee on the group's healer,
        /// else on one of its casters. One peeler per attacker; the scan runs once a second.
        /// </summary>
        private GameLiving FindPeelTarget()
        {
            if (Body?.Group == null || BotBody?.CharacterClass == null ||
                !BotCasterSurvival.IsPeeler((eCharacterClass)BotBody.CharacterClass.ID,
                    BotCasterSurvival.ShieldEquipped(BotBody), PrefersCurrentSpellRange()))
                return null;

            long now = GameLoop.GameLoopTime;
            if (now < _nextPeelScan)
                return StillPeeling(_peelTarget) ? _peelTarget : null;
            _nextPeelScan = now + 1_000;

            var candidates = new List<(GameLiving Enemy, int Priority, double Distance)>();
            foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
            {
                if (member == null || member == Body || !member.IsAlive || member.CurrentRegionID != Body.CurrentRegionID ||
                    !Body.IsWithinRadius(member, GROUP_DEFENSE_ASSIST_RADIUS))
                    continue;
                ICharacterClass memberClass = member is GamePlayer player ? player.CharacterClass :
                    member is GameBot bot ? bot.CharacterClass : null;
                if (memberClass == null)
                    continue;
                int priority = BotCasterSurvival.PeelPriority(
                    BotCasterSurvival.IsPeelHealer((eCharacterClass)memberClass.ID), BotCasterSurvival.IsPeelCaster(memberClass));
                if (priority < 0)
                    continue;
                int reach = member.MeleeAttackRange + BotCasterSurvival.ThreatReachPadding;
                foreach (GameLiving enemy in member.GetPlayersInRadius((ushort)reach).Cast<GameLiving>()
                             .Concat(member.GetNPCsInRadius((ushort)reach).Where(BotPvpCrowdControl.PlayerLike)))
                {
                    if (enemy?.IsAlive == true && enemy.TargetObject == member && !enemy.IsStealthed &&
                        enemy.IsAttacking && CanAggroTarget(enemy))
                        candidates.Add((enemy, priority, Body.GetDistanceTo(enemy)));
                }
            }
            GameLiving best = candidates.OrderBy(c => c.Priority).ThenBy(c => c.Distance)
                .Select(c => c.Enemy).FirstOrDefault(enemy => BotCasterSurvival.ClaimPeel(BotBody, enemy));
            if (best != null && best != _peelTarget && log.IsDebugEnabled)
                log.Debug($"BOT_PEEL bot={Body.Name} on={best.Name} for={(best.TargetObject as GameLiving)?.Name}");
            _peelTarget = best;
            return best;
        }

        private bool StillPeeling(GameLiving target) =>
            target?.IsAlive == true && target.ObjectState == GameObject.eObjectState.Active &&
            target.TargetObject is GameLiving victim && victim != Body && Body.Group?.IsInTheGroup(victim) == true &&
            BotCasterSurvival.ClaimPeel(BotBody, target);
    }
}

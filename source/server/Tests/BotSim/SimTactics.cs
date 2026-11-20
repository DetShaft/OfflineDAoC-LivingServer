using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS;

namespace DOL.UnitTests.BotSim
{
    /// <summary>One brain decision for one agent, every <see cref="SimWorld.ThinkMilliseconds"/>.</summary>
    public interface ISimTactics
    {
        string Name { get; }
        void Think(SimWorld world, SimAgent self);
    }

    /// <summary>The CC spells the model gives the Mezzer role. Same spells in every realm.</summary>
    public static class SimSpells
    {
        private static Spell Make(int id, string name, int radius, double castSeconds, int durationSeconds) =>
            new(new DbSpell
            {
                SpellID = id, Name = name, Type = "Mesmerize", Target = "Enemy", Radius = radius,
                CastTime = castSeconds, Duration = durationSeconds, Range = 1_500,
            }, 20);

        private static SimSpell Wrap(Spell spell) => new() { Spell = spell, Kind = BotCasterSurvival.KindOf(spell) };

        /// <summary>Primary area mez (Sorcerer, Healer, Bard): 3 s cast, 350 radius.</summary>
        public static SimSpell AreaMez() => Wrap(Make(990_001, "area mez", 350, 3.0, 30));

        /// <summary>Single-target mez: 3 s cast.</summary>
        public static SimSpell SingleMez() => Wrap(Make(990_002, "single mez", 0, 3.0, 40));

        /// <summary>The slow backup area mez (Minstrel Lullaby): 5 s cast, 250 radius.</summary>
        public static SimSpell SlowAreaMez() => Wrap(Make(990_003, "slow area mez", 250, 5.0, 30));
    }

    /// <summary>
    /// The current live rules for bots in a player's group, as far as the model can follow them. Pure rules come from
    /// the server itself; the rest mirrors BotBrain's IDLE / FOLLOW / AGGRO states (see the doc for each rule's source).
    /// </summary>
    public class CurrentBotTactics : ISimTactics
    {
        /// <summary>
        /// Live for bots in a player's group (temporary companions): CompanionPvpEngagement.Observe gives the whole group one
        /// focus, the leader's target or else the enemy that last hit a groupmate nearest the leader, and orders every bot in
        /// range onto it. Off approximates groups without it (autonomous gamebot groups, not modelled exactly).
        /// </summary>
        public bool SharedFocus { get; init; } = true;
        public string Label { get; init; } = "current";
        public string Name => Label;

        public virtual void Think(SimWorld w, SimAgent a)
        {
            long now = w.Now;
            foreach (SimAgent enemy in a.Aggro.Keys.ToList())
                if (!enemy.Alive || a.DistanceTo(enemy) > SimWorld.MaxAggroDistance || !CanAggro(w, a, enemy))
                    a.Aggro.Remove(enemy);

            SimAgent leader = a.Leader;
            // BotState_Idle / BotState_Follow: assist only while the leader is attacking (or casting a harmful spell).
            if (leader != null && leader.Alive && leader.Target is { Alive: true } leaderTarget && LeaderIsFighting(w, leader) &&
                a.DistanceTo(leaderTarget) <= SimWorld.AggroRadius && CanAggro(w, a, leaderTarget) && !a.Aggro.ContainsKey(leaderTarget))
                a.Aggro[leaderTarget] = 1;

            a.OrderedTarget = null;
            SimAgent focus = SharedFocus ? GroupFocus(w, a) : null;
            // AssistPlayerAttack: support bots hold support; others take an ordered pull onto the focus.
            if (focus != null && !a.Kit.Heals && a.DistanceTo(focus) <= SimWorld.AggroRadius && CanAggro(w, a, focus))
            {
                a.OrderedTarget = focus;
                if (!a.Aggro.ContainsKey(focus))
                    a.Aggro[focus] = 1;
            }

            if (a.Kit.Heals && TryHeal(w, a))
                return;
            if (a.Kit.Heals)
            {
                // HoldsExclusiveSupportRole: a healer does not take up offense.
                a.AttackingNow = false;
                StayWithLeader(w, a, "healer: nobody below 75%");
                return;
            }

            if (a.Kit.CastsMez && a.Aggro.Count > 0 && TryCrowdControl(w, a))
                return;

            SimAgent target = ChooseTarget(w, a, out string why);
            if (target == null)
            {
                a.AttackingNow = false;
                a.Target = null;
                StayWithLeader(w, a, why);
                return;
            }
            if (target != a.Target)
            {
                a.Target = target;
                a.Metrics.TargetsChosen.Add(new SimTargetChoice { Time = now, Target = target.Name, TargetClass = target.ClassName });
                w.Log($"{a.Name} targets {target.Label}");
            }

            if (a.Kit.CastsNukes)
                CasterTurn(w, a, target);
            else
            {
                a.AttackingNow = true;
                a.MoveGoal = null;
                a.Activity = a.DistanceTo(target) <= a.Kit.MeleeRange ? SimActivity.Attacking : SimActivity.Approaching;
                a.Status = "melee";
            }
        }

        /// <summary>CompanionPvpEngagement.Observe's focus: the leader's enemy target, else the live threat nearest the leader.</summary>
        public static SimAgent GroupFocus(SimWorld w, SimAgent a)
        {
            SimAgent leader = a.Leader;
            if (leader == null || !leader.Alive)
                return null;
            if (LeaderIsFighting(w, leader) && leader.Target is { Alive: true } target)
                return target.Kit.IsNpc ? null : target; // Order() takes enemy bots only; NPCs come through plain assist
            return w.Enemies(a).Where(e => w.IsThreat(a.Side, e) && e.DistanceTo(leader) <= SimWorld.AggroRadius)
                .OrderBy(e => e.DistanceTo(leader)).FirstOrDefault();
        }

        public static bool LeaderIsFighting(SimWorld w, SimAgent leader) =>
            leader.AttackingNow || leader.Casting != null && leader.Casting.Target.Side != leader.Side;

        /// <summary>Bots may not aggro a keep lord shielded by a standing door (GuardLord.ShieldedFromBots).</summary>
        public static bool CanAggro(SimWorld w, SimAgent a, SimAgent target) =>
            target.Alive && target.Side != a.Side && (target.ShieldedFromBots == null || !target.ShieldedFromBots(w.Now));

        /// <summary>
        /// BotPvpCrowdControl.Protected: a mezzed enemy is left alone while another enemy is free nearby, and an enemy a
        /// groupmate is mezzing (a live claim) is left alone. The leader's own target and defended attackers are exempt.
        /// </summary>
        public static bool Protected(SimWorld w, SimAgent a, SimAgent target)
        {
            if (target.Kit.IsNpc)
                return false;
            SimAgent leader = a.Leader;
            if (leader != null && leader.Alive && leader.Target == target && LeaderIsFighting(w, leader))
                return false;
            if (a.OrderedTarget == target || a.Tactics is CurrentBotTactics { SharedFocus: true } && w.IsThreat(a.Side, target))
                return false; // CompanionPvpEngagement.Focused / Defending
            if (target.Mezzed(w.Now))
                return w.Enemies(a).Any(e => e != target && !e.Kit.IsNpc && !e.Mezzed(w.Now) && e.DistanceTo(a) <= BotPvpCrowdControl.FreeEnemyRadius);
            if (!w.Claims.TryGetValue(target, out SimWorld.SimClaim claim) || claim.Caster == a || !claim.Caster.Alive || claim.Caster.Side != a.Side)
                return false;
            if (claim.Spell.Kind == BotCasterSurvival.ControlKind.Mez && w.ImmuneToMez(target))
                return false;
            int? casting = claim.Caster.Casting?.Spell?.Spell.ID;
            return BotPvpCrowdControl.ClaimCastAlive(claim.Start, claim.Spell.Spell.ID, w.Now, casting);
        }

        protected virtual SimAgent ChooseTarget(SimWorld w, SimAgent a, out string why)
        {
            if (a.OrderedTarget is { Alive: true } ordered && CanAggro(w, a, ordered))
            {
                why = "";
                return ordered;
            }
            if (a.Target != null && a.Aggro.ContainsKey(a.Target) && a.Target.Alive && CanAggro(w, a, a.Target) && !Protected(w, a, a.Target))
            {
                why = "";
                return a.Target;
            }
            var candidates = a.Aggro.Keys.Where(e => e.Alive && CanAggro(w, a, e)).ToList();
            var free = candidates.Where(e => !Protected(w, a, e)).ToList();
            if (free.Count == 0)
            {
                why = candidates.Count > 0 ? "aggro list holds only mezzed/claimed enemies"
                    : a.Leader != null && a.Leader.Alive && a.Leader.Mezzed(w.Now) ? "leader is mezzed; not attacked itself"
                    : a.Leader != null && a.Leader.Alive && !LeaderIsFighting(w, a.Leader) ? "leader is not attacking; not attacked itself"
                    : w.Enemies(a).Any(e => e.ShieldedFromBots != null && e.ShieldedFromBots(w.Now)) ? "only a door-shielded lord in reach"
                    : "no enemy in aggro list";
                return null;
            }
            why = "";
            return free.OrderByDescending(e => a.Aggro[e]).ThenBy(e => a.DistanceTo(e)).First();
        }

        protected static void StayWithLeader(SimWorld w, SimAgent a, string why)
        {
            SimAgent leader = a.Leader;
            if (leader != null && leader != a && leader.Alive && a.DistanceTo(leader) > SimWorld.FollowDistance)
            {
                a.MoveGoal = leader.Position.Toward(a.Position, 250);
                a.Activity = SimActivity.Following;
                a.Status = "following leader (" + why + ")";
                return;
            }
            a.MoveGoal = null;
            a.Activity = SimActivity.Idle;
            a.Status = why;
        }

        protected virtual bool TryHeal(SimWorld w, SimAgent a)
        {
            if (w.Now < a.InterruptedUntil)
            {
                a.Activity = SimActivity.Interrupted;
                a.Status = "interrupted";
                return true;
            }
            SimAgent hurt = w.Allies(a).Where(m => m.HealthPercent < 75 && a.DistanceTo(m) <= SimWorld.HealRange && !m.Kit.IsNpc)
                .OrderBy(m => m.HealthPercent).FirstOrDefault();
            if (hurt == null)
                return false;
            if (w.StartCast(a, null, hurt, SimWorld.HealCastMilliseconds))
            {
                a.Activity = SimActivity.Healing;
                a.Status = "healing " + hurt.Name;
            }
            return true;
        }

        /// <summary>The PvP CC turn (BotBrain.PvpCrowdControl) on the real ordering and gating rules.</summary>
        protected virtual bool TryCrowdControl(SimWorld w, SimAgent a)
        {
            if (w.Now < a.InterruptedUntil)
                return false;
            var bySpell = a.CcSpells.ToDictionary(s => s.Spell);
            foreach (Spell ordered in BotPvpCrowdControlPolicy.Order(a.CcSpells.Select(s => s.Spell)))
            {
                SimSpell spell = bySpell[ordered];
                var inRange = w.Enemies(a).Where(e => !e.Kit.IsNpc && a.DistanceTo(e) <= spell.Range && !e.Mezzed(w.Now) && !w.ImmuneToMez(e) &&
                                                      !(w.Claims.TryGetValue(e, out var c) && c.Caster != a && c.Caster.Alive)).ToList();
                if (inRange.Count == 0)
                    continue;
                if (BotPvpCrowdControlPolicy.IsAreaMez(spell.Spell))
                {
                    if (!BotPvpCrowdControlPolicy.AreaMezReady(w.Now, a.LastAreaMezAttempt < 0 ? (long?)null : a.LastAreaMezAttempt))
                        continue;
                    var best = inRange.Select(center => (center, count: inRange.Count(e => e.DistanceTo(center) <= spell.Radius)))
                        .OrderByDescending(p => p.count).ThenBy(p => a.DistanceTo(p.center)).First();
                    if (!BotPvpCrowdControlPolicy.AreaClusterWorthCasting(best.count))
                        continue;
                    if (w.StartCast(a, spell, best.center, spell.CastMilliseconds))
                    {
                        a.LastAreaMezAttempt = w.Now;
                        a.Activity = SimActivity.Casting;
                        a.Status = $"area mez on {best.center.Name} ({best.count} in area)";
                        return true;
                    }
                    return false;
                }
                bool breaks = BotPvpCrowdControlPolicy.BreaksOnDamage(spell.Kind);
                SimAgent single = inRange
                    .Where(e => !breaks || !w.Allies(a).Any(m => m.Target == e && (m.AttackingNow || m.Casting?.Target == e)))
                    .OrderByDescending(e => e.Target != null && e.Target.Side == a.Side)
                    .ThenBy(e => a.DistanceTo(e)).FirstOrDefault();
                if (single != null && w.StartCast(a, spell, single, spell.CastMilliseconds))
                {
                    a.Activity = SimActivity.Casting;
                    a.Status = "single mez on " + single.Name;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Casters: the kite decision is the real BotCasterSurvival.Decide.</summary>
        protected virtual void CasterTurn(SimWorld w, SimAgent a, SimAgent target)
        {
            SimAgent threat = w.Enemies(a).Where(e => e.Target == a && e.AttackingNow && !e.Kit.CastsNukes && !e.Kit.Heals && a.DistanceTo(e) <= 350)
                .OrderBy(e => a.DistanceTo(e)).FirstOrDefault();
            bool held = threat != null && threat.Controlled(w.Now);
            BotKiteAction action = BotCasterSurvival.Decide(threat != null, held, false, false, true,
                BotCasterSurvival.LegsLeft(a.KiteLegStarts, w.Now), false);
            if (action == BotKiteAction.Run && threat != null)
            {
                int oldest = Array.IndexOf(a.KiteLegStarts, a.KiteLegStarts.Min());
                a.KiteLegStarts[oldest] = w.Now;
                (int x, int y) = BotCasterSurvival.AwayPoint((int)a.Position.X, (int)a.Position.Y, (int)threat.Position.X, (int)threat.Position.Y, 450, 0);
                a.AttackingNow = false;
                a.MoveGoal = new Vec2(x, y);
                a.Activity = SimActivity.Kiting;
                a.Status = "kiting from " + threat.Name;
                return;
            }
            if (action == BotKiteAction.Melee && threat != null)
            {
                a.Target = threat;
                a.AttackingNow = true;
                a.Status = "melee (no kite legs left)";
                return;
            }
            double distance = a.DistanceTo(target);
            if (distance > SimWorld.NukeRange)
            {
                a.AttackingNow = false;
                a.MoveGoal = target.Position.Toward(a.Position, SimWorld.NukeRange - 100);
                a.Activity = SimActivity.Approaching;
                a.Status = "walking into spell range";
                return;
            }
            a.MoveGoal = null;
            if (w.Now < a.InterruptedUntil)
            {
                a.Activity = SimActivity.Interrupted;
                a.Status = "interrupted";
                return;
            }
            if (w.StartCast(a, null, target, SimWorld.NukeCastMilliseconds))
            {
                a.Activity = SimActivity.Casting;
                a.Status = "nuking " + target.Name;
            }
        }
    }

    /// <summary>
    /// The human player (group leader) as a script: attacks a chosen target, or the nearest enemy. Not an AI under test.
    /// </summary>
    public sealed class ScriptedLeader : ISimTactics
    {
        public string Name => "scripted player";
        public Func<SimWorld, SimAgent, SimAgent> PickTarget { get; init; }
        /// <summary>The player never attacks (leash scenario).</summary>
        public bool Passive { get; init; }
        public int EngageRadius { get; init; } = 5_000; // a player sees and charges the enemy group across the field

        public void Think(SimWorld w, SimAgent a)
        {
            if (Passive)
            {
                a.AttackingNow = false;
                a.Activity = SimActivity.Idle;
                a.Status = "player standing still";
                return;
            }
            SimAgent target = a.Target is { Alive: true } current ? current : PickTarget?.Invoke(w, a) ??
                w.Enemies(a).Where(e => e.DistanceTo(a) <= EngageRadius).OrderBy(e => e.DistanceTo(a)).FirstOrDefault();
            if (target == null)
            {
                a.AttackingNow = false;
                a.Activity = SimActivity.Idle;
                a.Status = "player: no enemy near";
                return;
            }
            if (target != a.Target)
            {
                a.Metrics.TargetsChosen.Add(new SimTargetChoice { Time = w.Now, Target = target.Name, TargetClass = target.ClassName });
                w.Log($"{a.Name} (player) attacks {target.Label}");
            }
            a.Target = target;
            a.AttackingNow = true;
            a.Status = "player attacking";
        }
    }

    /// <summary>Keep lord and guards: aggro on the nearest enemy in range, then the one that hurt them most; return home.</summary>
    public sealed class NpcGuardTactics : ISimTactics
    {
        public string Name => "keep NPC";
        public int AggroRange { get; init; } = 700;

        public void Think(SimWorld w, SimAgent a)
        {
            foreach (SimAgent enemy in a.Aggro.Keys.ToList())
                if (!enemy.Alive || enemy.Position.DistanceTo(a.Home) > 2_500)
                    a.Aggro.Remove(enemy);
            foreach (SimAgent enemy in w.Enemies(a).Where(e => e.DistanceTo(a) <= AggroRange))
                if (!a.Aggro.ContainsKey(enemy))
                    a.Aggro[enemy] = 0.5;
            SimAgent target = a.Aggro.Count == 0 ? null : a.Aggro.OrderByDescending(p => p.Value).ThenBy(p => a.DistanceTo(p.Key)).First().Key;
            if (target == null)
            {
                a.AttackingNow = false;
                a.Target = null;
                a.MoveGoal = a.Position.DistanceTo(a.Home) > 5 ? (Vec2?)a.Home : null;
                a.Activity = SimActivity.Idle;
                a.Status = "guarding";
                return;
            }
            if (target != a.Target)
                w.Log($"{a.Name} turns on {target.Label}");
            a.Target = target;
            a.AttackingNow = true;
            a.Status = "fighting " + target.Name;
        }
    }

    /// <summary>An enemy that hits, then runs straight away from the group's leader: draws a chase (leash scenario).</summary>
    public sealed class RunnerTactics : ISimTactics
    {
        public string Name => "runner";
        public long RunAfterMilliseconds { get; init; } = 8_000;
        public Vec2 RunFrom { get; init; }

        public void Think(SimWorld w, SimAgent a)
        {
            if (w.Now >= RunAfterMilliseconds)
            {
                if (a.Activity != SimActivity.Fleeing)
                    w.Log($"{a.Name} turns and runs");
                a.AttackingNow = false;
                double dx = a.Position.X - RunFrom.X, dy = a.Position.Y - RunFrom.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                a.MoveGoal = new Vec2(a.Position.X + dx / d * 2_000, a.Position.Y + dy / d * 2_000);
                a.Activity = SimActivity.Fleeing;
                a.Status = "running away";
                return;
            }
            SimAgent target = a.Target is { Alive: true } t ? t : w.Enemies(a).Where(e => !e.IsLeader).OrderBy(e => e.DistanceTo(a)).FirstOrDefault();
            a.Target = target;
            a.AttackingNow = target != null;
            a.Status = "harassing";
        }
    }
}

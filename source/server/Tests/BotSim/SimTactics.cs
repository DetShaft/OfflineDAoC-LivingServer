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
        /// <summary>Autonomous gamebots (the enemy side): enemies this close join the aggro list on sight.</summary>
        public int ProximityAggroRadius { get; init; }
        /// <summary>Temporary companions are recalled to the player past 3,000 units when no groupmate near them fights.</summary>
        public bool CompanionRecall { get; init; } = true;
        /// <summary>The #170 fixes (on: the live rules). Off reproduces the rules merged in PR #168 for comparison.</summary>
        public bool Fixes170 { get; init; } = true;
        /// <summary>
        /// #175-#177 (on: the live rules). Off reproduces the rules before them. For every bot: a cast that failed its line
        /// of sight check walks until the target is in sight (#176). Fight mode adds its own (FightModeTactics).
        /// </summary>
        public bool EdgeCaseFixes { get; init; } = true;
        /// <summary>
        /// BotBrain.AttackMostWanted: for a bot in a player's group the enemy that hit it in the last 5 s becomes its
        /// target (KillOrderAttacker leaves it unchanged outside the autonomous incoming-fire rules). Autonomous enemies:
        /// off, an approximation of BotIncomingFireReaction.OverridesKillOrder.
        /// </summary>
        public bool DirectAttackerFirst { get; init; } = true;
        public string Label { get; init; } = "fight mode off";
        public string Name => Label;

        public virtual void Think(SimWorld w, SimAgent a)
        {
            long now = w.Now;
            foreach (SimAgent enemy in a.Aggro.Keys.ToList())
                if (!enemy.Alive || a.DistanceTo(enemy) > SimWorld.MaxAggroDistance || !CanAggro(w, a, enemy))
                    a.Aggro.Remove(enemy);

            if (ProximityAggroRadius > 0)
                foreach (SimAgent enemy in w.Enemies(a).Where(e => a.DistanceTo(e) <= ProximityAggroRadius && CanAggro(w, a, e) && w.LineOfSight(a, e)))
                    if (!a.Aggro.ContainsKey(enemy))
                        a.Aggro[enemy] = 0.5;

            SimAgent leader = a.Leader;
            if (CompanionRecall && a.IsBot && now >= a.NextRecallCheck)
            {
                a.NextRecallCheck = now + TemporaryCompanionRecovery.CheckIntervalMilliseconds;
                if (leader != null && leader != a && leader.Alive && a.DistanceTo(leader) > TemporaryCompanionRecovery.MaximumLeaderDistance &&
                    !HasPartyCombat(w, a))
                {
                    w.Log($"{a.Name} is recalled to {leader.Name} from {a.DistanceTo(leader):0} units");
                    a.Position = leader.Position.Toward(a.Position, 150);
                    a.Aggro.Clear();
                    a.Target = null;
                    a.AttackingNow = false;
                    a.MoveGoal = null;
                    a.Metrics.Recalls++;
                }
            }

            // BotState_Idle / BotState_Follow: assist only while the leader is attacking (or casting a harmful spell).
            if (leader != null && leader.Alive && leader.Target is { Alive: true } leaderTarget && LeaderIsFighting(w, leader) &&
                a.DistanceTo(leaderTarget) <= SimWorld.AggroRadius && CanAggro(w, a, leaderTarget) && !a.Aggro.ContainsKey(leaderTarget))
                a.Aggro[leaderTarget] = 1;

            a.OrderedTarget = null;
            // #170 follow-up: while fight mode is on it picks the targets, and the companion focus stays out.
            SimAgent focus = SharedFocus && !FightModeOwnsTargets(a) ? GroupFocus(w, a) : null;
            // AssistPlayerAttack: support bots hold support; others take an ordered pull onto the focus.
            if (focus != null && !a.Kit.Heals && a.DistanceTo(focus) <= SimWorld.AggroRadius && CanAggro(w, a, focus))
            {
                a.OrderedTarget = focus;
                if (!a.Aggro.ContainsKey(focus))
                    a.Aggro[focus] = 1;
            }

            if (FightModeTurn(w, a))
                return;

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

        /// <summary>
        /// Runs after the companion focus and before targeting (BotBrain.ThinkGroupFightMode's place). Off here. True: the
        /// turn was spent (a caster closing up on its melee, #177).
        /// </summary>
        protected virtual bool FightModeTurn(SimWorld w, SimAgent a) => false;

        /// <summary>#175: stay on <paramref name="pick"/> rather than turn to <paramref name="other"/> (fight mode only).</summary>
        protected virtual bool KeepsPickOver(SimWorld w, SimAgent a, SimAgent pick, SimAgent other) => false;

        /// <summary>PlayerGroupFightMode.ActiveFor: fight mode is on for this bot's group.</summary>
        protected virtual bool FightModeOwnsTargets(SimAgent a) => false;

        /// <summary>Enemies the CC turn must leave alone (fight mode: the group's focus).</summary>
        protected virtual bool SkipForCrowdControl(SimWorld w, SimAgent a, SimAgent enemy) => false;

        /// <summary>TemporaryCompanionRecovery.HasPartyCombat: a groupmate within 3,000 units of the bot is fighting.</summary>
        public static bool HasPartyCombat(SimWorld w, SimAgent a) =>
            a.Group.Any(m => m.Alive && m.DistanceTo(a) <= TemporaryCompanionRecovery.MaximumLeaderDistance &&
                             (m.AttackingNow || m.Aggro.Count > 0 || w.Now - m.LastDamageTime < SimWorld.InCombatWindowMilliseconds));

        /// <summary>CompanionPvpEngagement.Observe's focus: the leader's enemy target, else the live threat nearest the leader.</summary>
        public static SimAgent GroupFocus(SimWorld w, SimAgent a)
        {
            SimAgent leader = a.Leader;
            if (leader == null || !leader.Alive)
                return null;
            if (LeaderIsFighting(w, leader) && leader.Target is { Alive: true } target)
                return target.Kit.IsNpc ? null : target; // Order() takes enemy bots only; NPCs come through plain assist
            // #170: a free attacker before a mezzed one.
            bool fixes = a.Tactics is not CurrentBotTactics { Fixes170: false };
            return w.Enemies(a).Where(e => w.IsThreat(a.Side, e) && e.DistanceTo(leader) <= SimWorld.AggroRadius)
                .OrderBy(e => fixes && e.Mezzed(w.Now)).ThenBy(e => e.DistanceTo(leader)).FirstOrDefault();
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
            if (a.OrderedTarget == target)
                return false; // CompanionPvpEngagement.Focused
            bool defended = a.Tactics is CurrentBotTactics { SharedFocus: true } && w.IsThreat(a.Side, target);
            if (defended && a.Tactics is CurrentBotTactics { Fixes170: false })
                return false; // before #170: Defending was exempt even when mezzed
            // #170: a mezzed enemy is left alone while another is free, even one that hit a groupmate (Defending).
            if (target.Mezzed(w.Now))
                return w.Enemies(a).Any(e => e != target && !e.Kit.IsNpc && !e.Mezzed(w.Now) && e.DistanceTo(a) <= BotPvpCrowdControl.FreeEnemyRadius);
            if (defended)
                return false; // CompanionPvpEngagement.Defending
            if (!w.Claims.TryGetValue(target, out SimWorld.SimClaim claim) || claim.Caster == a || !claim.Caster.Alive || claim.Caster.Side != a.Side)
                return false;
            if (claim.Spell.Kind == BotCasterSurvival.ControlKind.Mez && w.ImmuneToMez(target))
                return false;
            int? casting = claim.Caster.Casting?.Spell?.Spell.ID;
            return BotPvpCrowdControl.ClaimCastAlive(claim.Start, claim.Spell.Spell.ID, w.Now, casting);
        }

        protected virtual SimAgent ChooseTarget(SimWorld w, SimAgent a, out string why)
        {
            SimAgent ordered = a.OrderedTarget is { Alive: true } o && CanAggro(w, a, o) ? o : null;
            // AttackMostWanted: protectionTarget ?? directAttacker ?? ... ?? the aggro list (where the ordered pull sits).
            // A mezzed attacker is left alone (#170's mez protection).
            if (DirectAttackerFirst && a.LastAttacker is { Alive: true } direct && w.Now - a.LastAttackedAt <= 5_000 &&
                direct.Side != a.Side && CanAggro(w, a, direct) && !direct.Mezzed(w.Now))
            {
                why = "";
                return ordered != null && ordered != direct && KeepsPickOver(w, a, ordered, direct) ? ordered : direct;
            }
            if (ordered != null)
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
                    : a.Leader != null && a.Leader != a && !a.Leader.Alive ? "leader is dead; not attacked itself"
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
            SimAgent hurt = w.Allies(a).Where(m => m.HealthPercent < 75 && a.DistanceTo(m) <= SimWorld.HealRange && !m.Kit.IsNpc && w.LineOfSight(a, m))
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
            // #170: an enemy that will hit the caster before a cast ends interrupts it (BotBrain.TryPvpCrowdControl,
            // BotPvpCrowdControlPolicy.Threatens). Sim casters and mezzers are the ranged attackers; nobody carries a bow.
            var threats = Fixes170 ? w.Enemies(a).Where(e => e.Alive && !e.Mezzed(w.Now) && (e.Target == a || e.Casting?.Target == a)).ToList()
                : new List<SimAgent>();
            bool UnderFire(int castTime) => threats.Any(e => BotPvpCrowdControlPolicy.Threatens(true, false, e.Casting?.Target == a,
                e.Kit.CastsNukes, (int)e.DistanceTo(a), e.Kit.MeleeRange + 50, castTime));
            var bySpell = a.CcSpells.ToDictionary(s => s.Spell);
            foreach (Spell ordered in BotPvpCrowdControlPolicy.Order(a.CcSpells.Select(s => s.Spell)))
            {
                SimSpell spell = bySpell[ordered];
                if (!BotPvpCrowdControlPolicy.CastSurvives(spell.CastMilliseconds, UnderFire(spell.CastMilliseconds)))
                    continue;
                var inRange = w.Enemies(a).Where(e => !e.Kit.IsNpc && a.DistanceTo(e) <= spell.Range && !e.Mezzed(w.Now) && !w.ImmuneToMez(e) &&
                                                      w.LineOfSight(a, e) &&
                                                      !SkipForCrowdControl(w, a, e) &&
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
            if (LineOfSightTurn(w, a, target))
                return;
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

        /// <summary>
        /// #176: the target is behind a ridge. A cast attempt fails its line of sight check (the think is spent standing);
        /// GameNPC then follows the target. Before the fix the next think's ranged turn stopped that follow and cast into
        /// the ridge again, so the caster stood about every other think. With the fix
        /// (BotLineOfSightPolicy.Walks) it keeps walking until the target is in sight. True: the turn was spent.
        /// </summary>
        /// <summary>GameNPC.OnCastSpellLosCheckFail follows the target to about melee range.</summary>
        private const int LosFollowDistance = 100;

        protected bool LineOfSightTurn(SimWorld w, SimAgent a, SimAgent target)
        {
            bool inSight = w.LineOfSight(a, target);
            int distance = (int)a.DistanceTo(target);
            if (EdgeCaseFixes && BotLineOfSightPolicy.Walks(w.Now, a.LosBlockedUntil, a.LosBlockedTarget == target, inSight, distance))
            {
                a.MoveGoal = target.Position.Toward(a.Position, BotLineOfSightPolicy.ApproachDistance);
                a.Activity = SimActivity.Approaching;
                a.Status = "walking for line of sight";
                return true;
            }
            if (inSight)
            {
                a.LosFollowNext = false;
                return false;
            }
            // GameNPC.OnCastSpellLosCheckFail's follow (both builds): one think of walking after each failed check.
            // With the fix this is what is left once the walk has reached ApproachDistance or the hold ran out.
            if (a.LosFollowNext && a.LosBlockedTarget == target)
            {
                a.LosFollowNext = false;
                a.MoveGoal = target.Position.Toward(a.Position, LosFollowDistance);
                a.Activity = SimActivity.Approaching;
                a.Status = "following after a failed line of sight check";
                return true;
            }
            a.LosBlockedTarget = target;
            a.LosBlockedUntil = w.Now + BotLineOfSightPolicy.HoldMilliseconds;
            a.LosFollowNext = true;
            a.MoveGoal = null;
            a.AttackingNow = false;
            a.Activity = SimActivity.Idle;
            a.Status = "cast failed: no line of sight";
            return true;
        }
    }

    /// <summary>
    /// Fight mode (#165) on top of the older rules, as BotBrain.ThinkGroupFightMode and PlayerGroupFightMode run it. The
    /// target picks, the leash test and the exit timer are the real GroupFightPolicy functions. Scope (frontier,
    /// battlegrounds, not companion raids) is assumed: every scenario is an RvR fight.
    /// </summary>
    public sealed class FightModeTactics : CurrentBotTactics
    {
        public int ChaseDistance { get; init; } = GroupFightPolicy.ChaseDistance(0);

        /// <summary>
        /// The rule before the owner's decision of 2026-11-20 (#170): the player's selected target came first even while
        /// they were mezzed or not swinging. The live rule (PlayerGroupFightMode.LeaderTarget) counts it only while they
        /// attack it. True is a comparison only.
        /// </summary>
        public bool SelectedTargetFirst { get; init; }

        private sealed class GroupState
        {
            public bool Active;
            public long LastHostile, NextScan;
            public SimAgent Focus;
            public List<SimAgent> Candidates = new();
            public readonly Dictionary<SimAgent, long> LastSeen = new(); // #176
        }

        private readonly Dictionary<SimAgent, GroupState> _groups = new();
        private readonly Dictionary<SimAgent, (SimAgent Pick, GroupFightPolicy.PickReason Reason)> _lastPick = new();

        public FightModeTactics() { Label = "fight mode (#175-#177)"; }

        /// <summary>
        /// PlayerGroupFightMode.Describe's role. The model maps by role, not class, so every realm is described the same
        /// way: healers are healers, casters and mezzers are casters, melee are fighters, keep NPCs (no class) are pets.
        /// </summary>
        public static GroupFightPolicy.TargetRole RoleOf(SimAgent t) =>
            t.Kit.IsNpc ? GroupFightPolicy.TargetRole.Pet
            : t.Kit.Heals ? GroupFightPolicy.TargetRole.Healer
            : t.Kit.CastsNukes ? GroupFightPolicy.TargetRole.Caster
            : GroupFightPolicy.TargetRole.Fighter;

        private static bool HittingBackLine(SimAgent leader, SimAgent enemy)
        {
            SimAgent victim = enemy.Casting?.Target ?? (enemy.AttackingNow ? enemy.Target : null);
            return victim != null && leader.Group.Contains(victim) && (victim.Kit.Heals || victim.Kit.CastsNukes);
        }

        private static GroupFightPolicy.Candidate Describe(SimWorld w, SimAgent leader, SimAgent anchor, SimAgent t) =>
            new(RoleOf(t), (int)t.HealthPercent, (int)anchor.DistanceTo(t), t.Mezzed(w.Now), HittingBackLine(leader, t));

        /// <summary>PlayerGroupFightMode.Anchor (#170): the leader, or once dead the living groupmate nearest the body.</summary>
        public static SimAgent Anchor(SimAgent leader)
        {
            if (leader.Alive)
                return leader;
            var members = leader.Group.Where(m => m != leader && !m.Kit.IsNpc).ToList();
            int index = GroupFightPolicy.AnchorIndex(members.Select(m => m.Alive).ToArray(),
                members.Select(m => (int)leader.DistanceTo(m)).ToArray());
            return index < 0 ? null : members[index];
        }

        private static bool Hostile(SimWorld w, SimAgent t) => t.Alive && (t.ShieldedFromBots == null || !t.ShieldedFromBots(w.Now));

        private GroupState State(SimAgent leader)
        {
            if (!_groups.TryGetValue(leader, out GroupState state))
                _groups[leader] = state = new GroupState();
            return state;
        }

        /// <summary>PlayerGroupFightMode.Scan: threats and enemy players/bots near the anchor; enter, exit, focus.</summary>
        private void Scan(SimWorld w, SimAgent leader, SimAgent anchor, GroupState st)
        {
            if (w.Now < st.NextScan)
                return;
            st.NextScan = w.Now + PlayerGroupFightMode.ScanIntervalMilliseconds;
            // Threats, plus enemy players, bots and their pets the anchor can see (BotSiegeRuntime.Visible); #176: or saw
            // within GroupFightPolicy.SightMemoryMilliseconds.
            foreach (SimAgent e in w.Enemies(leader))
                if (w.LineOfSight(anchor, e))
                    st.LastSeen[e] = w.Now;
            bool Sighted(SimAgent e) => w.LineOfSight(anchor, e) ||
                EdgeCaseFixes && GroupFightPolicy.StillInSight(false, w.Now, st.LastSeen.TryGetValue(e, out long seen) ? seen : -1);
            var candidates = w.Enemies(leader).Where(e => Hostile(w, e) && anchor.DistanceTo(e) <= ChaseDistance &&
                (w.HitSideWithin(leader.Side, e, GroupFightPolicy.ThreatMemoryMilliseconds) || (!e.Kit.IsNpc || e.Kit.IsPet) && Sighted(e))).ToList();
            st.Candidates = candidates;
            if (candidates.Count > 0)
            {
                st.LastHostile = w.Now;
                if (!st.Active)
                {
                    st.Active = true;
                    st.Focus = null;
                    SimAgent first = candidates.OrderBy(e => anchor.DistanceTo(e)).First();
                    w.Log($"FIGHT MODE on for {leader.Name}'s group ({first.Name} at {anchor.DistanceTo(first):0} from {anchor.Name})");
                }
            }
            else if (st.Active && GroupFightPolicy.ShouldExit(w.Now, st.LastHostile))
            {
                st.Active = false;
                st.Focus = null;
                st.LastSeen.Clear();
                w.Log($"FIGHT MODE off for {leader.Name}'s group");
                return;
            }
            if (!st.Active || candidates.Count == 0)
                return;
            // Sticky focus, replaced by a better-role enemy (GroupFightPolicy.ShouldReplaceFocus, #170).
            var described = candidates.Select(t => Describe(w, leader, anchor, t)).ToArray();
            int best = GroupFightPolicy.PickFocus(described);
            int current = st.Focus == null ? -1 : candidates.IndexOf(st.Focus);
            if (current >= 0 && described[current].Mezzed)
                current = -1;
            if (best >= 0 && (current < 0 || Fixes170 && GroupFightPolicy.ShouldReplaceFocus(described[current], described[best])))
            {
                st.Focus = candidates[best];
                w.Log($"FIGHT FOCUS {st.Focus.Label} ({GroupFightPolicy.Label(described[best].Role)}, {described[best].HealthPercent}% HP, {candidates.Count} candidates, anchor {anchor.Name})");
            }
            else if (current < 0)
                st.Focus = null;
        }

        protected override bool FightModeOwnsTargets(SimAgent a) =>
            Fixes170 && a.Leader != null && _groups.TryGetValue(a.Leader, out GroupState st) && st.Active;

        protected override bool SkipForCrowdControl(SimWorld w, SimAgent a, SimAgent enemy) =>
            a.Leader != null && _groups.TryGetValue(a.Leader, out GroupState st) && st.Active && st.Focus == enemy;

        /// <summary>#175: in fight mode a pet does not take a bot off its pick (BotBrain.KeepFightModePickOverPet).</summary>
        protected override bool KeepsPickOver(SimWorld w, SimAgent a, SimAgent pick, SimAgent other) =>
            EdgeCaseFixes && FightModeOwnsTargets(a) && !pick.Mezzed(w.Now) && a.DistanceTo(pick) <= SimWorld.AggroRadius &&
            GroupFightPolicy.PetYields(PetRole(other), PetRole(pick));

        private static GroupFightPolicy.TargetRole PetRole(SimAgent t) =>
            t.Kit.IsPet ? GroupFightPolicy.TargetRole.Pet : GroupFightPolicy.TargetRole.Fighter;

        /// <summary>A groupmate who fights from the back (PlayerGroupFightMode.IsBackLine): healers, casters, mezzers.</summary>
        public static bool IsBackLine(SimAgent m) => m.Kit.Heals || m.Kit.CastsNukes;

        /// <summary>#177: PlayerGroupFightMode.DistanceToBackLine.</summary>
        private static int DistanceToBackLine(SimAgent self, SimAgent anchor, SimAgent target)
        {
            var back = self.Group.Where(m => m != self && m.Alive && IsBackLine(m) && anchor.DistanceTo(m) <= SimWorld.AggroRadius).ToList();
            return back.Count == 0 ? int.MaxValue : (int)back.Min(m => m.DistanceTo(target));
        }

        private static bool HittingGroup(SimAgent a, SimAgent anchor, SimAgent t, int chase)
        {
            SimAgent victim = t.Casting?.Target ?? (t.AttackingNow ? t.Target : null);
            return victim != null && (victim == a || victim.Side == a.Side && anchor.DistanceTo(victim) <= chase);
        }

        /// <summary>#177: BotBrain.CloseUpOnMelee for the ranged casters (not healers).</summary>
        private bool CloseUp(SimWorld w, SimAgent a, SimAgent anchor)
        {
            if (!EdgeCaseFixes || a.Kit.Heals || !a.Kit.CastsNukes || a.Casting != null)
                return false;
            SimAgent melee = a.Group.Where(m => m != a && m.Alive && !IsBackLine(m) && m.AttackingNow && anchor.DistanceTo(m) <= SimWorld.AggroRadius)
                .OrderBy(m => m.DistanceTo(a)).FirstOrDefault();
            int distance = melee == null ? int.MaxValue : (int)a.DistanceTo(melee);
            if (!GroupFightPolicy.CasterClosesIn(false, w.Now - a.LastAttackedAt <= 5_000, distance))
                return false;
            a.AttackingNow = false;
            a.MoveGoal = melee.Position.Toward(a.Position, GroupFightPolicy.CasterHold);
            a.Activity = SimActivity.Approaching;
            a.Status = "closing up on " + melee.Name;
            a.Metrics.CloseUps++;
            return true;
        }

        protected override bool FightModeTurn(SimWorld w, SimAgent a)
        {
            SimAgent leader = a.Leader;
            if (leader == null || leader == a)
                return false;
            // #170: a dead leader no longer ends fight mode; the anchor stands in.
            if (!Fixes170 && !leader.Alive)
                return false;
            SimAgent anchor = Anchor(leader);
            if (anchor == null)
                return false;
            bool near = a.DistanceTo(anchor) <= SimWorld.AggroRadius;
            if (!Fixes170 && !near)
                return false;
            GroupState st = State(leader);
            Scan(w, leader, anchor, st);
            // #170 follow-up: the leash also runs once fight mode has ended (enemy players only) and at any distance.
            if (Fixes170)
                Leash(w, a, leader, anchor, enemyPlayersOnly: !st.Active);
            if (!st.Active || !near)
                return false;
            if (!Fixes170)
                Leash(w, a, leader, anchor, enemyPlayersOnly: false);
            if (a.Casting != null || a.Kit.Heals)
                return false; // never cut a cast short; healers stay support-first (#48)
            if (CloseUp(w, a, anchor))
                return true;

            SimAgent leaderTarget = !leader.Alive ? null
                : leader.AttackingNow && leader.Target is { Alive: true } attacking ? attacking
                : SelectedTargetFirst && leader.Target is { Alive: true } selected && Hostile(w, selected) && leader.DistanceTo(selected) <= ChaseDistance ? selected : null;
            if (leaderTarget != null && !CanAggro(w, a, leaderTarget))
                leaderTarget = null;

            SimAgent pick = null;
            var reason = GroupFightPolicy.PickReason.None;
            if (leaderTarget != null && GroupFightPolicy.FollowsLeaderTarget(true, leaderTarget.Mezzed(w.Now)))
            {
                pick = leaderTarget;
                reason = GroupFightPolicy.PickReason.Leader;
            }
            else
            {
                bool tank = a.Role == SimRole.Tank;
                SimAgent current = a.Aggro.Count > 0 && a.Target is { } t && Hostile(w, t) && a.Aggro.ContainsKey(t) &&
                                   anchor.DistanceTo(t) <= ChaseDistance ? t : null; // BotBrain.LiveCombatTarget
                bool peeling = tank && current != null && HittingBackLine(leader, current);
                if (!Fixes170 && current != null)
                    return false; // before #170: HasLiveCombatTarget, no switching at all
                if (tank && !peeling)
                {
                    var described = st.Candidates.Select(c => Describe(w, leader, anchor, c)).ToArray();
                    int peel = GroupFightPolicy.PickPeel(described);
                    if (peel >= 0)
                    {
                        pick = st.Candidates[peel];
                        reason = GroupFightPolicy.PickReason.Peel;
                    }
                }
                if (pick == null && st.Focus != null && st.Focus != current &&
                    (current == null || !peeling && GroupFightPolicy.ShouldSwitchToFocus(RoleOf(current), RoleOf(st.Focus))))
                {
                    pick = st.Focus;
                    reason = GroupFightPolicy.PickReason.Focus;
                }
            }
            if (pick == null || a.DistanceTo(pick) > SimWorld.AggroRadius || !CanAggro(w, a, pick))
                return false;
            // #177: never pick what the support leash would drop at once.
            if (EdgeCaseFixes && reason != GroupFightPolicy.PickReason.Leader && !IsBackLine(a) &&
                GroupFightPolicy.BeyondSupport(DistanceToBackLine(a, anchor, pick), ChaseDistance, false, HittingGroup(a, anchor, pick, ChaseDistance)))
                return false;
            a.OrderedTarget = pick; // AssistPlayerAttack -> OrderPull, with the aggro boost that makes a switch stick
            if (!a.Aggro.ContainsKey(pick))
                a.Aggro[pick] = 1;
            if (!_lastPick.TryGetValue(a, out var last) || last.Pick != pick || last.Reason != reason)
            {
                _lastPick[a] = (pick, reason);
                w.Log($"{a.Name} fight-mode pick {pick.Name} (why: {reason.ToString().ToLowerInvariant()})");
            }
            return false;
        }

        /// <summary>BotBrain.EnforceGroupFightLeash with the real GroupFightPolicy.BeyondLeash.</summary>
        private void Leash(SimWorld w, SimAgent a, SimAgent leader, SimAgent anchor, bool enemyPlayersOnly)
        {
            SimAgent leaderAttacking = leader.Alive && leader.AttackingNow ? leader.Target : null;
            SimAgent leaderSelected = leader.Alive ? leader.Target : null;
            var watched = a.Aggro.Keys.ToList();
            if (a.Target != null && !watched.Contains(a.Target))
                watched.Add(a.Target);
            foreach (SimAgent t in watched)
            {
                if (t == null || !t.Alive || enemyPlayersOnly && t.Kit.IsNpc)
                    continue;
                int distance = (int)anchor.DistanceTo(t);
                bool supportLeash = EdgeCaseFixes && !enemyPlayersOnly && !IsBackLine(a);
                if (distance <= ChaseDistance && !supportLeash)
                    continue;
                bool hittingGroup = HittingGroup(a, anchor, t, ChaseDistance);
                // #170 follow-up: an automatic ordered pull (fight mode's pick, the group focus) is no longer exempt;
                // the sim has no /pull, and the player's own attack is leaderAttacking.
                bool players = t == leaderSelected || t == leaderAttacking || !Fixes170 && t == a.OrderedTarget;
                // #177: the support leash, front line only, in fight mode only.
                bool beyondSupport = supportLeash && distance <= ChaseDistance &&
                    GroupFightPolicy.BeyondSupport(DistanceToBackLine(a, anchor, t), ChaseDistance, players, hittingGroup);
                if (!beyondSupport && !GroupFightPolicy.BeyondLeash(distance, ChaseDistance, players, hittingGroup))
                    continue;
                a.Aggro.Remove(t);
                if (Fixes170 && a.OrderedTarget == t)
                    a.OrderedTarget = null; // CancelOrderedPull
                if (a.Target == t)
                {
                    a.Target = null;
                    a.AttackingNow = false;
                    a.MoveGoal = null;
                }
                a.Metrics.LeashDrops++;
                if (beyondSupport)
                    a.Metrics.SupportLeashDrops++;
                w.Log(beyondSupport ? $"{a.Name} drops {t.Name} at the support leash ({DistanceToBackLine(a, anchor, t)} units from the nearest caster or healer)"
                    : $"{a.Name} drops {t.Name} at the leash ({distance} units from {anchor.Name})");
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

    /// <summary>
    /// A pet (#175): fights the nearest enemy within 1,200 units of its owner and stays on it while it is within 1,500 of
    /// the owner, else follows the owner. Not an AI under test.
    /// </summary>
    public sealed class PetTactics : ISimTactics
    {
        public string Name => "pet";

        public void Think(SimWorld w, SimAgent a)
        {
            SimAgent owner = a.Owner;
            if (owner == null || !owner.Alive)
            {
                a.AttackingNow = false;
                return;
            }
            SimAgent target = a.Target is { Alive: true } current && current.DistanceTo(owner) <= 1_500 && !current.Mezzed(w.Now) ? current
                : w.Enemies(a).Where(e => e.DistanceTo(owner) <= 1_200 && !e.Mezzed(w.Now)).OrderBy(e => e.DistanceTo(a)).FirstOrDefault();
            if (target == null)
            {
                a.Target = null;
                a.AttackingNow = false;
                a.MoveGoal = a.DistanceTo(owner) > 250 ? owner.Position.Toward(a.Position, 150) : null;
                a.Activity = SimActivity.Following;
                a.Status = "following owner";
                return;
            }
            if (target != a.Target)
                w.Log($"{a.Name} goes for {target.Label}");
            a.Target = target;
            a.AttackingNow = true;
            a.Status = "pet attacking";
        }
    }

    /// <summary>
    /// An enemy melee that baits (#177): fights the nearest enemy bot for <see cref="FightMilliseconds"/>, then runs to
    /// <see cref="BaitPoint"/> without swinging for <see cref="RunMilliseconds"/>, and starts again. Draws melee away
    /// from their casters.
    /// </summary>
    public sealed class BaiterTactics : ISimTactics
    {
        public string Name => "baiter";
        public Vec2 BaitPoint { get; init; }
        public long FightMilliseconds { get; init; } = 7_000;
        public long RunMilliseconds { get; init; } = 7_000;

        public void Think(SimWorld w, SimAgent a)
        {
            long phase = w.Now % (FightMilliseconds + RunMilliseconds);
            if (phase >= FightMilliseconds)
            {
                if (a.Activity != SimActivity.Fleeing)
                    w.Log($"{a.Name} breaks off toward the bait point");
                a.AttackingNow = false;
                a.Target = null;
                a.MoveGoal = BaitPoint;
                a.Activity = SimActivity.Fleeing;
                a.Status = "baiting";
                return;
            }
            SimAgent target = a.Target is { Alive: true } t && t.DistanceTo(a) <= 1_200 ? t
                : w.Enemies(a).Where(e => !e.IsLeader && !e.Kit.IsNpc).OrderBy(e => e.DistanceTo(a)).FirstOrDefault();
            a.Target = target;
            a.AttackingNow = target != null;
            a.MoveGoal = null;
            a.Status = "baiter fighting";
        }
    }

    /// <summary>A caster that holds its spot and nukes the nearest enemy in range (#177's enemy leader). Not an AI under test.</summary>
    public sealed class HoldAndNukeTactics : ISimTactics
    {
        public string Name => "holding caster";

        public void Think(SimWorld w, SimAgent a)
        {
            a.AttackingNow = false;
            a.MoveGoal = null;
            SimAgent target = w.Enemies(a).Where(e => !e.Kit.IsNpc && a.DistanceTo(e) <= SimWorld.NukeRange && w.LineOfSight(a, e))
                .OrderBy(e => a.DistanceTo(e)).FirstOrDefault();
            if (target == null || w.Now < a.InterruptedUntil)
            {
                a.Activity = SimActivity.Idle;
                a.Status = "holding";
                return;
            }
            if (w.StartCast(a, null, target, SimWorld.NukeCastMilliseconds))
            {
                a.Activity = SimActivity.Casting;
                a.Status = "nuking " + target.Name;
            }
        }
    }
}

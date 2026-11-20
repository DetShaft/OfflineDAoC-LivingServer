using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS;

namespace DOL.UnitTests.BotSim
{
    // Bot AI simulation harness (#166). A small, deterministic combat model on a flat field. Bot decisions come from
    // ISimTactics; the default tactics (CurrentBotTactics) call the real pure bot policies where they exist. See
    // docs/BOT-AI-SIMULATION.md for what is and is not modelled.

    public enum SimRole { Tank, Melee, Caster, Healer, Mezzer, Lord, Guard }

    public enum SimActivity { Dead, Controlled, Idle, Following, Approaching, Attacking, Casting, Healing, Interrupted, Kiting, Fleeing }

    public struct Vec2
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
        public double DistanceTo(Vec2 other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
        public Vec2 Toward(Vec2 target, double step)
        {
            double d = DistanceTo(target);
            if (d <= step || d < 0.001)
                return target;
            return new Vec2(X + (target.X - X) / d * step, Y + (target.Y - Y) / d * step);
        }
        public override string ToString() => $"({X:0},{Y:0})";
    }

    /// <summary>
    /// Role stats. Every realm uses the same numbers for the same role, so results are realm-even by construction;
    /// only class names differ (SimRoster).
    /// </summary>
    public sealed class SimKit
    {
        public SimRole Role;
        public double HealthFactor = 1.0;
        public double MeleeFactor = 1.0;
        public int SwingMilliseconds = 3_700;
        public int MeleeRange = 200;
        public bool CastsNukes;
        public bool Heals;
        public bool CastsMez;
        public bool IsNpc;

        public static SimKit For(SimRole role) => role switch
        {
            SimRole.Tank => new SimKit { Role = role, HealthFactor = 1.3, MeleeFactor = 0.9 },
            SimRole.Melee => new SimKit { Role = role, HealthFactor = 1.05, MeleeFactor = 1.3, SwingMilliseconds = 3_300 },
            SimRole.Caster => new SimKit { Role = role, HealthFactor = 0.7, MeleeFactor = 0.35, CastsNukes = true },
            SimRole.Healer => new SimKit { Role = role, HealthFactor = 0.85, MeleeFactor = 0.5, Heals = true },
            SimRole.Mezzer => new SimKit { Role = role, HealthFactor = 0.75, MeleeFactor = 0.4, CastsNukes = true, CastsMez = true },
            SimRole.Lord => new SimKit { Role = role, HealthFactor = 9.0, MeleeFactor = 2.4, SwingMilliseconds = 3_000, IsNpc = true },
            SimRole.Guard => new SimKit { Role = role, HealthFactor = 1.6, MeleeFactor = 1.1, IsNpc = true },
            _ => new SimKit { Role = role },
        };
    }

    /// <summary>Class names by realm and role (labels only; see SimKit).</summary>
    public static class SimRoster
    {
        public static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };

        public static string ClassName(eRealm realm, SimRole role) => (realm, role) switch
        {
            (eRealm.Albion, SimRole.Tank) => "Armsman",
            (eRealm.Albion, SimRole.Melee) => "Mercenary",
            (eRealm.Albion, SimRole.Caster) => "Wizard",
            (eRealm.Albion, SimRole.Healer) => "Cleric",
            (eRealm.Albion, SimRole.Mezzer) => "Sorcerer",
            (eRealm.Midgard, SimRole.Tank) => "Warrior",
            (eRealm.Midgard, SimRole.Melee) => "Berserker",
            (eRealm.Midgard, SimRole.Caster) => "Runemaster",
            (eRealm.Midgard, SimRole.Healer) => "Shaman",
            (eRealm.Midgard, SimRole.Mezzer) => "Healer",
            (eRealm.Hibernia, SimRole.Tank) => "Hero",
            (eRealm.Hibernia, SimRole.Melee) => "Blademaster",
            (eRealm.Hibernia, SimRole.Caster) => "Eldritch",
            (eRealm.Hibernia, SimRole.Healer) => "Druid",
            (eRealm.Hibernia, SimRole.Mezzer) => "Bard",
            (_, SimRole.Lord) => "Keep lord",
            (_, SimRole.Guard) => "Keep guard",
            _ => role.ToString(),
        };

        public static string Short(eRealm realm) => realm switch
        {
            eRealm.Albion => "Alb",
            eRealm.Midgard => "Mid",
            eRealm.Hibernia => "Hib",
            _ => "Npc",
        };
    }

    public sealed class SimCast
    {
        public SimSpell Spell;
        public SimAgent Target;
        public long Start, End;
    }

    /// <summary>One CC spell: a real <see cref="Spell"/> for the policies plus the numbers the model needs.</summary>
    public sealed class SimSpell
    {
        public Spell Spell;
        public BotCasterSurvival.ControlKind Kind;
        public string Name => Spell.Name;
        public int CastMilliseconds => Spell.CastTime;
        public int Radius => Spell.Radius;
        public int Range => Spell.Range;
        public int DurationMilliseconds => Spell.Duration;
    }

    public sealed class SimAgent
    {
        public int Id;
        public string Name;
        public eRealm Realm;
        public int Side;
        public SimRole Role;
        public string ClassName;
        public int Level;
        public SimKit Kit;
        public ISimTactics Tactics;
        public bool IsLeader;
        public bool IsBot; // the AI under test (not the scripted player, not keep NPCs or scripted enemies)
        public SimAgent Leader;
        public List<SimAgent> Group = new();

        public Vec2 Position;
        public double MaxHealth, Health;
        public double Speed = 191;
        public bool Alive => Health > 0;

        // Combat state
        public SimAgent Target;
        public bool AttackingNow;
        public long NextSwing;
        public SimCast Casting;
        public long InterruptedUntil;
        public long MezzedUntil, MezImmuneUntil;
        public SimAgent MezzedBy;
        public long StunnedUntil;
        public bool Unbreakable; // scripted mez that damage does not break (the leader-mezzed scenario)
        public Func<long, bool> ShieldedFromBots; // keep lords: the scenario's door rule
        public List<SimSpell> CcSpells = new();
        public Vec2 Home;
        public long LastAreaMezAttempt = -1;
        public readonly long[] KiteLegStarts = new long[BotCasterSurvival.MaxLegsPerWindow];
        public Vec2? MoveGoal;
        public long NextThink;

        // Aggro: enemy -> amount. Live: AddToAggroList on being hit, leader assist adds 1.
        public readonly Dictionary<SimAgent, double> Aggro = new();
        public SimAgent OrderedTarget; // the group focus this think (CompanionPvpEngagement.Observe -> OrderPull)
        public long NextRecallCheck;
        public long LastDamageTime = -100_000;

        // Per-tick outcome for metrics
        public SimActivity Activity;
        public string Status = "";
        public SimAgentMetrics Metrics = new();

        public bool Controlled(long now) => now < MezzedUntil || now < StunnedUntil;
        public bool Mezzed(long now) => now < MezzedUntil;
        public double HealthPercent => MaxHealth <= 0 ? 0 : Health / MaxHealth * 100;
        public double DistanceTo(SimAgent other) => Position.DistanceTo(other.Position);
        public string Label => $"{Name} ({ClassName})";
        public override string ToString() => Name;
    }

    public sealed class SimEvent
    {
        public long Time;
        public string Text;
    }

    public sealed class SimWorld
    {
        public const int TickMilliseconds = 100;
        public const int ThinkMilliseconds = 500;
        public const int InterruptMilliseconds = 3_000; // server property spell_interrupt_duration (3 s in the shipped DB)
        public const int MezImmunityMilliseconds = 60_000; // PvP mez immunity (docs/PVP-BEHAVIOR-RESEARCH.md Q13)
        public const int AggroRadius = BotBrain.GROUP_DEFENSE_ASSIST_RADIUS; // 2000
        public const int MaxAggroDistance = BotBrain.MAX_AGGRO_LIST_DISTANCE; // 6000
        public const int FollowDistance = 650; // BotState_Idle: follow when farther than this from the leader
        public const int InCombatWindowMilliseconds = 5_000;
        public const double ResistChance = 0.12;

        public readonly List<SimAgent> Agents = new();
        public readonly List<SimEvent> Timeline = new();
        public readonly Random Random;
        public long Now;
        public readonly Dictionary<SimAgent, SimClaim> Claims = new();
        /// <summary>Per side: every enemy that hit a member, and when it last did.</summary>
        public readonly Dictionary<int, Dictionary<SimAgent, long>> LastHitOnSide = new();

        /// <summary>True when the enemy hit a member of the side within the last <paramref name="memoryMilliseconds"/>.</summary>
        public bool HitSideWithin(int side, SimAgent enemy, long memoryMilliseconds) =>
            LastHitOnSide.TryGetValue(side, out var hits) && hits.TryGetValue(enemy, out long at) && Now - at < memoryMilliseconds;

        /// <summary>CompanionPvpEngagement.RecordThreat: 12 s memory, enemy bots only (not keep guards or lords).</summary>
        public bool IsThreat(int side, SimAgent enemy) => !enemy.Kit.IsNpc && HitSideWithin(side, enemy, 12_000);
        public int TimelineLimit = 400;
        private int _nextId = 1;

        public SimWorld(int seed) { Random = new Random(seed); }

        public sealed class SimClaim { public SimAgent Caster; public long Start; public SimSpell Spell; }

        public IEnumerable<SimAgent> Enemies(SimAgent agent) => Agents.Where(a => a.Side != agent.Side && a.Alive);
        public IEnumerable<SimAgent> Allies(SimAgent agent) => Agents.Where(a => a.Side == agent.Side && a.Alive);

        public SimAgent Add(string name, eRealm realm, int side, SimRole role, int level, Vec2 position, ISimTactics tactics)
        {
            SimKit kit = SimKit.For(role);
            var agent = new SimAgent
            {
                Id = _nextId++, Name = name, Realm = realm, Side = side, Role = role, Level = level, Kit = kit,
                ClassName = SimRoster.ClassName(realm, role), Position = position, Home = position, Tactics = tactics,
            };
            agent.MaxHealth = agent.Health = Math.Round(BaseHealth(level) * kit.HealthFactor);
            agent.NextThink = Random.Next(ThinkMilliseconds); // brains do not all think on the same tick
            Agents.Add(agent);
            return agent;
        }

        public static double BaseHealth(int level) => 60 + 22.0 * level;
        public static double SwingDamage(int level) => 8 + 2.2 * level;
        public double NukeDamage(SimAgent a) => SwingDamage(a.Level) * 2.0;
        public double HealAmount(SimAgent a) => SwingDamage(a.Level) * 2.2;
        public const int NukeCastMilliseconds = 2_800;
        public const int HealCastMilliseconds = 3_000;
        public const int NukeRange = 1_500;
        public const int HealRange = 2_000;

        public void Log(string text)
        {
            if (Timeline.Count < TimelineLimit)
                Timeline.Add(new SimEvent { Time = Now, Text = text });
            else if (Timeline.Count == TimelineLimit)
                Timeline.Add(new SimEvent { Time = Now, Text = "(timeline truncated)" });
        }

        /// <summary>True when the agent's side fought in the last few seconds, or an enemy stands within aggro range.</summary>
        public bool SideInCombat(SimAgent agent) =>
            Agents.Any(a => a.Side == agent.Side && a.Alive && Now - a.LastDamageTime < InCombatWindowMilliseconds) ||
            Enemies(agent).Any(e => e.DistanceTo(agent) <= NukeRange);

        public bool ImmuneToMez(SimAgent target) => Now < target.MezImmuneUntil;

        // ---- damage and control

        public void Damage(SimAgent source, SimAgent target, double amount, string how)
        {
            if (!target.Alive || amount <= 0)
                return;
            amount = Math.Min(amount, target.Health);
            target.Health -= amount;
            source.Metrics.DamageDealt += amount;
            target.Metrics.DamageTaken += amount;
            source.LastDamageTime = target.LastDamageTime = Now;
            source.Metrics.LastHitTime = Now;
            target.Metrics.LastTakenTime = Now;
            source.Metrics.DamageByTarget.TryGetValue(target.Name, out double sum);
            source.Metrics.DamageByTarget[target.Name] = sum + amount;
            target.Aggro.TryGetValue(source, out double aggro);
            target.Aggro[source] = aggro + amount;
            if (!target.Kit.IsNpc)
            {
                if (!LastHitOnSide.TryGetValue(target.Side, out var hits))
                    LastHitOnSide[target.Side] = hits = new Dictionary<SimAgent, long>();
                hits[source] = Now;
            }

            if (target.Mezzed(Now) && !target.Unbreakable)
            {
                SimAgent mezzer = target.MezzedBy;
                target.MezzedUntil = Now;
                target.MezImmuneUntil = Now + MezImmunityMilliseconds;
                Log($"{target.Name}'s mez is broken by {source.Name} ({how})");
                if (mezzer != null && mezzer.Side == source.Side)
                {
                    mezzer.Metrics.MezzesBrokenByOwnSide++;
                    source.Metrics.BrokeOwnSideMez++;
                }
            }

            if (target.Casting != null)
            {
                Log($"{target.Name}'s {target.Casting.Spell?.Name ?? "spell"} is interrupted by {source.Name}");
                target.Casting = null;
                target.InterruptedUntil = Now + InterruptMilliseconds;
                target.Metrics.CastsInterrupted++;
                if (target.Metrics.PendingCcCast)
                {
                    target.Metrics.WastedCc++;
                    target.Metrics.WastedCcReasons.Add("interrupted");
                    target.Metrics.PendingCcCast = false;
                }
            }

            if (!target.Alive)
            {
                target.Metrics.Deaths++;
                source.Metrics.Kills++;
                target.Target = null;
                target.Casting = null;
                target.AttackingNow = false;
                Log($"{target.Label} dies to {source.Name} ({how})");
            }
        }

        public void Heal(SimAgent healer, SimAgent target, double amount)
        {
            if (!target.Alive)
                return;
            double done = Math.Min(amount, target.MaxHealth - target.Health);
            target.Health += done;
            healer.Metrics.HealingDone += done;
        }

        /// <summary>Lands a mez on every eligible enemy in the spell's area. Returns how many were mezzed.</summary>
        public int LandMez(SimAgent caster, SimSpell spell, SimAgent center)
        {
            var hit = new List<SimAgent>();
            IEnumerable<SimAgent> inArea = spell.Radius > 0
                ? Enemies(caster).Where(e => e.DistanceTo(center) <= spell.Radius)
                : (center.Alive ? new[] { center } : Array.Empty<SimAgent>());
            int immune = 0, resisted = 0, already = 0;
            foreach (SimAgent e in inArea.ToList())
            {
                if (e.Kit.IsNpc && e.Role == SimRole.Lord) { immune++; continue; }
                if (ImmuneToMez(e)) { immune++; continue; }
                if (e.Mezzed(Now)) { already++; continue; }
                if (Random.NextDouble() < ResistChance) { resisted++; continue; }
                e.MezzedUntil = Now + spell.DurationMilliseconds;
                e.MezzedBy = caster;
                e.Casting = null;
                e.AttackingNow = false;
                hit.Add(e);
            }
            caster.Metrics.EnemiesMezzed += hit.Count;
            string detail = $"{hit.Count} mezzed" + (immune > 0 ? $", {immune} immune" : "") + (resisted > 0 ? $", {resisted} resisted" : "") + (already > 0 ? $", {already} already mezzed" : "");
            Log($"{caster.Name} lands {spell.Name} on {center.Name}: {detail}" + (hit.Count > 0 ? $" [{string.Join(", ", hit.Select(h => h.Name))}]" : ""));
            if (hit.Count == 0)
            {
                caster.Metrics.WastedCc++;
                caster.Metrics.WastedCcReasons.Add(immune > 0 && resisted == 0 ? "all immune" : already > 0 && resisted == 0 ? "already mezzed" : "resisted");
            }
            return hit.Count;
        }

        // ---- the loop

        public void Run(int seconds, Func<SimWorld, bool> stop = null)
        {
            long end = (long)seconds * 1000;
            while (Now < end)
            {
                Step();
                if (stop != null && stop(this))
                    break;
                if (Agents.Where(a => !a.Kit.IsNpc || a.Role == SimRole.Lord).Select(a => a.Side).Distinct().Count() > 1 &&
                    Agents.GroupBy(a => a.Side).Any(g => g.All(a => !a.Alive)))
                {
                    Log("One side is down; the run ends.");
                    break;
                }
            }
        }

        public void Step()
        {
            Now += TickMilliseconds;
            // Interleave the order each tick so neither side always acts first.
            List<SimAgent> order = Agents.OrderBy(_ => Random.Next()).ToList();
            foreach (SimAgent agent in order)
            {
                if (!agent.Alive)
                {
                    agent.Activity = SimActivity.Dead;
                    continue;
                }
                if (agent.Controlled(Now))
                {
                    agent.Activity = SimActivity.Controlled;
                    agent.AttackingNow = false;
                    agent.MoveGoal = null;
                    continue;
                }
                if (agent.MezzedUntil > 0 && agent.MezzedUntil <= Now && agent.MezImmuneUntil < agent.MezzedUntil)
                    agent.MezImmuneUntil = agent.MezzedUntil + MezImmunityMilliseconds;

                if (Now >= agent.NextThink)
                {
                    agent.NextThink = Now + ThinkMilliseconds;
                    agent.Tactics.Think(this, agent);
                }
                Act(agent);
            }
            foreach (SimAgent agent in Agents)
                SimMetricsRecorder.Record(this, agent);
            if (Now % StatusEveryMilliseconds == 0)
                LogStatus();
        }

        public const int StatusEveryMilliseconds = 5_000;

        /// <summary>A status line every few seconds: each side's health, and which bots stand idle in a fight and why.</summary>
        private void LogStatus()
        {
            var parts = new List<string>();
            foreach (var side in Agents.GroupBy(a => a.Side).OrderBy(g => g.Key))
            {
                var list = side.ToList();
                double hp = list.Sum(a => Math.Max(0, a.Health)) / Math.Max(1, list.Sum(a => a.MaxHealth)) * 100;
                parts.Add($"side {side.Key} ({SimRoster.Short(list[0].Realm)}): {list.Count(a => a.Alive)}/{list.Count} up, {hp:0}% HP");
            }
            var idle = Agents.Where(a => a.IsBot && a.Alive && a.Activity is SimActivity.Idle or SimActivity.Following && SideInCombat(a))
                .Select(a => $"{a.Name}: {a.Status}").ToList();
            Log("STATUS " + string.Join(" | ", parts) + (idle.Count > 0 ? " || idle in combat: " + string.Join("; ", idle) : ""));
        }

        /// <summary>Carries out the current intent each tick: movement, casts finishing, melee swings.</summary>
        private void Act(SimAgent a)
        {
            if (a.Casting != null)
            {
                a.Activity = a.Casting.Target.Side == a.Side ? SimActivity.Healing : SimActivity.Casting;
                if (Now >= a.Casting.End)
                    FinishCast(a);
                return;
            }

            if (a.MoveGoal is Vec2 goal)
            {
                a.Position = a.Position.Toward(goal, a.Speed * TickMilliseconds / 1000.0);
                if (a.Position.DistanceTo(goal) < 1)
                    a.MoveGoal = null;
            }

            if (a.AttackingNow && a.Target != null && a.Target.Alive)
            {
                double distance = a.DistanceTo(a.Target);
                if (distance > a.Kit.MeleeRange)
                {
                    a.Position = a.Position.Toward(a.Target.Position, a.Speed * TickMilliseconds / 1000.0);
                    if (a.Activity is not SimActivity.Kiting and not SimActivity.Fleeing)
                        a.Activity = SimActivity.Approaching;
                }
                else
                {
                    a.Activity = SimActivity.Attacking;
                    if (Now >= a.NextSwing)
                    {
                        a.NextSwing = Now + a.Kit.SwingMilliseconds;
                        Damage(a, a.Target, SwingDamage(a.Level) * a.Kit.MeleeFactor * (0.85 + Random.NextDouble() * 0.3), "melee");
                    }
                }
            }
        }

        public bool StartCast(SimAgent a, SimSpell spell, SimAgent target, int castMilliseconds)
        {
            if (Now < a.InterruptedUntil || a.Casting != null)
                return false;
            a.AttackingNow = false;
            a.MoveGoal = null;
            a.Casting = new SimCast { Spell = spell, Target = target, Start = Now, End = Now + castMilliseconds };
            if (spell != null)
            {
                a.Metrics.CcCasts++;
                a.Metrics.PendingCcCast = true;
                Claims[target] = new SimClaim { Caster = a, Start = Now, Spell = spell };
                Log($"{a.Name} starts {spell.Name} on {target.Name}");
            }
            return true;
        }

        private void FinishCast(SimAgent a)
        {
            SimCast cast = a.Casting;
            a.Casting = null;
            SimAgent t = cast.Target;
            if (cast.Spell != null)
            {
                a.Metrics.PendingCcCast = false;
                if (Claims.TryGetValue(t, out SimClaim claim) && claim.Caster == a)
                    Claims.Remove(t);
                if (!t.Alive || a.DistanceTo(t) > cast.Spell.Range + 50)
                {
                    a.Metrics.WastedCc++;
                    a.Metrics.WastedCcReasons.Add(t.Alive ? "target out of range" : "target died");
                    return;
                }
                if (cast.Spell.Kind == BotCasterSurvival.ControlKind.Mez)
                    LandMez(a, cast.Spell, t);
                return;
            }
            if (!t.Alive)
                return;
            if (t.Side == a.Side)
            {
                if (a.DistanceTo(t) <= HealRange + 50)
                    Heal(a, t, HealAmount(a));
                return;
            }
            if (a.DistanceTo(t) <= NukeRange + 50)
                Damage(a, t, NukeDamage(a) * (0.85 + Random.NextDouble() * 0.3), "nuke");
        }
    }
}

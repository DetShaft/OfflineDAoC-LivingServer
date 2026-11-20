using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.UnitTests.BotSim
{
    public sealed class SimTargetChoice
    {
        public long Time;
        public string Target;
        public string TargetClass;
    }

    public sealed class SimAgentMetrics
    {
        public double DamageDealt, DamageTaken, HealingDone;
        public int Kills, Deaths;
        public int CcCasts, WastedCc, EnemiesMezzed, MezzesBrokenByOwnSide, BrokeOwnSideMez, CastsInterrupted;
        public int LeashDrops, Recalls;
        public List<string> WastedCcReasons = new();
        public List<SimTargetChoice> TargetsChosen = new();
        public Dictionary<string, double> DamageByTarget = new();

        public long InCombatMs, IdleInCombatMs, ControlledMs, AliveMs;
        public Dictionary<SimActivity, long> ActivityMs = new();
        /// <summary>Why the bot was idle (or only following) while its side was in combat, in milliseconds.</summary>
        public Dictionary<string, long> IdleReasons = new();

        public double MaxDistanceFromLeader, DistanceSum;
        public long DistanceSamples, MsBeyondAssistRadius;

        // bookkeeping
        public bool PendingCcCast;
        public long LastHitTime = -100_000, LastTakenTime = -100_000;

        public double IdlePercent => InCombatMs == 0 ? 0 : 100.0 * IdleInCombatMs / InCombatMs;
        public double MeanDistanceFromLeader => DistanceSamples == 0 ? 0 : DistanceSum / DistanceSamples;
    }

    public static class SimMetricsRecorder
    {
        public static void Record(SimWorld w, SimAgent a)
        {
            SimAgentMetrics m = a.Metrics;
            if (!a.Alive)
                return;
            long tick = SimWorld.TickMilliseconds;
            m.AliveMs += tick;
            m.ActivityMs.TryGetValue(a.Activity, out long sum);
            m.ActivityMs[a.Activity] = sum + tick;
            if (a.Activity == SimActivity.Controlled)
                m.ControlledMs += tick;
            if (w.SideInCombat(a))
            {
                m.InCombatMs += tick;
                if (a.Activity is SimActivity.Idle or SimActivity.Following)
                {
                    m.IdleInCombatMs += tick;
                    string reason = string.IsNullOrEmpty(a.Status) ? "unknown" : a.Status;
                    m.IdleReasons.TryGetValue(reason, out long r);
                    m.IdleReasons[reason] = r + tick;
                }
            }
            if (a.Leader != null && a.Leader != a && a.Leader.Alive)
            {
                double d = a.DistanceTo(a.Leader);
                m.MaxDistanceFromLeader = Math.Max(m.MaxDistanceFromLeader, d);
                m.DistanceSum += d;
                m.DistanceSamples++;
                if (d > SimWorld.AggroRadius)
                    m.MsBeyondAssistRadius += tick;
            }
        }
    }

    /// <summary>One finished run: who fought whom, and every agent's numbers.</summary>
    public sealed class SimRunResult
    {
        public string Scenario, Variant;
        public int Seed;
        public SimRealmPair Realms;
        public long DurationMs;
        public int WinnerSide = -1; // -1: nobody wiped (time ran out)
        public List<SimAgent> Agents;
        public List<SimEvent> Timeline;
        public Dictionary<string, string> Facts = new();

        public IEnumerable<SimAgent> Side(int side) => Agents.Where(a => a.Side == side);
        /// <summary>The AI under test: side 0's bots (not the scripted player, not keep NPCs).</summary>
        public IEnumerable<SimAgent> Bots(int side = 0) => Agents.Where(a => a.Side == side && a.IsBot);
    }

    public struct SimRealmPair
    {
        public DOL.GS.eRealm Side0, Side1;
        public override string ToString() => $"{SimRoster.Short(Side0)} vs {SimRoster.Short(Side1)}";
    }
}

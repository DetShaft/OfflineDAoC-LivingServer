using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace DOL.UnitTests.BotSim
{
    public sealed class SimFinding
    {
        public string Scenario, Variant, Expectation, Measured;
        public bool Ok, Live;
    }

    /// <summary>Writes a scenario's Markdown report and JSON metrics, and evaluates its expectations.</summary>
    public static class SimReport
    {
        public static string OutputFolder()
        {
            string folder = Environment.GetEnvironmentVariable("BOT_SIM_DIR") is { Length: > 0 } dir
                ? dir : Path.Combine(NUnit.Framework.TestContext.CurrentContext.WorkDirectory, "bot-sim");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static List<SimFinding> Evaluate(SimScenario scenario, IReadOnlyList<SimRunResult> results)
        {
            var findings = new List<SimFinding>();
            foreach (var group in results.GroupBy(r => r.Variant))
            {
                var runs = group.ToList();
                foreach (SimExpectation e in scenario.Expectations)
                {
                    (bool ok, string detail) = e.Check(runs);
                    bool live = SimRunner.AllVariants(scenario).Any(v => v.Name == group.Key && v.Live);
                    findings.Add(new SimFinding { Scenario = scenario.Name, Variant = group.Key, Expectation = e.Text, Measured = detail, Ok = ok, Live = live });
                }
            }
            return findings;
        }

        private static string F(double v) => v.ToString("0", CultureInfo.InvariantCulture);
        private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string Time(long ms) => (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";

        public static string Markdown(SimScenario scenario, IReadOnlyList<SimRunResult> results, IReadOnlyList<SimFinding> findings)
        {
            var sb = new StringBuilder();
            var variants = results.Select(r => r.Variant).Distinct().ToList();
            int perVariant = results.Count / Math.Max(1, variants.Count);
            sb.AppendLine($"# {scenario.Title}");
            sb.AppendLine();
            sb.AppendLine(scenario.Why);
            sb.AppendLine();
            if (scenario.Issues.Length > 0)
                sb.AppendLine($"Issues: {scenario.Issues}. ");
            sb.AppendLine($"Runs: {perVariant} per variant (6 realm pairings x {scenario.Seeds.Length} seeds), up to {scenario.Seconds} simulated seconds each. Level {SimScenarios.Level}. Simulation results are tests only, not in-game evidence.");
            sb.AppendLine();
            sb.AppendLine("Variants:");
            foreach (SimVariant v in SimRunner.AllVariants(scenario))
                sb.AppendLine($"- **{v.Name}**: {v.Description}");
            sb.AppendLine();

            sb.AppendLine("## Expectations");
            sb.AppendLine();
            sb.AppendLine("| Variant | Result | Expectation | Measured |");
            sb.AppendLine("|---|---|---|---|");
            foreach (SimFinding f in findings)
                sb.AppendLine($"| {f.Variant} | {(f.Ok ? "pass" : "**FINDING**")} | {f.Expectation} | {f.Measured} |");
            sb.AppendLine();

            sb.AppendLine("## Side 0 bots by realm");
            sb.AppendLine();
            sb.AppendLine("Side 0 is the player's group (the AI under test). Every realm plays side 0 against both other realms with the same seeds, so the rows should match.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Side 0 realm | Runs | Side 0 won | Idle in combat | Damage dealt / bot | Damage taken / bot | Deaths / bot | CC casts | Wasted CC | Max distance from leader |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (string variant in variants)
                foreach (var realm in SimRoster.Realms)
                {
                    var runs = results.Where(r => r.Variant == variant && r.Realms.Side0 == realm).ToList();
                    var bots = runs.SelectMany(r => r.Bots()).ToList();
                    if (bots.Count == 0)
                        continue;
                    long combat = bots.Sum(b => b.Metrics.InCombatMs);
                    double idle = combat == 0 ? 0 : 100.0 * bots.Sum(b => b.Metrics.IdleInCombatMs) / combat;
                    sb.AppendLine($"| {variant} | {SimRoster.Short(realm)} | {runs.Count} | {runs.Count(r => r.WinnerSide == 0)} | {F1(idle)}% | " +
                                  $"{F(bots.Average(b => b.Metrics.DamageDealt))} | {F(bots.Average(b => b.Metrics.DamageTaken))} | {bots.Average(b => b.Metrics.Deaths):0.00} | " +
                                  $"{bots.Sum(b => b.Metrics.CcCasts)} | {bots.Sum(b => b.Metrics.WastedCc)} | {F(bots.Max(b => b.Metrics.MaxDistanceFromLeader))} |");
                }
            sb.AppendLine();

            sb.AppendLine("## Side 1 bots by realm (both sides' AI, every variant)");
            sb.AppendLine();
            sb.AppendLine("The enemy gamebot group by its own realm: for the upstream 1.2 comparison, the rows of \"live vs upstream 1.2 enemy\" are upstream's bots and the others this fork's autonomous bots.");
            sb.AppendLine();
            sb.AppendLine("| Variant | Side 1 realm | Runs | Side 1 won | Side 0 won | Timed out | Deaths / bot | Damage dealt / bot | CC casts | Wasted CC | Enemies mezzed | Casts interrupted (side 0) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (string variant in variants)
                foreach (var realm in SimRoster.Realms)
                {
                    var runs = results.Where(r => r.Variant == variant && r.Realms.Side1 == realm).ToList();
                    var bots = runs.SelectMany(r => r.Side(1).Where(a => a.IsBot)).ToList();
                    if (bots.Count == 0)
                        continue;
                    int won1 = runs.Count(r => r.WinnerSide == 1), won0 = runs.Count(r => r.WinnerSide == 0);
                    sb.AppendLine($"| {variant} | {SimRoster.Short(realm)} | {runs.Count} | {won1} | {won0} | {runs.Count - won0 - won1} | " +
                                  $"{bots.Average(b => b.Metrics.Deaths):0.00} | {F(bots.Average(b => b.Metrics.DamageDealt))} | {bots.Sum(b => b.Metrics.CcCasts)} | " +
                                  $"{bots.Sum(b => b.Metrics.WastedCc)} | {bots.Sum(b => b.Metrics.EnemiesMezzed)} | {runs.SelectMany(r => r.Bots()).Sum(b => b.Metrics.CastsInterrupted)} |");
                }
            sb.AppendLine();

            sb.AppendLine("## Side 0 bots by role (all realms)");
            sb.AppendLine();
            sb.AppendLine("| Variant | Role | Idle in combat | Longest idle stretch (mean / max) | Damage dealt | Damage taken | Healing | Deaths | Targets chosen | CC casts | Wasted CC | Casts interrupted | Leash drops (support) | Recalls | Close-ups | Melee hits on pets (player near) / all hits | Mean / max distance from leader | Mean / max distance to back line |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (string variant in variants)
                foreach (var role in results.Where(r => r.Variant == variant).SelectMany(r => r.Bots()).GroupBy(b => b.Role).OrderBy(g => g.Key))
                {
                    var bots = role.ToList();
                    long combat = bots.Sum(b => b.Metrics.InCombatMs);
                    double idle = combat == 0 ? 0 : 100.0 * bots.Sum(b => b.Metrics.IdleInCombatMs) / combat;
                    var spaced = bots.Where(b => b.Metrics.BackLineSamples > 0).ToList();
                    string backLine = spaced.Count == 0 ? "-" : $"{F(spaced.Average(b => b.Metrics.MeanDistanceToBackLine))} / {F(spaced.Max(b => b.Metrics.MaxDistanceToBackLine))}";
                    sb.AppendLine($"| {variant} | {role.Key} | {F1(idle)}% | {F1(bots.Average(b => b.Metrics.LongestIdleMs) / 1000.0)}s / {F1(bots.Max(b => b.Metrics.LongestIdleMs) / 1000.0)}s | " +
                                  $"{F(bots.Average(b => b.Metrics.DamageDealt))} | {F(bots.Average(b => b.Metrics.DamageTaken))} | " +
                                  $"{F(bots.Average(b => b.Metrics.HealingDone))} | {bots.Average(b => b.Metrics.Deaths):0.00} | {bots.Average(b => b.Metrics.TargetsChosen.Count):0.0} | " +
                                  $"{bots.Average(b => b.Metrics.CcCasts):0.0} | {bots.Average(b => b.Metrics.WastedCc):0.0} | {bots.Average(b => b.Metrics.CastsInterrupted):0.0} | " +
                                  $"{bots.Average(b => b.Metrics.LeashDrops):0.0} ({bots.Average(b => b.Metrics.SupportLeashDrops):0.0}) | {bots.Average(b => b.Metrics.Recalls):0.0} | " +
                                  $"{bots.Average(b => b.Metrics.CloseUps):0.0} | {bots.Sum(b => b.Metrics.MeleeHitsOnPetsPlayerNear)} / {bots.Sum(b => b.Metrics.MeleeHits)} | " +
                                  $"{F(bots.Average(b => b.Metrics.MeanDistanceFromLeader))} / {F(bots.Max(b => b.Metrics.MaxDistanceFromLeader))} | {backLine} |");
                }
            sb.AppendLine();

            sb.AppendLine("## Why side 0 bots stood idle in combat");
            sb.AppendLine();
            sb.AppendLine("| Variant | Reason | Share of idle time | Bot-seconds |");
            sb.AppendLine("|---|---|---|---|");
            foreach (string variant in variants)
            {
                var reasons = new Dictionary<string, long>();
                foreach (SimAgent b in results.Where(r => r.Variant == variant).SelectMany(r => r.Bots()))
                    foreach (var p in b.Metrics.IdleReasons)
                    {
                        string key = NormalizeReason(p.Key);
                        reasons.TryGetValue(key, out long sum);
                        reasons[key] = sum + p.Value;
                    }
                long total = reasons.Values.Sum();
                foreach (var p in reasons.OrderByDescending(p => p.Value).Take(8))
                    sb.AppendLine($"| {variant} | {p.Key} | {F1(100.0 * p.Value / Math.Max(1, total))}% | {F(p.Value / 1000.0)} |");
            }
            sb.AppendLine();

            var wasted = results.SelectMany(r => r.Bots().SelectMany(b => b.Metrics.WastedCcReasons.Select(w => (r.Variant, w)))).ToList();
            if (wasted.Count > 0)
            {
                sb.AppendLine("## Wasted CC by reason (side 0 bots)");
                sb.AppendLine();
                sb.AppendLine("| Variant | Reason | Count |");
                sb.AppendLine("|---|---|---|");
                foreach (var g in wasted.GroupBy(x => x).OrderBy(g => g.Key.Variant).ThenByDescending(g => g.Count()))
                    sb.AppendLine($"| {g.Key.Variant} | {g.Key.w} | {g.Count()} |");
                sb.AppendLine();
            }

            foreach (string variant in variants)
            {
                SimRunResult first = results.First(r => r.Variant == variant);
                sb.AppendLine($"## Timeline: {variant}, {first.Realms}, seed {first.Seed}");
                sb.AppendLine();
                sb.AppendLine(first.WinnerSide >= 0 ? $"Side {first.WinnerSide} won after {Time(first.DurationMs)}." : $"Time ran out at {Time(first.DurationMs)}.");
                sb.AppendLine();
                sb.AppendLine("| Bot | Role | Damage dealt | Damage taken | Deaths | Idle in combat | Targets (first 4) | Top idle reason |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (SimAgent a in first.Agents.Where(a => a.Side == 0))
                {
                    string targets = string.Join(", ", a.Metrics.TargetsChosen.Take(4).Select(t => $"{t.Target} @{Time(t.Time)}"));
                    string reason = a.Metrics.IdleReasons.Count == 0 ? "" : a.Metrics.IdleReasons.OrderByDescending(p => p.Value).First().Key;
                    sb.AppendLine($"| {a.Name} | {a.Role} | {F(a.Metrics.DamageDealt)} | {F(a.Metrics.DamageTaken)} | {a.Metrics.Deaths} | " +
                                  $"{(a.IsBot ? F1(a.Metrics.IdlePercent) + "%" : "(player)")} | {targets} | {reason} |");
                }
                sb.AppendLine();
                sb.AppendLine("```");
                foreach (SimEvent e in first.Timeline)
                    sb.AppendLine($"{Time(e.Time),7} {e.Text}");
                sb.AppendLine("```");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Folds per-target status text ("single mez on Mid3-...") into one reason row.</summary>
        public static string NormalizeReason(string status)
        {
            int on = status.IndexOf(" on ", StringComparison.Ordinal);
            int from = status.IndexOf(" from ", StringComparison.Ordinal);
            if (status.StartsWith("following leader (", StringComparison.Ordinal))
                return status;
            if (on > 0)
                return status.Substring(0, on);
            if (from > 0)
                return status.Substring(0, from);
            return status;
        }

        public static string Json(IReadOnlyList<SimRunResult> results, IReadOnlyList<SimFinding> findings) =>
            JsonConvert.SerializeObject(new
            {
                findings,
                runs = results.Select(r => new
                {
                    r.Scenario, r.Variant, r.Seed, side0 = r.Realms.Side0.ToString(), side1 = r.Realms.Side1.ToString(),
                    durationMs = r.DurationMs, winnerSide = r.WinnerSide,
                    agents = r.Agents.Select(a => new
                    {
                        a.Name, a.ClassName, role = a.Role.ToString(), realm = a.Realm.ToString(), a.Side, a.IsBot, a.IsLeader,
                        alive = a.Alive, health = Math.Round(a.Health),
                        damageDealt = Math.Round(a.Metrics.DamageDealt), damageTaken = Math.Round(a.Metrics.DamageTaken),
                        healingDone = Math.Round(a.Metrics.HealingDone), a.Metrics.Kills, a.Metrics.Deaths,
                        inCombatMs = a.Metrics.InCombatMs, idleInCombatMs = a.Metrics.IdleInCombatMs, controlledMs = a.Metrics.ControlledMs,
                        ccCasts = a.Metrics.CcCasts, wastedCc = a.Metrics.WastedCc, wastedCcReasons = a.Metrics.WastedCcReasons,
                        enemiesMezzed = a.Metrics.EnemiesMezzed, brokeOwnSideMez = a.Metrics.BrokeOwnSideMez, castsInterrupted = a.Metrics.CastsInterrupted,
                        maxDistanceFromLeader = Math.Round(a.Metrics.MaxDistanceFromLeader), meanDistanceFromLeader = Math.Round(a.Metrics.MeanDistanceFromLeader),
                        msBeyondAssistRadius = a.Metrics.MsBeyondAssistRadius, leashDrops = a.Metrics.LeashDrops, recalls = a.Metrics.Recalls,
                        supportLeashDrops = a.Metrics.SupportLeashDrops, closeUps = a.Metrics.CloseUps, longestIdleMs = a.Metrics.LongestIdleMs,
                        meleeHits = a.Metrics.MeleeHits, meleeHitsOnPets = a.Metrics.MeleeHitsOnPets, meleeHitsOnPetsPlayerNear = a.Metrics.MeleeHitsOnPetsPlayerNear,
                        maxDistanceToBackLine = Math.Round(a.Metrics.MaxDistanceToBackLine), meanDistanceToBackLine = Math.Round(a.Metrics.MeanDistanceToBackLine),
                        targets = a.Metrics.TargetsChosen.Select(t => new { t.Time, t.Target }),
                        idleReasons = a.Metrics.IdleReasons,
                        activityMs = a.Metrics.ActivityMs.ToDictionary(p => p.Key.ToString(), p => p.Value),
                    }),
                }),
            }, Formatting.Indented);

        /// <summary>Writes the scenario's .md and .json files and returns the Markdown.</summary>
        public static string Write(SimScenario scenario, IReadOnlyList<SimRunResult> results, IReadOnlyList<SimFinding> findings)
        {
            string folder = OutputFolder();
            string md = Markdown(scenario, results, findings);
            File.WriteAllText(Path.Combine(folder, scenario.Name + ".md"), md);
            File.WriteAllText(Path.Combine(folder, scenario.Name + ".json"), Json(results, findings));
            // One line per expectation, read by CI for the run summary and ::warning annotations.
            File.AppendAllLines(Path.Combine(folder, "findings.tsv"),
                findings.Select(f => string.Join('\t', f.Ok ? "pass" : "FINDING", f.Scenario, f.Variant, f.Live ? "live" : "compare", f.Expectation, f.Measured)));
            return md;
        }
    }
}

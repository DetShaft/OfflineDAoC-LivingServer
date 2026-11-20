# Bot AI simulation harness

Issue #166. A way to put bots in scripted fights before an in-game test and see how they behave: who they target, how
long they stand idle, what their crowd control (CC) does, who dies, and how far they stray from the leader.

**Simulation results are tests only.** Under the project rules a feature counts as working only once it has been seen
in one of the owner's PC logs. Use the harness to find decision problems early and to compare rule sets, not as proof.

## Running it

- **On every push:** `build-and-test` runs the scenarios with the other server tests. The run page shows a summary
  table, one warning per finding, and a **bot-sim** artifact with one `.md` report (tables plus a timeline) and one
  `.json` file per scenario.
- **On demand, any branch:** Actions, then **bot-sim**, then Run workflow. Pick the branch, optionally one scenario
  name, and optionally *strict* (fail when the live rules miss an expectation). About 10 minutes, mostly the build.
- **Locally (Windows, .NET 10 SDK):**

  ```bash
  dotnet test source/server/Tests/Tests.csproj -c Release --filter "FullyQualifiedName~UT_BotSim"
  ```

  Reports go to `bot-sim/` beside the test binaries, or to the folder in `BOT_SIM_DIR`. Set `BOT_SIM_STRICT=1` to
  fail on findings.

## What a report shows

Each scenario runs every variant in all six realm pairings (Alb vs Mid, Mid vs Alb, and so on) with three seeds.
Side 0 is the player's group: a scripted player (the leader) plus seven bots, the AI under test. Side 1, where there
is one, is a matched autonomous gamebot group that attacks enemies within 1,500 units on sight. The report has:

- **Expectations:** pass, or **FINDING** with the measured value. Findings do not fail the build unless strict.
- **Side 0 bots by realm:** wins, idle time in combat, damage dealt and taken, deaths, CC casts, wasted CC and the
  greatest distance from the leader. The rows must match, because every realm uses the same role stats; the test
  fails if they differ (realm leaked into a rule).
- **Side 0 bots by role**, **why bots stood idle** (the bot's own reason, such as "leader is not attacking; not
  attacked itself") and **wasted CC by reason** (interrupted, all immune, resisted, target died or out of range).
- **A timeline** of the first run of each variant: every target choice, cast, interrupt, mez, broken mez and death,
  plus a status line every 5 s listing the bots idle in combat and why.

Metric definitions:
- *In combat:* the bot's side dealt or took damage in the last 5 s, or an enemy is within 1,500 units.
- *Idle in combat:* in combat and either standing still or only following the leader. Healers with nobody to heal
  count as idle, so the idle expectations leave healers out.
- *Wasted CC:* a CC cast that mezzed nobody (interrupted, every target immune or already mezzed, resisted), or whose
  target died or left range.

## The scenarios

| Name | What it sets up |
|---|---|
| `open-field-8v8` | Two matched groups (tank, 2 melee, 2 casters, mezzer, 2 healers) meet in the open. |
| `keep-lord-after-doors` | A group in the courtyard after the gate and keep door fell, with the upper gatehouse doors standing (Dun Killaloe, 2026-11-19, #157 / #160). Compares the live door rule with the rule before #160. |
| `cc-heavy` | Both groups bring three mezzers, one with the slow backup area mez (#151 / #152). |
| `leader-mezzed` | The player is mezzed 2 s in, before contact, for 25 s while the enemy charges, with an enemy still selected. The case that motivated fight mode. |
| `chase-leash` | Two enemy melee harass the group for 8 s, then run straight away. The player never moves. Measures how far bots chase. |

Variants (side 0's rules):
- **fight mode off**: the rules before #165 (server setting `bots / group_fight_mode` = False).
- **fight mode (#165)**: the shipped default since PR #168. This is the *live* variant: CI warns about its findings and
  strict mode fails on them.
- `keep-lord-after-doors` also runs **rule before #160** (the old door rule) to show what #160 changed.
- `open-field-8v8` also runs **mirror control**: side 0's bots use the enemy's autonomous rules, so both sides are
  identical. It should win about half; if it does, the other variants' win rates come from their rules, not the setup.

## What it simulates, and what it does not

The real server needs the world database, spell data, class specs and navmeshes, and none of those are in Git or CI.
So the harness does **not** boot the server or run `BotBrain` itself. It runs a small model on a flat field and calls
the real server code wherever a bot rule is a pure function.

**Real server code used:**
- `BotPvpCrowdControlPolicy`: CC spell order, the 60 s area mez hold, 2+ free enemies for an area mez, no mez on
  enemies groupmates are hitting. The CC spells are real `Spell` objects.
- `BotPvpCrowdControl.ClaimCastAlive` and `FreeEnemyRadius`: when a groupmate's mez claim protects a target, and when a
  mezzed enemy is left alone.
- `BotCasterSurvival.Decide`, `LegsLeft`, `AwayPoint`, `KindOf`: the caster kite decision and kite legs.
- `GuardLord.BlocksTheWayIn`: whether the keep's doors still shield the lord from bots.
- `GroupFightPolicy` (fight mode, #165, #170): `PickFocus`, `PickPeel`, `BeyondLeash`, `FollowsLeaderTarget`, `ShouldExit`,
  `ShouldReplaceFocus`, `ShouldSwitchToFocus`, `AnchorIndex` (the group anchor once the leader is dead), the 15 s
  threat memory and the 1,500 chase distance.
- `BotPvpCrowdControlPolicy.CastSurvives` (#170): no cast-time CC while an enemy is hitting or casting at the caster. The scan, the leader-target rule and the leash loop follow
  `PlayerGroupFightMode` and `BotBrain.ThinkGroupFightMode` by hand.
- `TemporaryCompanionRecovery`: the 3,000-unit recall distance and 2 s check.
- Constants: assist radius 2,000 (`GROUP_DEFENSE_ASSIST_RADIUS`) and aggro list distance 6,000.

**Mirrored by hand from `BotBrain` (could drift if the brain changes):**
- Bots assist the leader's target only while the leader is attacking or casting a harmful spell (BotState_Idle /
  BotState_Follow), and otherwise fight enemies that hit them (the aggro list).
- The companion group focus (`CompanionPvpEngagement.Observe`): the player's enemy target, else the enemy that hit a
  groupmate in the last 12 s nearest the player, ordered onto every bot within 2,000 units. Healers hold support.
- The companion recall: past 3,000 units from the player with no groupmate near the bot fighting, the bot is moved
  back to the player.
- Fight mode's target roles are mapped by role, not class (healers, casters and mezzers, melee, keep NPCs as pets), so
  every realm is described the same way.
- They keep the current target, then pick the most-damaging enemy on the aggro list, then the nearest.
- They follow the leader when farther than 650 units, and drop aggro beyond 6,000 units from themselves.
- Healers heal the lowest groupmate under 75% and take no offense.
- Defended attackers (12 s threat memory) are exempt from mez protection, except once mezzed (#170).

**Modelled simply, not from game data:**
- Health, swing damage, nukes and heals scale with level and role, the same in every realm.
- Any damage interrupts a cast and blocks casting for 3 s (`spell_interrupt_duration`).
- A mez breaks on damage, then gives 60 s of immunity, and has a 12% resist chance.
- Movement is straight lines at 191 units/s with no terrain, line of sight or navmesh.

**Not simulated:** power and endurance, styles and positionals, buffs, stuns and roots (only mez), pets, songs,
stealth, archers, realm abilities, line of sight, terrain, siege, the autonomous gamebot goal layer (travel, camps,
group orders), real class spell lists, and Darkness Falls or raid AI.

## Adding a scenario

1. In `source/server/Tests/BotSim/SimScenarios.cs`, add a method that returns a `SimScenario`: a name, a title, a
   `Build` function that places agents (use `AddGroup` for a group, `world.Add` for single NPCs), and a few
   `SimExpectation`s.
2. List it in `SimScenarios.All()` and in `UT_BotSim.ScenarioNames()`; a test checks the two lists match.
3. Keep it realm-even: build both sides from the same roles and let the runner rotate realms.

## Testing a new rule set (for example active fight mode)

Write an `ISimTactics`, or subclass `CurrentBotTactics` and override `FightModeTurn`, `ChooseTarget`,
`TryCrowdControl`, `SkipForCrowdControl` or `CasterTurn` (`FightModeTactics` is the example), then add a `SimVariant`
with it (`SimVariant.FightMode()` shows how; set `Live` on the one that ships). The report then shows it beside
**current** in every table. When the new rules move into `BotBrain`, move the shared decision into a pure static
function in the server and call it from both, so the harness tests the shipped code rather than a copy.

Files: `source/server/Tests/BotSim/` (`SimCore.cs` world and combat, `SimTactics.cs` decisions, `SimScenarios.cs`
catalog, `SimMetrics.cs`, `SimReport.cs`, `UT_BotSim.cs`), `tools/bot-sim/summarize.sh`,
`.github/workflows/bot-sim.yml`.

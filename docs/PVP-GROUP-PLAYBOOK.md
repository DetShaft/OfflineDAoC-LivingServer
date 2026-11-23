# Group PvP playbook: how a real group fight runs, and what our bots do at each step

Status: research only (2026-11-23, issue #138). No code, data or balance changes. Tests only: nothing here was checked
in game. Proposed AI changes are filed as separate GitHub issues (listed at the end) and are not implemented.

## Why this doc exists, and how it relates to the others

- [PVP-BEHAVIOR-RESEARCH.md](PVP-BEHAVIOR-RESEARCH.md) (PR #155, #156) answers 24 questions about classic PvP: class
  roles, compositions, ranges, CC immunity history, Minstrel Lullaby data, siege roles and per-class tables. This doc
  does **not** repeat those facts; it points to them as "Research Qn".
- This doc looks at the same material from a different angle: a group fight as a **sequence of phases**, the job each
  role has in each phase, and the bot code that runs that phase today. The goal is to show where the bot AI (fight mode,
  crowd control, healer posts, battleground keep fights) is missing a step, not to list facts again.
- [BOT-AI-SIMULATION.md](BOT-AI-SIMULATION.md) describes the harness that any change proposed here must go through
  first (Jay's rule, 2026-11-19).

**Bot types.** These stay separate throughout:
- **Players:** real people.
- **Player-led bots:** companions, and gamebots in the player's group. They run group fight mode (#165, setting
  `bots / group_fight_mode`, `PlayerGroupFightMode.cs`, `GroupFightPolicy.cs`).
- **Autonomous groups:** gamebot groups no player leads (frontier groups, battleground parties from #188, siege
  parties). They do **not** run fight mode today.

**Labels**, as in the research doc: **Confirmed** (patch notes or source code), **Consensus** (several community sources
agree), **Unsure** (one source, or opinion). Code references were read on `main` at 3f4b819 (2026-11-23).

**Era.** Server context is Classic + SI (client 1.127, PROGRESSION-DIRECTION.md). Most new sources here are 2002-2006
(classic to post-ToA) or 2017-2021 freeshard (1.65-rules Uthgard, Eden 1.90+). Era is noted where it matters.

**Source limits found this time.** Eden's forum (`eden-daoc.net/viewtopic.php`) now refuses automated fetching
(robots.txt), including its "Assist group leader" macro thread and class guides, so the Eden material here is limited
to two readable threads and what PR #155 already had. If Jay wants the Eden macro and 8v8 threads covered, saving
them as PDFs (as with the Camelot Herald pages) is the way in.

## 1. The fight as phases

Every source that describes a full fight describes roughly the same shape (Consensus: FH-FG, BT-sorc, UTH-W,
FH-HT, CA-101 and UTH-G from the research doc). The phases below are a synthesis; no single source lists them in this
form.

| Phase | What decides it | Players | Bots today | Gap |
|---|---|---|---|---|
| 0. Approach | Who sees whom first, and from where | Leader calls engage or turn away at first sight (Research Q18). Casters stand at max range; the group spreads out. | Player-led: the player decides. Autonomous: attack enemies within 1,500 on sight. | Autonomous groups never decline a fight. Retreat is #190. |
| 1. Opener (control war) | Whose CC lands first on the other side's supports | Long-range AE mez (Sorcerer 1,875) on the enemy group; the goal is to catch supports and **force the enemy's purges** (BT-sorc). Interrupt everything that casts. | `BotBrain.PvpCrowdControl.cs`: area mez on the densest cluster, 2+ free enemies, then a 60 s hold (#152). | Target choice is by cluster size and distance, never by role (see 3.2). |
| 2. Kill window (train) | Damage on one target before it is healed or guarded | One called target; tank train plus debuff nukes are the strongest damage (FH-FG). Debuff first, then the train hits. | Player-led: player's target, else fight mode's focus (healer > caster > fighter > pet, `GroupFightPolicy.PickFocus`). Autonomous: aggro list, highest threat. | Autonomous groups have no shared target (see 3.4). |
| 3. Sustain | Who recovers from CC faster | Demezz supports first (BT-sorc order), purge, re-mez loose enemies, root tanks diving the healers, guard the trained teammate. | Cure mez: the player first, then group order (`BotBrain.cs` ~5730). Purge: on any mez or stun in an RvR zone. No Guard or Intercept. | No role order for demezz; no guard (see 3.1, 3.3). |
| 4. Finish or disengage | Supports alive, power left, CC up | Commit when the enemy supports are down or CC'd; disengage when the opener is lost (Research Q18). | Fight mode exits 6 s after the last hostile. No retreat rule. | #190 (not started). |
| 5. Reset | Time before the next group arrives | Rez, rebuff, re-summon pets, leave the spot (CA-101). 8v8 etiquette lets the other side rebuff too (MG-8v8). | Rest recovery, rebuffs, resurrection reservations exist for raids. | Fine for now; etiquette is a gamebot design idea only. |

### What a phase looks like in a timeline

Useful when reading a bot-sim timeline or a server log against real play (Consensus for the order, Unsure for the
seconds, which come from cast times in the research doc rather than any source that times a whole fight):

1. **0-3 s:** both mezzers start their AE mez from range (3 s cast for Sorcerer, Bard, Healer; 5 s for the Minstrel's
   Lullaby, which is why it is a poor opener). Everyone else who can interrupts the enemy mezzer.
2. **3-8 s:** whoever lands first has the window. The mezzed side purges (instant) or waits for a demezz (a cast). The
   other side calls a target and starts the train on a free enemy support.
3. **8-30 s:** the kill window. A mezzed enemy is never hit (the mez is worth more than the damage). Loose enemies are
   re-mezzed with single-target mez, divers are rooted, tanks guard the trained teammate.
4. **30-60 s:** the AE mez wears off or was broken; everyone it touched is immune for 60 s (Research Q14), so the fight
   becomes melee, roots, stuns and snares until the immunities fade. This is where groups win on healing and peel.

## 2. Role playbooks

Each list is the decision order a player in that role follows, written by role so the same rule applies in every realm.
The per-realm table in section 4 says which classes take each role.

### 2.1 Mezzer (primary CC)
1. Open with the AE mez on the enemy group from max range. Aim for the cluster that holds their supports (BT-sorc).
2. Re-mez loose players with single-target mez, supports first (BT-sorc).
3. Root enemy tanks that are diving your healers; root tanks peeling your back line (BT-sorc).
4. Debuff free-casting enemy supports and casters (BT-sorc).
5. Demezz your own group in the order in 2.2 if you carry a cure mez (Sorcerer, Healer and Bard got a baseline cure
   mez in 1.52: Confirmed, CH-1.52 in the research doc).
6. A target that speed-warps away and nukes from range gets rooted, and the root is not broken unless the kill is
   certain (Unsure: FH-FG).

### 2.2 Healer
1. Stay alive and in range of the group (Research Q7).
2. **Demezz in this order:** healers, then the group's main CC (Minstrel in BT-sorc's Albion example), then casters,
   then the front line (Consensus: BT-sorc gives the full order; FH-FG and UTH-W agree supports come first).
3. Instant heals when someone is low and no cast heal has started; big heals are slow and rarely used in RvR
   (Unsure: FH-HT, 1.50-era Midgard Healer).
4. Spend free seconds on offense against enemy supports: stun, mez, interrupts (FH-FG: "Healers and clerics should go
   offensive on enemy support"; FH-HT lists mez, stun and attack-speed debuff on the healer's bar).
5. Two healers split the job: one heals and cures, the other controls (Research Q7).

### 2.3 Peel tank (guard)
1. Guard the teammate the enemy is training. Everyone in the group keeps track of who the guard is, so the trained
   teammate can stay near the guard (FH-FG; the Hero guide adds the guard must stand very close to the guarded player).
2. Spread Protect and Intercept over the casters and healers, not stacked on one (Unsure: AZ-HERO, summary part).
3. Save the long shield stun (about 8 s, a level 42 shield style in the Hero guide) for a real threat (AZ-HERO;
   Research Q8).
4. Do not stun a target with high Determination or Purge unless it will die during the stun: a free target with an
   area snare punishes the group (Unsure: FH-FG, 2006).
5. Grapple-type locks are for the enemy's heaviest melee, or a target the casters can nuke down (Unsure: FH-FG; a
   post-classic realm ability).

### 2.4 Train melee
1. Hit the called target only. Two tanks that can assist are the minimum for a tank train (FH-FG).
2. Never hit a mezzed enemy (Research Q17).
3. Tank trains pick the enemy's damage first when the enemy is melee-heavy ("no DPS no mid group": UTH-W, a Hibernian
   naturalist group against Midgard on Uthgard, 2018). Against a caster or support group, supports first (Research Q17).
4. Stop at the leash. A train that overextends gets its own supports caught (UTH-W: the Hibernian warden landed its
   area root on Midgard supports after their tank train overextended).

### 2.5 Nuker
1. Assist the caller. In Albion caster groups the Cabalist often calls; the Sorcerer adds the body debuff and lifetaps
   (BT-sorc).
2. Cast one to three spells from a spot, then move; run back toward your own CC when tanks come (Unsure: FH-FG reply).
3. Spread out rather than "boxing up" next to each other, so one tank's swings or one AE can't interrupt several
   casters (Consensus: FH-FG replies, UTH-G). Pre-ToA groups did box casters around the guard against melee groups, and
   the guide author defends that against tank-heavy groups (FH-FG), so this is a matchup choice, not a fixed rule.

### 2.6 Interrupter and pet classes
1. Interrupt any enemy that casts, even when nothing better is happening: "everyone should interrupt when they have a
   free second" (FH-FG).
2. Kill enemy pets early, even before the mez, because they come back and they interrupt casters. Casters do it, because
   buffed pets resist melee but have low magic resistance (Unsure: FH-FG, 2006). This is the casters' job, not the
   train's: #175 (melee bots stay on players while an enemy player is near) stays right.

### 2.7 Purge
- Purge removes the mez or stun but leaves the immunity timer (Research Q14). The opener's point is to make the enemy
  spend it (BT-sorc). After 1.50, tanks with Purge made mezzes less decisive in RvR (Unsure: FH-HT, 2002).
- **No source gives a timing rule** beyond "purge when CC'd and the fight needs you". The current bot rule (purge on any
  mez or stun in an RvR zone, `AutonomousBotRealmAbilities.Wanted`) matches that. No change proposed.

## 3. Where our bots differ, by system

### 3.1 Cure mez order (all bots with a cure mez)
- **Now:** `BotBrain.CheckHeals` (~line 5730) cures the assisted player first, then the first mezzed groupmate in group
  order. A mezzed tank listed before a mezzed Cleric is cured first.
- **Players:** healers, then the main CC, then casters, then the front line (2.2).
- **Proposal:** order cure mez by role, after the player. Filed as #199.

### 3.2 PvP crowd control target choice (all bots with CC)
- **Now:** `BotBrain.PvpCrowdControl.cs` picks the candidate with the most free enemies around it, then the nearest.
  For a single-target mez or root that means "the nearest legal enemy". It already skips immune targets, targets
  groupmates are hitting, and fight mode's focus (#152, #170).
- **Players:** area mez on the cluster holding the supports; single mez on loose supports first; roots on tanks diving
  the back line (2.1).
- **Proposal:** keep the area rule, and rank single-target CC by the same role order fight mode already uses
  (`GroupFightPolicy.RoleOf`), with roots preferring enemies that are hitting the back line (`Candidate.HittingBackLine`
  already exists). Filed as #200.

### 3.3 Guard, Intercept and Protect (tanks)
- **Now:** no bot uses them. `BotBrain.cs` (~line 224) only reads Protect effects other players placed. Peeling is
  "attack the enemy hitting the back line" (`GroupFightPolicy.PickPeel`, #136).
- **Players:** Guard on the trained teammate, moved as the enemy switches; Protect and Intercept spread over the
  casters (2.3). Eden doubles Guard's block bonus per level and makes Protect stronger against pets (Research Q2),
  which shows how central these abilities are to the 8v8 game.
- **Proposal:** tanks in fight mode guard the most-attacked healer or caster within range and move the guard when the
  enemy's focus changes. Filed as #201.

### 3.4 Autonomous groups have no group fight
- **Now:** fight mode (#165) covers only player-led bots. Autonomous groups (#188 battleground parties, frontier
  groups, siege parties) use each bot's own aggro list and the member-help rule in `AutonomousGroupCombat.cs`. The
  bot-sim enemy side models exactly this ("attacks enemies within 1,500 on sight").
- **Players:** every real group runs an assist train (Research Q6). An enemy group that does not is easy prey, which
  matters both for fair fights against the player and for bot-vs-bot battleground fights.
- **Proposal:** let an autonomous group pick a main assist (its leader, or a caster in caster groups) and use the same
  `GroupFightPolicy` focus and peel rules. Filed as #202. It touches gamebot difficulty, so it should come with a
  setting and a harness run before it ships.

### 3.5 Things already matching play (no change)
- Area mez as an opener with a 60 s hold; no mez on immune or hit targets (#152).
- Never hit a mezzed enemy while a free one is near; pets yield to players for melee (#170, #175).
- Casters kite under attack, close up to 900 of the melee when idle (#136, #177).
- Purge on CC in RvR zones (2.7).
- Two leashes (player and back line) against overextending trains (#177; matches 2.4 item 4).

### 3.6 What the harness can't test yet
The harness models only mez: no stuns, roots, purge, guard, cure mez or pets' own rules (BOT-AI-SIMULATION.md, "Not
simulated"). Sections 3.1-3.3 can't be measured until it can. Filed as #203, to land before or with the others.

## 4. Who each rule touches, per realm

Same rule in every realm; only the class names differ. "Data" means the bot reads it from its own spell or ability
list, so the class names are examples, not a hard-coded list.

| Rule | Albion | Midgard | Hibernia | Basis |
|---|---|---|---|---|
| Opener AE mez | Sorcerer (Minstrel only as backup) | Healer (Pacification) | Bard | Research Q13, Q14 |
| Baseline cure mez (1.52) | Sorcerer | Healer | Bard | CH-1.52; others by spec (data) |
| Other demezzers seen in groups | Cabalist ("3 demezzer" body group) | Spiritmaster | None found in sources (data decides) | UTH-V, EDN-OSP; data decides |
| Guard / Protect / Intercept | Armsman, Paladin (shield users) | Warrior, Thane | Hero, Champion | Research Q8, AZ-HERO; data decides |
| Main CC to demezz second | Minstrel, Sorcerer | Healer (Pacification), Skald | Bard, Enchanter | BT-sorc (Albion); same role elsewhere |
| Typical caller | Cabalist or tank | Tank in melee trains | Enchanter in caster groups | Research Q6, BT-sorc |

The realm identities hold: Hibernia's naturalist and caster groups rely on roots and kiting, Midgard's on the tank
train, Albion's on pets, interrupts and its Clerics. None of the proposals moves damage, healing or CC numbers.

## 5. Proposed issues (filed, not started)

Each one names this doc, keeps bot types apart and asks for a bot-sim run before release:

1. #199: cure mez by role order (3.1).
2. #200: role-aware target choice for single-target PvP CC (3.2).
3. #201: tanks use Guard, Intercept and Protect in fight mode (3.3).
4. #202: autonomous groups get a main assist and the fight-mode focus rules (3.4).
5. #203: bot-sim harness models stuns, roots, cure mez, purge and guard (3.6).

Already filed elsewhere and not repeated: group retreat (#190), song twisting (#32), group spacing and role-based spots
(#56), buffing review (#164).

## 6. Open questions

- **Purge timing:** no source gives one (2.7).
- **Demezz order outside Albion:** only BT-sorc gives a full order, written for Albion. Applying it by role to the
  other realms is our inference.
- **Eden 8v8 practice:** Eden's forum can't be fetched automatically now. The 1.90+ meta (speed and purge wars, Eden's
  guard and protect changes) is covered only through the rules in Research Q2.
- **Numbers:** no source gives seconds per phase, health thresholds for guard switches, or a retreat threshold. Treat
  them as settings.

## Sources (accessed 2026-11-23)

Keys used in the research doc (BT-*, CH-*, UTH-G, CA-101, EDN-*) are listed there. New ones:

| Key | Source |
|---|---|
| FH-FG | FreddysHouse, "The newbie guide to FG RvR" (Dave J., 29 Jun 2006, with replies to 2 Jul 2006): https://forums.freddyshouse.com/threads/the-newbie-guide-to-fg-rvr.198488 |
| FH-HT | FreddysHouse, "Healer tips" (Jul 2002, patch 1.50/1.51 era): https://forums.freddyshouse.com/threads/healer-tips.91814 |
| BT-sorc | Blackthorn wiki, "Sorcerer Guide" (opener, demezz order, caller): https://blackthorn-daoc.com/wiki/sorcerer-guide |
| UTH-W | Uthgard forum, "How viable are wardens in 8 man?" (Jun 2018): https://www.uthgard.net/forum/viewtopic.php?p=463434 |
| UTH-V | Uthgard forum, "Viable RvR Groups" (Feb 2017): https://www.uthgard.net/forum/viewtopic.php?p=412661 |
| UTH-Z | Uthgard forum, "What made Hib/Mid/Alb zerg work so well?" (Aug 2018; early-2002 play before immunity timers): https://www.uthgard.net/forum/viewtopic.php?p=471494 |
| EDN-OSP | Eden forum, "Old School Players coming to Eden" (group roles for returning players): https://eden-daoc.net/viewtopic.php?p=13696 |
| MG-8v8 | Madgrim et al., "Nurturing the 8v8 Population in Dark Age of Camelot" (Apr 2021; 8v8 etiquette, Ellan Vannin): https://sites.google.com/view/daoc-proposals/nurturing-8v8 |
| AZ-HERO | Allakhazam, "Gruesome's Big Fat Hero Guide" v1.0 (2002; Guard, Protect, Intercept; partly read from a summary): https://camelot.allakhazam.com/Hero_Guide.html |

Tried and not usable: Eden "Macros - Assist group leader" (t=4494) and "Beginner's Guide to Thane" (t=3466), both
refused by robots.txt; Allakhazam "Crowd Control by Class" (World of Warcraft content); Camelot Herald "Combat
Basics" (PvE only); DOL forum "A long list of daoc Basics" (crafting and stats, no group play).

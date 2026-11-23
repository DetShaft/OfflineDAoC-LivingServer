# Class balance and expansion progression: Phoenix, Eden and this server

Issue #108. Written 2026-11-23. **Research only.** Nothing in this document changes game data, spells or code.
Every item under "Recommendations" needs Jay's decision before any work starts, and any change that follows
still needs the per-realm summary table and a run through the bot sim harness (docs/BOT-AI-SIMULATION.md).

Companion document: [docs/PVP-BEHAVIOR-RESEARCH.md](../PVP-BEHAVIOR-RESEARCH.md) (PR #155). Its Table 1 holds the
full per-class Phoenix and Eden notes, with sources. This document does not repeat all of that. It adds what #108
asked for and the PvP doc did not do:

1. what **this server runs today** for every class, read from the code;
2. a **comparison** of today's values with the live damage table, Phoenix and Eden;
3. **recommendations** that treat every realm the same way, each with a per-realm table;
4. which Eden and live-patch changes could become **era-gate settings** (docs/PROGRESSION-DIRECTION.md).

## Ground rules used

- **Reference order.** Phoenix is the closest variant to this server and the main balance reference. Eden has
  the best PvP commentary and the most interesting expansion-progression ideas. Eden runs 1.90-based class data,
  so its class changes are reference only. **No Eden spell changes.**
- **Era.** Class and world data are OpenDAoC classic-era data. Jay's client is 1.127. Where a rule comes from a
  live patch, the patch number is given in *(italics)*.
- **Realm identity is not imbalance.** Hibernia is caster heavy, Midgard is melee heavy, and Albion has one pure
  healer (Cleric) but a balanced mix. Warlock is probably not really in the game, Vampiir is not implemented, and
  the Necromancer acts only through its pet. Uneven class counts that come from the real roster are reported, not
  "fixed". What must be even is the **rule**: a recommendation applies the same rule to every realm.
- **Confidence labels** (same as the PvP doc): **Confirmed** (patch note, source code or data), **Consensus**
  (several sources agree), **Unsure** (single source, search snippet or offline page).
- **Tests only.** Nothing here has been seen in one of Jay's PC logs. It is a desk study.

## Sources read for this document

| Key | Source | Notes |
|---|---|---|
| CODE | This repo, `main` at 3f4b819: `playerclasses/*/*.cs`, `gameobjects/CharacterClasses/CharacterClassBase.cs`, `ECS-Components/AttackComponent.cs`, `ECS-Components/Actions/AttackAction.cs`, `skillhandler/ProtectAbilityHandler.cs`, `spells/CCSpellHandler.cs`, `serverproperty/ServerProperties.cs`, `realmabilities_atlasOF/` | Read 2026-11-23. Spell data lives in the database, which is not in the repo, so spell values are not compared here. |
| EDN-W | Eden wiki "Class Changes from 1.90", PDF supplied by Jay (2026-11-17 export) | Read in full for this document. |
| CH-DT | Camelot Herald wiki "Damage Table", PDF supplied by Jay | Live per-weapon damage factors (current page, not era-tagged). |
| CH-1.5x/1.6x/1.7x | Camelot Herald patch notes 1.50 to 1.72, 1.104, 1.108, 1.111, PDFs supplied by Jay (2026-11-19) | 1.56 Warrior weapon skill, 1.68 interrupt, 1.72 pet stun immunity checked directly. |
| PX-S1 | Phoenix Season 1 patch notes (Wayback copy), as summarised in PVP-BEHAVIOR-RESEARCH.md | The Wayback copy was blocked for automated fetching on 2026-11-23 and the Phoenix site is offline, so Phoenix items are taken from the PvP doc and keep its labels. |
| PVP | docs/PVP-BEHAVIOR-RESEARCH.md, Table 1 and section A | Full per-class Phoenix/Eden columns and the source list. |
| #35/#109 | Bonedancer research comment on #108 | Eden Bonedancer comparison; Jay chose none of those changes (2026-11-17). |

## 1. What this server runs today

### 1.1 Class constants, per realm

Read from the class scripts (inheritance resolved). Columns:

- **Spec mult**: specialization points per level ×10 (20 = 2.0 points per level).
- **WS base**: `m_wsbase`, the class's melee weapon-skill base. It plays the role of the live "damage table
  factor": WS base ÷ 20 gives the factor (Warrior 460 = 23, Paladin 380 = 19).
- **Live factor**: the live damage table (CH-DT) for the class's main weapon lines. Live uses one factor per
  *weapon line*; this server uses one value per *class*.
- **Base HP**: `m_baseHP`.
- **Pulses**: maximum active pulsing spells or songs (default 1).
- Every class uses the same ranged weapon-skill base, 440 (factor 22). Live lists 18 for Infiltrator crossbow
  and Hero/Blademaster shortbow and 20 for Mercenary shortbow; archer classes use their own archery formulas.

#### Albion

| Class | Spec mult | WS base (factor) | Live factor, main lines | Base HP | Pulses | Match? |
|---|---|---|---|---|---|---|
| Armsman | 20 | 440 (22) | 22 | 880 | 1 | Yes |
| Mercenary | 20 | 440 (22) | 22 | 880 | 1 | Yes |
| Paladin | 20 | 380 (19) | 19 (20 staff) | 760 | 2 | Yes |
| Reaver | 20 | 380 (19) | 19 (20 staff) | 760 | 1 | Yes |
| Friar | 15 | 380 (19) | 18 crush, 20 staff | 720 | 1 | Averaged: 1 high on crush, 1 low on staff |
| Scout | 20 | 360 (18) | 19 slash/thrust, 18 crush/staff | 720 | 1 | **1 low on slash/thrust** |
| Infiltrator | 25 | 360 (18) | 18 | 720 | 1 | Yes |
| Minstrel | 15 | 360 (18) | 18 | 720 | 2 | Yes |
| Cleric | 10 | 320 (16) | 16 | 720 | 1 | Yes |
| Wizard | 10 | 240 (12) | 12 staff | 560 | 1 | Yes |
| Sorcerer | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Theurgist | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Cabalist | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Necromancer | 10 | 280 (14) | 14 staff | 560 | 1 | Yes (fights through its pet) |

#### Midgard

| Class | Spec mult | WS base (factor) | Live factor, main lines | Base HP | Pulses | Match? |
|---|---|---|---|---|---|---|
| Warrior | 20 | 460 (23) | 23 (raised in *1.56*) | 880 | 1 | Yes |
| Berserker | 20 | 440 (22) | 22 | 880 | 1 | Yes |
| Savage | 15 | 400 (20) | 20 | 880 | 1 | Yes |
| Thane | 20 | 360 (18) | 18 axe/sword, 19 hammer | 720 | 1 | **1 low on hammer** |
| Skald | 15 | 380 (19) | 19 | 760 | 2 | Yes |
| Hunter | 20 | 380 (19) | 19 | 720 | 1 | Yes |
| Shadowblade | 22 | 360 (18) | 18 | 760 | 1 | Yes |
| Healer | 10 | 360 (18) | 18 | 720 | 1 | Yes |
| Shaman | 10 | 360 (18) | 18 | 720 | 1 | Yes |
| Runemaster | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Spiritmaster | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Bonedancer | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Warlock | 10 | 280 (14) | 14 staff | 560 | 1 | Not really in game |

#### Hibernia

| Class | Spec mult | WS base (factor) | Live factor, main lines | Base HP | Pulses | Match? |
|---|---|---|---|---|---|---|
| Hero | 20 | 440 (22) | 22 | 880 | 1 | Yes |
| Blademaster | 20 | 440 (22) | 22 | 880 | 1 | Yes |
| Champion | 20 | 380 (19) | 19 | 760 | 1 | Yes |
| Valewalker | 15 | 420 (21) | 21 scythe | 720 | 1 | Yes |
| Ranger | 20 | 360 (18) | 18 | 720 | 1 | Yes |
| Nightshade | 22 | 360 (18) | 18 | 720 | 1 | Yes |
| Bard | 15 | 360 (18) | 18 | 720 | 2 | Yes |
| Warden | 15 | 360 (18) | 18 | 720 | 2 | Yes |
| Druid | 10 | 320 (16) | 16 | 720 | 1 | Yes |
| Eldritch | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Enchanter | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Mentalist | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Animist | 10 | 280 (14) | 14 staff | 560 | 1 | Yes |
| Sluaghbinder (custom, 0.37b edition) | 10 | 380 (19) | none (custom class) | 760 | 1 | No reference; see R9 |
| Vampiir | 15 | 440 (22) | not on the classic table | 878 | 1 | Not implemented |

**Finding 1 (Confirmed: CODE, CH-DT).** The class constants match the live damage table for every Classic/SI class
except three single-weapon cases: Friar (one value for crush and staff), Scout slash/thrust (1 low) and Thane hammer
(1 low). Each comes from the server using one value per class instead of one per weapon line. Two cases are Albion
and one is Midgard; Hibernia has none, because no Hibernian class has a split table on its main weapons.

### 1.2 Global combat rules that differ between eras or shards

| Rule | This server today | Classic live | Phoenix | Eden | Confidence |
|---|---|---|---|---|---|
| Spell interrupt window after being hit | 3.0 s (`spell_interrupt_duration` = 3000) | 2 s from *1.68* | Nsf | Nsf | Confirmed (CODE, CH-1.68) |
| Pet stun immunity (Cabalist Amber Simulacrum, Spiritmaster Spirit Thrall line, Druid Nature Spirit line) | 0 s (`CCSpellHandler`: "Pet Stun" gives no immunity) | 5 × stun length from *1.72* | Nsf | Nsf | Confirmed (CODE, CH-1.72) |
| Player CC immunity after mez/stun | 60 s flat (`immunity_timer_use_adaptive` = False) | 60 s | Nsf | Nsf | Confirmed (CODE) |
| Guard bonus block per Guard level | 5% | 5% | Nsf | 10% (custom) | Confirmed (CODE, EDN-W) |
| Protect range | 1,000 | 1,000 | Nsf | 2,500, and 50% stronger against pets (custom) | Confirmed (CODE, EDN-W) |
| Intercept order | before block/evade (stock) | stock | Nsf | after block/evade/miss; intercepted style fails (custom) | Confirmed (EDN-W); our order Unsure, not traced |
| Critical Shot multiplier | up to 2.0×, less against higher-level targets (`AttackAction.cs`) | 2.0× | 1.75× (Nov 2020) | Nsf | Confirmed (CODE); Phoenix Unsure (PVP) |
| Resist debuffs (casted) | classic data in the DB (not read) | 15/30/50 | cut 20%, then set back to 15/30/50 (Jan 2021) | become 8 s spec debuffs (spell change) | Phoenix Confirmed (PVP); Eden Confirmed |
| Realm ability system | Atlas "Old Frontiers" set (`realmabilities_atlasOF`); `use_new_passives_ras_scaling` and `use_new_actives_ras_scaling` = False | pre-*1.108* | classic | *1.108* 5/9-tier system, improved RR5 for every class, Stoicism 3 levels, cheaper Determination | Confirmed (CODE, EDN-W) |
| Power regen halved below 50% for list casters | off (`mana_regen_amount_halved_below_50_percent` = False, a launcher option) | on | Nsf | Nsf | Confirmed (CODE) |
| Theurgist pet cap | 0 = uncapped, limited by cast time, power and duration (1.65 behaviour) | uncapped in 1.65 | Nsf | pets −25% health; Mastery of Concentration pets get less health | Confirmed (CODE, EDN-W) |
| Concentration buff range | unlimited (`buff_range` = 0) | limited | Nsf | Nsf | Confirmed (CODE); live range Unsure |
| Maximum active pulses | 1, or 2 for Paladin, Minstrel, Skald, Bard, Warden | same | Nsf | 9 for everyone (custom) | Confirmed (CODE, EDN-W) |

**Finding 2 (Confirmed).** Two global values sit on the pre-patch side of a live change: the 3 s interrupt window
(live 2 s from 1.68) and zero pet-stun immunity (live 5× from 1.72). Both are real, even, one-switch decisions.
The pet-stun rule touches exactly one class per realm (Cabalist, Spiritmaster, Druid). The interrupt window touches
every caster, so Hibernia, the caster realm, feels it most. That is realm identity, not a reason to skip it.

## 2. Per-class comparison: today, Phoenix, Eden

One row per class, the same columns in every realm. "Today" lists only what CODE can show (constants above);
spell data is in the database and is not compared. Eden entries are shortened; PVP Table 1 has every line with its
patch tag. "Nsf" = no source found.

### Albion

| Class | Today (spec / factor) | Phoenix | Eden (short) | Gap worth noting | Confidence |
|---|---|---|---|---|---|
| Armsman | 2.0 / 22 | Nsf | Memories of War halved; Stoicism II; Tactics | None in class data | Confirmed (CODE, EDN-W) |
| Paladin | 2.0 / 19 | Factor 21; instant group heal and group damage add, 60 s reuse | Spec 2.3; tri-resist and all-magic chants; Stoicism I | Phoenix makes it a damage hybrid; Eden makes it a support hybrid | Phoenix factor Unsure, abilities Confirmed |
| Mercenary | 2.0 / 22 | Nsf | Stoicism III; Slam 9 to 5 s; Dirty Tricks timers *(1.128)* | None | Confirmed (EDN-W) |
| Reaver | 2.0 / 19 | Nsf | One instant lifedrain, 1,500 range, 20 s *(1.114)*; instant snare *(1.114)*; Charge *(1.111)* | None | Confirmed (EDN-W) |
| Cleric | 1.0 / 16 | Nsf | Cure Nearsight 25/40 *(1.114)*; Smite scales like list casters (custom) | None | Confirmed (EDN-W) |
| Friar | 1.5 / 19 | Factor 21 | Cure Nearsight; group cure disease; 70% Focus Shield | One value for crush (live 18) and staff (live 20) | Phoenix Unsure |
| Sorcerer | 1.0 / 14 | Nsf | Resist debuffs become 8 s spec debuffs; DD+snare 2.6 s | None | Confirmed (EDN-W) |
| Theurgist | 1.0 / 14 | Nsf | Pet health −25%; air pet stun range 750 | Uncapped pets (1.65) | Confirmed (CODE, EDN-W) |
| Cabalist | 1.0 / 14 | Nsf | 8 s spec resist debuffs; stat debuffs 60 s / 10 s recast | Pet stun gives no immunity (see R2) | Confirmed |
| Wizard | 1.0 / 12 | Nsf | Thornweed Field; DD+snare 50%, 2.6 s | None | Confirmed (EDN-W) |
| Necromancer | 1.0 / 14 | Nsf | No Necromancer; Occultist instead (pet 88% level) | Acts through its pet; no reference to compare | Confirmed (EDN-W) |
| Minstrel | 1.5 / 18 | Charm 6 s recast; releasing the pet cancels the charm pulse | Charm no longer breaks mez/root on expiry; +50% charm duration *(1.121)*; Determination; Speed of Sound only in a group | Called the strongest classic class for control (Unsure) | Phoenix Confirmed |
| Infiltrator | 2.5 / 18 | Nsf | DoT poisons raised *(1.104)*; Pincer 4 s stun *(1.121)*; poisons in combat | Classic "overpowered" talk (Consensus) | Consensus |
| Scout | 2.0 / 18 | Bow damage −4.5%; crit 1.75×; Stop −30% | Tracker's Alacrity; Power Shot 3.5 s / 20 s *(1.121)* | Melee factor 1 low on slash/thrust | Phoenix Unsure |

### Midgard

| Class | Today (spec / factor) | Phoenix | Eden (short) | Gap worth noting | Confidence |
|---|---|---|---|---|---|
| Warrior | 2.0 / 23 | Nsf | Testudo rework; Shield Throw; Tactics; Stoicism II | None | Confirmed (EDN-W) |
| Berserker | 2.0 / 22 | Nsf | Stoicism III; Berserk 5 min; acuity-debuff proc | None | Confirmed (EDN-W) |
| Savage | 1.5 / 20 | Nsf | Charge *(1.107)*; Stoicism II; taunt 3 s cast | 1.65 nerf is classic history (CH-1.65) | Confirmed |
| Thane | 2.0 / 18 | Non-hammer factor +1; energy-debuff proc; faster, cheaper nukes (Unsure) | Hammer table raised *(1.111)*; Chain Lightning 2 s uninterruptible *(1.125)*; Stoicism I | Hammer factor 1 low against live | Phoenix proc Confirmed, nukes Unsure |
| Skald | 1.5 / 19 | Nsf | Determination *(1.121)*; speed pulse 10 s; Speed of Sound only in a group | None | Confirmed (EDN-W) |
| Healer | 1.0 / 18 | Celerity range 1,500 | Mez-dampening self buff *(1.114)*; Cure Nearsight | None | Confirmed |
| Shaman | 1.0 / 18 | Nsf | Cure Nearsight; group cure disease; regen buffs become HoTs; faster Cave DDs | None | Confirmed (EDN-W) |
| Runemaster | 1.0 / 14 | Nsf | 8 s spec resist debuffs; Thornweed; DD+snare 50%, 2.6 s | None | Confirmed (EDN-W) |
| Spiritmaster | 1.0 / 14 | Nsf | 8 s spec resist debuffs; pet intercept 75% to 60% | Pet stun gives no immunity (see R2) | Confirmed |
| Bonedancer | 1.0 / 14 | Nsf | Body debuff 10/20/30% for 8 s; healer minion 80%; DD+snare 2.6 s | Jay chose none of the Eden changes (#35, 2026-11-17) | Confirmed |
| Hunter | 2.0 / 19 | Nsf | Tracker's Alacrity (custom); Power Shot 3.5 s *(1.121)* | Phoenix cut Ranger and Scout bows but no Hunter note was found | Unsure |
| Shadowblade | 2.2 / 18 | Nsf | DoT poisons raised *(1.104)*; Pincer 4 s stun *(1.121)* | None | Consensus |
| Warlock | 1.0 / 14 | Nsf | Chamber cooldown 3 to 8 s; chamber damage −25% | Probably not in game; skip | Confirmed (EDN-W) |

### Hibernia

| Class | Today (spec / factor) | Phoenix | Eden (short) | Gap worth noting | Confidence |
|---|---|---|---|---|---|
| Hero | 2.0 / 22 | Nsf | Combat Awareness no longer snares; Annihilation 7 s rear stun *(1.114)*; Stoicism II | None | Confirmed (EDN-W) |
| Blademaster | 2.0 / 22 | Nsf | Stoicism III; Slam 5 s; Triple Wield buffed *(1.111)* | None | Confirmed (EDN-W) |
| Champion | 2.0 / 19 | Nsf | Stat debuff range 1,500; instant acuity debuff *(1.114)* | None | Confirmed (EDN-W) |
| Valewalker | 1.5 / 21 | Used as the factor-21 reference point | Group damage add raised *(1.114)*; Blizzard Blade −15% | None | Confirmed (EDN-W) |
| Druid | 1.0 / 16 | Nsf | Cure Nearsight 3.5 s *(1.114)*; Nature DoT to baseline *(1.101)* | Pet stun gives no immunity (see R2) | Confirmed |
| Bard | 1.5 / 18 | Instant amnesia range 1,850 | Second instant DD *(1.102)*; mez-dampening buff *(1.114)*; amnesia 2,000 *(1.116)* | Dominant group healer on Eden (Consensus) | Phoenix Confirmed |
| Warden | 1.5 / 18 | Nsf | Greater heal and group heal *(1.110)*; root; Cure Nearsight | None | Confirmed (EDN-W) |
| Eldritch | 1.0 / 14 | Nsf | 8 s spec resist debuffs; Ichor *(1.121)*; DD+snare 50%, 2.6 s | None | Confirmed (EDN-W) |
| Enchanter | 1.0 / 14 | Nsf | Root *(1.110)*; energy DD *(1.114)*; Underhill Ally heals at 80% | None | Confirmed (EDN-W) |
| Mentalist | 1.0 / 14 | Nearsight and body/spirit/energy resist debuffs added | Str/Dex debuffs *(1.110)*; spec DD loses its heal *(1.114)*; uninterruptible charm | Phoenix gave Hibernia's nuker the debuffs Albion and Midgard nukers already have | Confirmed (PX-S1) |
| Animist | 1.0 / 14 | Nsf | 8 pets *(1.114)*; shroom health bonus 25%; Ichor *(1.114)* | None | Confirmed (EDN-W) |
| Nightshade | 2.2 / 18 | Nsf | DoT poisons raised *(1.104)*; Pincer 4 s stun *(1.121)* | None | Consensus |
| Ranger | 2.0 / 18 | Bow damage −9%; crit 1.75× | Tracker's Alacrity *(1.111)*; Power Shot 3.5 s *(1.121)* | Phoenix: Ranger overplayed, so the biggest archery cut | Phoenix Unsure |
| Sluaghbinder | 1.0 / 19 | custom | custom | Hybrid constants on a 1.0 spec multiplier; no outside reference | Confirmed (CODE) |

**Finding 3 (Consensus).** Phoenix's class changes follow one pattern: they raise weak hybrids (Paladin, Friar, Thane),
trim the strongest archery (Ranger more than Scout), and fill a realm's missing tool (Mentalist debuffs, Healer
celerity range). Eden makes many more changes and moves the game toward later live patches (1.101 to 1.128). Phoenix
is the closer match for a classic server, as #108 expected.

## 3. Recommendations

None of these is a change request. Each says what we would do *if* Jay chooses it, and how it lands in each realm.
The "Rule" column is what stays the same everywhere.

### Summary

| # | Recommendation | Rule (same in every realm) | Type | Suggested decision |
|---|---|---|---|---|
| R1 | Keep classic class constants as they are | Live damage table per class | none | Keep |
| R2 | Pet stun immunity 5× stun length | All pet stuns, *1.72* | Era gate | Add as an era setting; default off for Classic/SI |
| R3 | Spell interrupt window 2 s instead of 3 s | All casters, *1.68* | Era gate or server setting | Jay's call; the most faithful classic value is 2 s |
| R4 | Per-weapon-line damage factors | Live table per weapon line | Fidelity fix | Later, as one change for all classes |
| R5 | Phoenix hybrid-melee bump (factor 21) | Every "hybrid melee" class in each realm, not Albion only | Balance | Not now; revisit after group-AI work |
| R6 | Phoenix archery trim (crit 1.75×) | All three archers alike | Balance | Not now |
| R7 | Eden tank rules (Guard 10%, Protect 2,500, intercept order) | All tanks | Custom | Reference only |
| R8 | Eden spell changes | n/a | Spells | No (standing rule) |
| R9 | Review the custom Sluaghbinder separately | n/a | Custom class | File an issue if Jay wants it |
| R10 | Power regen halving below 50% stays a launcher option | All list casters | Setting | Keep as is |

### R1. Keep the classic class constants (Confirmed)

Today's spec multipliers, weapon-skill bases and HP match live classic for every Classic/SI class (Finding 1). No
class-wide constant needs to change to be faithful. Balance worries in classic-era commentary (Infiltrator, Minstrel,
Bard healing) are about abilities and play, not constants, and they are mostly bot-AI questions (see PVP doc).

### R2. Pet stun immunity, as a *1.72* era gate (Confirmed)

Live 1.72 made a pet's stun give the target an immunity five times the stun's length. Today the server gives none
(`CCSpellHandler.cs` returns 0 for "Pet Stun"), so these pets can chain-stun a target.

| Realm | Classes affected | Effect if turned on |
|---|---|---|
| Albion | Cabalist (Amber Simulacrum) | Pet stun can't chain on one target |
| Midgard | Spiritmaster (Spirit Thrall line) | Same |
| Hibernia | Druid (Nature Spirit line) | Same |

Even across realms: one class each. Fits "every era is a switch": off for Classic/SI (today's behaviour), on from 1.72.

### R3. Spell interrupt window (Confirmed)

Today 3.0 s (`spell_interrupt_duration` = 3000). Live 1.68 set it to 2 s. It is already a server property, so this
needs no code, only a value or an era default.

| Realm | Classes most affected | Effect of 2 s |
|---|---|---|
| Albion | Wizard, Sorcerer, Theurgist, Cabalist, Cleric, Friar | Casters get back to casting 1 s sooner after a hit |
| Midgard | Runemaster, Spiritmaster, Bonedancer, Healer, Shaman | Same |
| Hibernia | Eldritch, Enchanter, Mentalist, Animist, Druid | Same; Hibernia has the most casters, so it gains the most |

The rule is even; the impact follows realm identity. Bots (all three realms) cast through the same handler, so a
sim-harness run before and after would show the size of the shift.

### R4. Per-weapon-line damage factors (Confirmed gap, low priority)

The only differences from live are Friar (crush 18 / staff 20 shown as one 19), Scout slash/thrust (19 shown as 18)
and Thane hammer (19 shown as 18). The faithful fix is to let a class carry a factor per weapon line, as live does,
and fill it from the live table for **every** class at once. Bumping single classes would be uneven.

| Realm | Classes whose numbers change | Change |
|---|---|---|
| Albion | Friar, Scout | Friar crush −1 and staff +1; Scout slash/thrust +1 |
| Midgard | Thane | Hammer +1 |
| Hibernia | none | No Hibernian class has a split table on its main weapons |

This is a fidelity fix. It shifts two Albion classes and one Midgard class by one factor point, which is small.

### R5. Phoenix hybrid-melee bump, done evenly (not recommended now)

Phoenix moved Paladin and Friar to factor 21 and gave Thane +1 on non-hammer weapons (Unsure: snippets). Copying that
would favour Albion. If Jay ever wants it, the even version is one rule for each realm's armoured hybrid melee:

| Realm | Candidate classes | Today | Even rule would give |
|---|---|---|---|
| Albion | Paladin, Friar (Reaver?) | 19, 19, 19 | 21 |
| Midgard | Thane (Skald?) | 18, 19 | 21 (Thane would gain 3) |
| Hibernia | Champion (Valewalker already 21) | 19, 21 | 21 |

Recommendation: no change now. The group-fight AI is still changing, and the sim harness puts our open-field 8v8 win
rate around 20% (30-seed harness run, 2026-11-21), so class numbers would hide AI problems.

### R6. Phoenix archery trim (not recommended now)

Phoenix cut Ranger bow damage 9% and Scout 4.5%, and dropped Critical Shot from 2× to 1.75× (Nov 2020) because
Rangers were overplayed there. Our archers are gamebots and companions, not a crowd of players. If it is ever wanted,
use the even part only: the crit multiplier for all three archers (Scout, Hunter, Ranger), not per-class cuts.

| Realm | Archer | Today | Even option |
|---|---|---|---|
| Albion | Scout | crit up to 2.0× | 1.75× |
| Midgard | Hunter | crit up to 2.0× | 1.75× |
| Hibernia | Ranger | crit up to 2.0× | 1.75× |

### R7. Eden tank rules (reference only)

Guard 10% per level, Protect 2,500 range and stronger against pets, and intercept after other defences are Eden's
own, not live patches. They would affect Armsman, Warrior and Hero (and every Guard/Protect user) the same way, so
they are even, but they are not classic. Keep as ideas for the bot tank AI (#126) rather than rule changes.

### R8. Eden spell changes: no

Standing rule. Eden's resist-debuff rework (8 s spec debuffs), 2.6 s DD+snare, 60 s stat debuffs with 10 s recast,
confusion/buff normalisation and the per-class spell additions all stay reference only.

### R9. The custom Sluaghbinder (0.37b edition)

The class has hybrid constants (factor 19, 760 HP) with a list-caster spec multiplier (1.0). There is nothing to
compare it with. If Jay wants a balance pass, it should get its own issue and a sim-harness run against Hibernia's
other hybrids, separate from this research.

### R10. Power regen rule stays a launcher option

Classic halves list casters' power regen below 50%. The Living Server has it off by default and Jay can turn it on in
the launcher. It is even by rule; Hibernia would feel it most, then Albion, then Midgard. No change suggested.

## 4. Era-gate candidates

PROGRESSION-DIRECTION.md says every era is a switch. The server already has two such switches,
`use_new_passives_ras_scaling` and `use_new_actives_ras_scaling` (*1.108* realm abilities), which shows the pattern
works. Candidates, grouped by kind:

### 4.1 Live-patch rules (natural era gates: off in Classic/SI, on from their patch)

| Patch | Rule | Already a setting? | Source |
|---|---|---|---|
| *1.68* | Spell interrupt 2 s | Yes, `spell_interrupt_duration` | CH-1.68 |
| *1.72* | Pet stun immunity 5× | No | CH-1.72 |
| *1.101* | Cure Nearsight also cures silence; Druid Nature DoT to baseline | No (spell data) | EDN-W |
| *1.104* | Assassin poison rework | No (spell data) | CH-1.104, EDN-W |
| *1.107* / *1.111* | Charge for Savage, then Reaver | No | EDN-W |
| *1.108* | 5/9-tier realm abilities | Yes, the two `use_new_*_ras_scaling` switches | CH-1.108 |
| *1.109* | Dual-wield lowers the target's block/evade by 25% | No | EDN-W |
| *1.110*, *1.114*, *1.116*, *1.121*, *1.125*, *1.128* | Per-class spell and style lines (see PVP Table 1 and EDN-W) | No (spell data) | EDN-W |

The spell-data entries are "spell changes" today, so under the standing rule they wait for the era-gate system.
When it exists, each tagged line is one row in an era table, not a hand edit.

### 4.2 Eden's own systems (custom, not live)

| System | What it does | Fit here | Source |
|---|---|---|---|
| Seasons | Levels, gold and items reset; realm points carry over | Poor for single player; maybe a "new season" world reset option | EDN-prog (PVP) |
| Realm skill-point cap | Server-wide cap on counted realm-ability points, rising through a season | Good: a progression gate for gamebots and the player | EDN-RSP (PVP) |
| Realm timer | 1 h lock to one realm after RvR actions | Low value offline | EDN-RvR (PVP) |
| Realm loyalty | 0-100 per realm from frontier RP; XP/craft/siege bonuses | Possible later | EDN-RL (PVP) |
| Renegades | Opt-in fourth faction in one zone | Possible bot event later | EDN-RvR (PVP) |
| Staged class unlocks | Catacombs classes added in a later season | Good: matches era gates (Vampiir, Warlock and the other Catacombs classes would wait for their era) | EDN-prog (PVP) |

None of these is proposed now.

## 5. What this research could not check

- **Spell values.** Spells, styles and abilities are in `runtime/data/opendaoc.sqlite3.db`, which is not in the
  repo. A later pass should read Jay's shipped database (read only) and compare resist debuffs, DD+snare cast times,
  Minstrel charm recast and Bard amnesia range with Phoenix's values.
- **Phoenix originals.** The Phoenix site is offline and the Wayback copy refused automated fetching on 2026-11-23.
  Phoenix items rely on the PvP doc's earlier read. A PDF of the Wayback page would let someone confirm the factor-21
  hybrid change and the Thane nuke change, both still Unsure.
- **Intercept order** in our code was not traced.
- **Eden RvR system pages** (realm skill-point cap, loyalty, timer, renegades) need a Discord login; the PvP doc used
  the public staff posts instead.
- **Nothing here is in-game verified.** It is all tests-only desk research.

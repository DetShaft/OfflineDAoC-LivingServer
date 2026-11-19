# PvP behavior research: how DAoC RvR is really fought, to guide bot AI

Status: research only (2026-11-19). No code, data or balance changes. Written by Squire - Grokbot for
Jay's brief on #108, #126, #136, #151, #152 and #32. Tests only: nothing here was checked in game.

## How to read this

- **Labels.** Every claim carries one of three labels:
  - **Confirmed:** an official patch note, or source code/data (this repo's OpenDAoC server, or DOL source).
  - **Consensus:** several community sources agree.
  - **Unsure:** a single source, or a search-index snippet of a page that is now offline.
- **Era.** Server context is Classic + Shrouded Isles (client 1.127, see `docs/PROGRESSION-DIRECTION.md`).
  Anything from a later patch is tagged with its version, for example *(1.72)* or *(1.127D)*. Eden is 1.90-based
  with post-1.90 picks; Phoenix is a classic-era shard with its own custom changes.
- **Realm identity is not imbalance.** Hibernia leans on casters, Midgard on melee trains, and Albion runs Cleric as its
  only pure healer with a pet/interrupt-heavy mix. This doc reports those differences and does not call them defects.
- **Same rules everywhere.** Every behavior rule proposed here is written by role, not realm, so each realm gets the
  same rule applied to its own classes.
- **Scope.** The classes covered are Classic + SI. Catacombs and Darkness Rising classes (Heretic, Mauler, Valkyrie,
  Warlock, Vampiir, Bainshee) are out of scope. The custom Hibernian Sluaghbinder has no outside source.
- **Bot types.** These stay separate throughout:
  - **Players:** real people.
  - **Companions:** `/spawn` helpers and other bots led by a player.
  - **Gamebots:** autonomous world bots. A gamebot in a player's group follows the player-led rules.
- **Sources.** The main gaps are:
  - The Phoenix site domain is now for sale, so Phoenix notes come from search-index snippets of its old pages (Unsure
    unless several agree).
  - Eden's class-change wiki page (`eden-daoc.net/wiki?content=changes`) needs a Discord login, so most per-class Eden
    cells say "no source found". Jay has a PDF export of it (see #108), and reading it is the next step.
  - Camelot Herald fandom pages refuse automated fetch. Jay supplied nine as PDFs on 2026-11-19 (keys `CH-*` in the
    source list), and the claims they back are now Confirmed. The PDFs are not in the repo.

## Answers to the 24 questions

### A. Class roles and balance (#108)

**1. Group role per class.** See Table 1. In short (Consensus: Blackthorn class guides, Phoenix/Reddit 8-man threads):

| Role | Albion | Midgard | Hibernia |
|---|---|---|---|
| Healer | Cleric, Friar | Healer, Shaman | Druid, Bard |
| Crowd control | Sorcerer, Minstrel, Cleric (stun) | Healer, Skald, Shaman (root) | Bard, Druid (root), Enchanter |
| Nuker | Wizard, Cabalist, Theurgist | Runemaster, Spiritmaster, Bonedancer | Eldritch, Mentalist, Enchanter |
| Melee damage | Mercenary, Reaver, Paladin | Berserker, Savage, Thane | Blademaster, Champion, Valewalker |
| Tank/peeler | Armsman, Paladin | Warrior, Thane | Hero, Champion |
| Stealth | Infiltrator, Scout | Shadowblade, Hunter | Nightshade, Ranger |
| Pet class | Theurgist, Necromancer, Cabalist, Minstrel | Bonedancer, Spiritmaster, Hunter | Animist, Enchanter, Druid |

**2. What Phoenix and Eden say per class.** These are in Table 1.
- **Phoenix (Unsure: snippets of offline pages):**
  - Paladin and Friar moved to damage table 21. For comparison, the live table has Paladin at 19, Friar at 18
    (crush) and 20 (staff), Thane at 18 (axes/swords) and 19 (hammers), Valewalker at 21 (scythe), and Warrior at 23,
    the highest (Confirmed: CH-DT, the current wiki page, not era-tagged).
  - Paladin got an instant group heal and an instant group damage add, each on a 60 s cooldown (Season 1).
  - Thane nukes became faster and cheaper, and Thane gained an energy-debuff proc.
  - Mentalist got nearsight and resist debuffs.
  - Ranger bow damage −9%, Scout bow damage −4.5%, crit shot multiplier from 2× to 1.75×, Scout "Stop" duration −30%
    (Nov 2020).
  - Casted resist debuffs were reset to 15/30% plus realm-specific red debuffs (Feb 2021).
- **Eden:** the per-class page is login-gated. The #108 Bonedancer notes (from Jay's PDF) and a few forum posts are all
  that was read: Bard was dominant as a group healer, Minstrel pet release breaks mez, and some post-1.90 assassin and
  archer picks.

**3. Over/underpowered by era (Consensus unless noted):**
- **Classic to 1.65:**
  - Infiltrator was widely called overpowered, especially when buffed by a Minstrel (FreddysHouse "Balance issues").
  - **Savage, 1.65 (Confirmed: CH-1.65):**
    - Fixed the triple/quad hit chances: more triples, fewer quads, lower damage overall.
    - Buffs halved in duration and in health cost, but the cost became a percent of *maximum* health.
    - Irreverent Gesture went from a shout to a 2 s cast.
    - Damage with anything other than hand-to-hand weapons was lowered.
    - Savages got a single-line respec.
  - Friar was weak at the top end and got 1.5 spec points, Evade 5 and armor absorption (Unsure: the patch is unknown;
    it is *not* in the 1.65 notes).
  - Minstrel is called "the most powerful class" in classic 8v8 and solo, because of control, not damage
    (Unsure: Blackthorn, a 1.65-rules shard).
- **Phoenix (2020):** Ranger was overplayed, which led to the archery damage cut.
- **Eden:** Bard's healing made it dominate Hibernian groups (Consensus: Eden forum "Bards", r/daoc).

**4. Eden progression ideas that could become era-gate settings (Consensus: Eden forum announcements):**
- **Seasons** reset levels, gold and items, while realm points and non-economic progress carry over.
- **Realm skill-point cap:** a cap that rises during the season on how many realm-ability points can be spent.
- **Realm timer:** one hour locked out of other realms' frontiers after frontier activity, and 12 hours without siege RP.
- **Realm loyalty:** 0-100, gained from frontier RP and relic captures, lost by fighting for another realm.
- **Classes and zones by stage:** Catacombs classes were added in a later season's beta, and Catacombs PvE zones were
  not added at all.
- Each of these maps cleanly onto the "every era is a switch" rule in PROGRESSION-DIRECTION.md. None is proposed for
  now.

### B. Group composition and roles

**5. Typical 8-man groups.** Sources: Phoenix forum "8man setup" snippet, r/daoc "Phoenix typical 8v8" and "Best
8man Eden s3", Blackthorn guides. Consensus on the shape; exact slots vary.
- **Albion:**
  - Cleric ×2, Friar (or a third support), Minstrel, Sorcerer, Theurgist, Cabalist or Wizard, Armsman or Paladin.
  - Variant "necro tanker": Necromancer, 2 melee, Sorcerer, Theurgist, Minstrel, 2 supports.
  - Job of each slot: Sorcerer opens with the AE mez (1,875 range), Theurgist spams pets on enemy supports, Minstrel
    interrupts, Clerics heal and cure, and the tank peels.
- **Midgard:**
  - Hybrid: 2 tanks (Warrior plus Berserker or Savage), Healer ×2 (one Pacification spec, one Augmentation spec),
    Shaman, Runemaster, Spiritmaster or Bonedancer, Skald.
  - Melee train: Shaman, Skald, Savage ×2, Warrior, Healer ×2, plus one flex.
  - Job of each slot:
    - The Pacification Healer mezzes and stuns.
    - The Augmentation Healer heals, cures and casts celerity.
    - The Shaman buffs, roots and interrupts.
    - The Warrior peels.
    - The Skald gives speed and interrupts.
- **Hibernia:**
  - Caster group: Bard, Druid ×1-2, Warden, Eldritch, Enchanter, Mentalist, Hero or Blademaster.
  - Tanker: Bard ×2, Druid, Warden, plus 4 melee (Blademaster, Champion, Hero, Valewalker).
  - Job of each slot: Bard opens with the AE mez and uses instant amnesia, the Nature Druid roots, casters burst one
    target, and the Hero guards the casters.

**6. Who calls targets.** The **main assist** (MA) is one player whose target everyone takes ("assist train").
- It is usually a caster or tank with a good view of the fight: the Enchanter in Hibernian caster groups, a tank in
  Midgard melee trains.
- Supports and peel tanks are not on the train. They react to threats.
- Consensus (Blackthorn guides, crowsofwinter hybrid-8v8 summary, search snippets).

**7. Healers.**
- **Order of work:** keep themselves alive first (a dead Cleric drops every buff it cast), then heal whoever is being
  assisted.
- **Pre-heal:** heal a target that has just been debuffed by the enemy's main assist, before the damage lands.
- **Spread heal** when several are hurt, and cure disease and poison quickly.
- **When healthy:** they switch to offense, using a casted stun on an enemy support, interrupts or roots.
- **Two-healer split:** Midgard formalizes it as an aggressive Pacification Healer up front and an anchored
  Augmentation Healer in back. Hibernia does the same with a Nature Druid and a Nurture Druid.
- **Staying safe:** they play the midline or backline, step back when a tank dives, and call out teammates who are too
  far forward to heal.
- Consensus (Blackthorn Cleric, Healer, Druid and Bard guides).

**8. Tanks and peelers.**
- **What they do:**
  - Guard the healer or caster being trained, and swap Guard and Intercept as the enemy swaps targets.
  - Snare divers with snare styles.
  - Save the 9 s Slam stun for a real threat to the backline.
  - Kill pets that are interrupting their casters.
- **What they don't do:**
  - Chase enemy casters deep into the enemy backline.
  - Top the damage meter.
  - Waste stuns, which hands out free immunity.
- Consensus (Blackthorn Warrior, Armsman and Hero guides; crowsofwinter: "the job of a tank is NOT to kill but to
  contain").

### C. Positioning and movement

**9. Standoff distances.**
- **Ranges:**
  - Most direct-damage spells reach about 1,500. Bolts and the Sorcerer AE mez reach about 1,875 (Consensus).
  - Bow range varies by bow and level; no exact source was read in this pass.
- **Where each role stands:**
  - Casters stand near maximum range, behind their tanks, so melee must cross open ground. Anything that hits them
    interrupts a cast (see 16).
  - Healers stand midline: in range of the whole group, out of melee reach, and in line of sight.
  - Melee are on the target.
  - Peel tanks stand between the enemy melee and their own backline.
- **What the server and bots use today (Confirmed):**
  - The healer post in `bots/HealerPost.cs` keeps at least 350 from enemies (220 in tight spaces) and at most 750 from
    the group (600 in tight spaces).
  - Its code comment notes that heals reach 1,500-2,000.

**10. Gap closing and kiting.**
- **Melee close the gap with:**
  - sprint
  - the group speed song (Minstrel, Skald or Bard)
  - Speed of Sound (an instant group speed realm ability)
  - snares and roots from their own casters
- **Casters kite like this:**
  1. Control the attacker with an instant root, snare, stun or mez, or with Quickcast.
  2. Step back out of melee reach.
  3. Turn and cast again.
- **Hibernian caster groups kite as a whole group,** walking backwards while the Nature Druid roots the chasers.
- **A melee player who is being kited:**
  - stops chasing a rooted or snared target
  - swaps to a target in reach
  - waits for a purge or demezz
  - peels back to protect their own backline
- Consensus (Blackthorn Druid, Bard and Sorcerer guides; #136).

**11. Sticking together, splitting, running or committing.**
- **Moving:** groups move as one blob at song speed.
- **Splitting** is deliberate: the Skald splits off as a "mobile interrupt engine" when enemy casters are free-casting,
  and sticks to a target to kill it when one is low or overextended.
- **Commit:** when the opening control war is won (enemy supports mezzed or purges burned).
- **Disengage:** when it is lost.
- Consensus (Blackthorn Skald guide; Speed of Sound in the Minstrel guide).

**12. Terrain and line of sight.**
- Casters and healers use pillars, walls and keep corners to break the enemy's line of sight and stay out of their
  casts.
- The Minstrel's "flute mez" keeps pulsing while out of range or sight, and lands the moment the target comes back into
  view. Guides call this especially strong at keep doors.
- Unsure for the general rules: guides mention them, but no dedicated source was read. The flute mez point comes from
  the Bards of Camelot Minstrel twisting guide.

### D. Crowd control and abilities (#152)

**13. How CC is used.**
- **The opener:** an AE mez on the enemy group from max range.
  - Albion: the Sorcerer at 1,875.
  - Hibernia: the Bard (with instant amnesia to win the "mezz war").
  - Midgard: the Pacification Healer.
- **Next layer:**
  - single-target mezzes on whoever got free
  - roots on tanks that are diving
  - stuns on enemy supports to open a kill window
  - snares from melee styles
- **Who gets controlled:** enemy supports and casters first. Tanks get rooted or snared rather than mezzed.
- **What breaks it (Confirmed in `GameLiving.cs`):**
  - Mez breaks on any damage.
  - Mez also breaks on non-damaging hostile spells: nearsight, amnesia, disease, snare, stun, confusion and resist
    debuffs. On players, even a resisted hostile spell breaks it.
  - Root and snare break on damage.
  - Consensus (Blackthorn Sorcerer, Bard and Healer guides) and Confirmed for the break rules (repo source).

**14. AE mez frequency and the Minstrel.**
- **Immunity (Confirmed):**
  - Mez, root and nearsight leave a 60 s immunity when they end or break.
  - In this server: `AbstractCCSpellHandler` returns 60,000 ms, and immunity is 0 only for "Pet Stun".
  - Community source: MMOBread "State of the Game".
- **Style stuns** leave immunity equal to five times the stun *(1.72; before that it was a flat one minute)*. So do
  **pet stuns** (for example, a 1 s pet stun gives 5 s of immunity). Confirmed: CH-1.72, DOL `StyleStun.cs`.
- **Server difference to check (not a #151/#152 cause):** this server gives "Pet Stun" no immunity at all
  (`CCSpellHandler.cs`), where 1.72 says 5×.
- **What follows from that** (Consensus: MMOBread, crowsofwinter):
  - A wasted mez is costly: mezzing a group and then breaking it gives the whole group a minute of immunity.
  - Players save AE mez for the opener, or for a regroup after immunities have run out. They never recast it on mezzed
    or immune targets.
- *(1.127D, live, post-classic)* CC durations shrink for casters under level 50 (Confirmed: 1.127D patch notes). This
  is not a classic rule.
- **The real Minstrel:**
  - Its group value is the single-target flute mez (a song it can play while moving), two instant damage spells, an
    instant stun, its pet and speed (Unsure: Blackthorn Minstrel guide).
  - Its AE mez has a 5 s cast and a 30 s duration at 45 spec. A classic-era forum post calls it "greatly inferior to the
    Bard's" and says Minstrels were rarely seen using it in RvR (Unsure: FreddysHouse).
- **What the bot does today** (#152; Confirmed by reading code):
  - `BotBrain.PvpCrowdControl.cs` runs every 2 s for any bot with CC spells.
  - It tries mez before roots, and **the largest radius first**. That is the AE mez whenever it is off its timer.
  - It aims at the enemy with the most other enemies around it.
  - It skips targets that are already mezzed, but it does **not**:
    - check for mez immunity (`eEffect.MezImmunity`; the caster-survival code does check it)
    - require more than one enemy in the radius
    - check whether groupmates are already hitting those enemies (which would break the mez at once)
  - For a cast-time CC spell it stops moving to cast. The server then rejects immune targets only when the cast lands,
    so the bot can stand through a 5 s cast every few seconds. **This matches #152.**

- **Minstrel mez history in the supplied patches (Confirmed, post-classic):**
  - *(1.65)* Minstrels got new shouts at levels 43 and 50 (Discordant Howl, Cacophony). Nothing in 1.65 changes their
    mezzes.
  - *(1.108)* The Minstrel's realm-rank-5 ability Calming Notes, an area calm, now hits every enemy-realm target
    (players, pets and guards) but lasts only 20 s.
  - *(1.111)* Its radius went from 750 to 600.
  - These are later-era realm abilities, not the classic spell-line AE mez. No supplied patch covers the classic
    Minstrel AE mez spell, so the 5 s cast / 30 s figures stay Unsure.
- **Purge** removes negative effects but **leaves immunity timers in place** (Confirmed: realm ability text in CH-1.108).
  So a purged mez still leaves the target immune.

**15. Song twisting (#32).** Unsure (Bards of Camelot Minstrel twisting guide; Uthgard thread):
- **Pulses:** songs pulse about every 5 s, and each pulse's effect lasts about 10 s (longer with better instruments).
  Starting another song cancels the running one, but its last pulse keeps working.
- **Basic twist:** speed and power songs alternated about every 3 s, right after the new song "takes".
- **Heal song** needs an instrument swap. Flute mez also needs the flute in hand, which interrupts other songs.
- **Interrupts:** songs can be played while moving and are stopped only by a hit, a mez or stun, or taking the
  instrument out of hand.
- **Charm** is a chant that also cancels songs.
- **The rule bots need:** casting a song or chant that is already running turns it off. Confirmed: comment in
  `BotSongTwistPolicy.cs` and the #41 history.

**16. Interrupts and cast times.**
- **Interrupts:**
  - Once you are attacked (melee, archery or spells), you are too distracted to cast, and for a while after an
    interrupt you can't cast at all (Confirmed: CH-QC).
  - The server's interrupt window is `spell_interrupt_duration` = 3,000 ms (Confirmed: `ServerProperties.cs`).
  - **Amnesia** doesn't count as an interrupt, but it makes the caster forget the spell, so it stops the cast anyway
    (Confirmed: CH-QC).
  - *(1.72)* All concentration buffs became uninterruptible. Greater Heal (for healers specced in their healing line)
    and the group heals got shorter cast times (Confirmed: CH-1.72).
  - *(1.72, 1.111)* Several instant debuffs were fixed so they **no longer interrupt** enemy players:
    - 1.72: Enchanter Dazzling Strobe, and two other instant spells
    - 1.111: Bonedancer instant body debuff, an instant damage debuff line, Savage taunts (now instant, 15 s reuse)
    - Not every instant interrupts, so check the spell before a bot uses an instant to interrupt (Confirmed: CH-1.72,
      CH-1.111).
    - The Blackthorn Bonedancer guide says its instant body debuff "interrupts", which only holds before 1.111.
- **Ways around it:**
  - Instant spells cannot be interrupted, which is why instants are prized for "rupting".
  - **Quickcast:**
    - A base ability of the level-5 cloth casters.
    - It casts one spell faster, at **double power cost**, and melee or spells can't interrupt it.
    - Mez and stun still stop it.
    - The Necromancer's version (Facilitate Painworking) gives its pet 6 s of uninterrupted casting.
    - (Confirmed: CH-QC.) *(1.65)* Quickcast greys out on its recast timer like other abilities (Confirmed: CH-1.65).
  - **Mastery of Concentration (realm ability):**
    - 30 s of uninterrupted casting, lengthened by duration bonuses.
    - Higher-level monsters and keep/tower lords and guards can still interrupt it.
    - Each rank *raises* the effectiveness of cast-time spells (25/35/50/60/75%); instant spells keep full value
      (Confirmed: CH-MoC). This corrects the earlier "less effect per rank".
    - *(1.108)* It was reworked into a 100% bonus to avoid interruption, with the same rank percentages (Confirmed:
      CH-1.108).
    - The 10-minute reuse is still Unsure: it isn't in either supplied page.
- **Casting ends your own melee:** starting a cast ends a melee swing. #126 found bots that cancelled their own swing
  after every instant spell, which made melee hybrids stand idle; that was fixed in #135.

### E. Target selection (#151)

**17. Choosing among several enemies.**
- **Order:**
  1. The main assist's target comes first (focus fire).
  2. The best targets are enemy supports (healers) and exposed casters, especially debuffed ones.
  3. Next, anyone low or overextended.
- **Never:** split damage over several enemies, or hit an enemy a groupmate has mezzed.
- **By role:**
  - Peel tanks target whoever threatens their backline.
  - Interrupt classes target whoever is free-casting.
  - Healers target their own group.
- Consensus (crowsofwinter; Blackthorn Necromancer, Eldritch and Skald guides).

**18. Outnumbered and retreat.**
- **When outnumbered,** groups:
  - fall back to line of sight, keep walls or towers
  - use Speed of Sound or speed to reposition
  - fight only if the opening CC lands
- **Retreat triggers:**
  - the opener is lost
  - a support dies
  - power runs out
  - purges are down while the enemy still has CC up
- Unsure: no source gives numbers. Bots should treat these as tunable settings.

**19. Things that look idle but are correct.**
- standing just out of melee reach because a mezzed enemy must not be hit
- a healer holding its post while the group is healthy
- a tank holding its line instead of chasing
- casters waiting for the assist call, or for the kill window after CC lands
- a Minstrel "holding" a flute mez outside range before stepping in
- waiting out an immunity timer

The bug in #151 is different: bots standing beside a free, legal target that they are allowed to hit. Consensus for
the play patterns. The #151 split is our inference.

### F. Siege and keep play

**20. Siege roles.**
- **Attackers:**
  - ram the outer door, then the inner door
  - catapults or trebuchets on the walls and the defenders
  - archers and casters clear the walls
  - healers stay behind the ram crews
- **Defenders:**
  - hold the door with melee inside it
  - archers and casters on the walls
  - boiling oil on the ram
  - catapults on the back wall to hit both doors
  - when a door falls, fall back to the inner door and repeat
- Consensus (Celer's siege and keep-door guides at daoc.djswifty.com; Light Tanks forum, via search summary).
- Bot raids already mirror this in `CompanionRaidSiege.cs`.

**21. Doors, lords and chokepoints.**
- **The broken door is the choke.** Defenders AE and mez the gap. Attackers wait for a mez or root to break, then pour
  through in one push.
- **Lord fights:** the lord is a single strong target. One tank holds it while the healers stay behind the doorway.
- **Door passing:** players pass their own realm's doors by clicking them. Bots in a player's group are moved through
  doors per #114/#127.
- Unsure: general guides, plus repo history.

**22. Relic and frontier patrols.** No source found for how roaming groups pick fights, beyond "fight when the opener
can be won and the numbers are close" (Consensus from 11 and 18). Relic carriers are escorted and enemies try to
intercept them. That comes from repo code, not an outside source.

### G. Pets and stealth

**23. Pet classes.**
- **What players do with pets:** the pet is an interrupt and pressure tool.
  - **Theurgist:** spams pets on enemy healers. Earth pets for sustained pressure, ice pets for ranged interrupts and
    snare, air pets for stuns.
  - **Bonedancer:** interrupts with its pets and instants, then becomes the "hammer" with its lifetap once a window
    opens.
  - **Necromancer:** the pet *is* the character, and it boosts the tank train with an AF debuff.
  - **Minstrel:** its charmed pet interrupts. Releasing the pet clears the pet's own CC, and the pet then attacks the
    mezzed Minstrel, which breaks its mez.
  - **Spiritmaster, Animist and Hunter** pets follow the same "interrupt first" logic (no class guide read for them).
  - Consensus (Blackthorn Theurgist, Bonedancer, Necromancer and Minstrel guides; Eden forum t=1841).
- **Eden:** taunts and protect are 50% stronger against pets (from #108).
- **How much damage comes from the pet:** no source found. Here commanders get the full to-hit bonus and minions half
  (PR #109).

**24. Stealth classes.**
- **The opener:** Perforate Artery with poison applied, then a Critical Strike chain. Shadowblade: PA, then Creeping
  Death, then Stunning Stab.
- **After the opener:** keep styling with poisons and save endurance. Don't spend it on Garrote or Achilles Heel unless
  the kill is close.
- **Scouts and Rangers** open with bow crit shots from stealth *(Phoenix lowered the crit multiplier)*.
- Consensus (Blackthorn Shadowblade, Infiltrator and Nightshade guides via search summary; Phoenix notes).
- **Bots today:** `BotRvrAmbush.cs` stealths toward enemies within 2,000. But `BotMeleeStylePolicy.IsEligible` excludes
  every positional or stealth-opener style, so bots never use PA, back or side styles.

## Table 1: class role and balance

Source keys are in the source list at the end. "Nsf" means no source found.

### Albion

| Class | Realm | Group role | Strengths | Weaknesses | Phoenix notes | Eden notes | Confidence | Source |
|---|---|---|---|---|---|---|---|---|
| Armsman | Alb | Peel tank | Guard and Intercept, snare styles (Crippling Blow), Slam | Not a killer; must stay with the backline | Nsf | Nsf | Consensus | BT-arms |
| Paladin | Alb | Tank / chant support | Chants, durable | Damage factor 19 on the live table (CH-DT) | Damage table 21; instant group heal and group damage add, 60 s cooldown (S1) | Nsf | Unsure | PX-S1, PX-dmg |
| Mercenary | Alb | Melee damage (assist train) | Dual-wield burst, evade | Medium armor; needs peel | Nsf | Nsf | Unsure | PX-8 |
| Reaver | Alb (SI) | Melee hybrid | Instant lifedrain interrupts | The instant used to cancel the bot's own swing (#126) | Nsf | Nsf | Unsure | REPO #126 |
| Cleric | Alb | Healer (often 2) | Spec buffs (Dex/Qui, Str/Con, Acuity, spirit/body/energy resists), 9 s casted stun | Albion's only pure healer, so its death drops the group's buffs | Nsf | Nsf | Consensus | BT-cleric |
| Friar | Alb | Second support, melee | Heals plus staff melee | Live factor 18 crush / 20 staff (CH-DT); weak at the top end (era unknown) | Damage table 21 | Nsf | Unsure | FH, PX-dmg, CH-DT |
| Sorcerer | Alb | CC lead | 1,875-range AE mez, roots, demezz | Squishy; key target | Nsf | Nsf | Consensus | BT-sorc |
| Theurgist | Alb | Pet interrupter | Earth, ice and air pets on supports | Low personal damage by design | Nsf | Nsf | Consensus | BT-theurg |
| Cabalist | Alb | Nuker / debuffer | Damage-over-time, debuffs, pet | Squishy | Nsf | Nsf | Unsure | PX-8 |
| Wizard | Alb | Nuker | Burst direct damage | Squishy | Nsf | Nsf | Unsure | R-8v8 |
| Necromancer | Alb (SI) | Pet class: the pet is the body | AF debuff turns the tank train lethal; quickcast | Owner can't kite; acts only through the pet | Nsf | Nsf | Consensus | BT-necro |
| Minstrel | Alb | CC / interrupt / speed | Flute mez while moving, instant stun, 2 instant damage spells, charmed pet, Speed of Sound | Hard to play; low damage | Charm pulse and pet-release changes discussed | Pet release breaks the Minstrel's own mez (forum) | Consensus | BT-minst, EDN-1841 |
| Infiltrator | Alb | Stealth | Perforate Artery opener, poisons | Weak once seen | Nsf | Keeps post-1.90 assassin RR5s; *(1.108)* Remedy moved from RR5 to a level-50 grant; *(1.104)* poison rework (Confirmed: CH-1.104, CH-1.108) | Consensus (classic overpowered talk) | FH, EDN-1219 |
| Scout | Alb | Stealth archer | Bow crit shot, shield | Low melee | Bow damage −4.5%, crit 1.75×, Stop −30% (2020) | Shield Trip's 300 damage and 60% speed came in *(1.108)* (Confirmed: CH-1.108) | Unsure | PX-arch, EDN-1219 |

### Midgard

| Class | Realm | Group role | Strengths | Weaknesses | Phoenix notes | Eden notes | Confidence | Source |
|---|---|---|---|---|---|---|---|---|
| Warrior | Mid | Peel tank | Slam (9 s), snare styles, Guard/Intercept, top damage table (factor 23, CH-DT) | Must not waste Slam | Nsf | Nsf | Consensus | BT-war |
| Thane | Mid | Tank / hybrid caster | Instant/casted lightning, hammer | Medium damage | Faster and cheaper nukes, energy-debuff proc; weapon parity disputed | Nsf | Unsure | PX-dmg |
| Berserker | Mid | Melee damage (train) | Burst | Needs celerity and peel | Nsf | Nsf | Unsure | PX-8 |
| Savage | Mid (SI) | Melee damage (train) | Hand-to-hand burst, self buffs | Health-cost buffs; nerfed in 1.65 | Nsf | Nsf | Confirmed (1.65 nerf) | CH-1.65 |
| Healer | Mid | Pacification (CC) or Augmentation (heal, celerity) healer | Mez, stun, celerity | The Pacification Healer is exposed up front | Celerity range 1,500 (snippet) | Nsf | Consensus | BT-healer |
| Shaman | Mid | Buffer / midline support | Full spec buffs, roots, cures, disease | Not a main healer | Nsf | Nsf | Consensus | BT-sham |
| Skald | Mid | Interrupt / speed / flex melee | 2 instant damage spells, instant snare, instant mez, speed | No Determination (CC-heavy weakness) | Nsf | Nsf | Consensus | BT-skald |
| Runemaster | Mid | Nuker | Bolts, direct damage | Squishy | Nsf | Nsf | Unsure | CoW |
| Spiritmaster | Mid | Nuker / pet | Mez, pet | Squishy | Nsf | Nsf | Unsure | CoW |
| Bonedancer | Mid (SI) | Pet interrupter, then "hammer" | Instant body debuff and lifetap, pets | Pets need managing | Nsf | Body debuff 10/20/30% for 8 s, stat debuff recast 10 s, snare nuke 2.6 s, healer pet heals at 80%, pets buff only when idle (Jay chose none) | Confirmed (PDF via #108) | BT-bd, EDN-108 |
| Hunter | Mid | Stealth archer / pet | Bow, pet | Low group utility | Nsf | Entwining Snakes' 60% speed came in *(1.108)* (Confirmed: CH-1.108) | Unsure | EDN-1219 |
| Shadowblade | Mid | Stealth | PA, then Creeping Death, then Stunning Stab | Weak once seen | Nsf | Assassin RR5s kept | Consensus | BT-sb, EDN-1219 |

### Hibernia

| Class | Realm | Group role | Strengths | Weaknesses | Phoenix notes | Eden notes | Confidence | Source |
|---|---|---|---|---|---|---|---|---|
| Hero | Hib | Peel tank / babysitter | Guard on casters, Slam, Numb | Low burst until it switches to offense | Nsf | Nsf | Consensus | BT-hero |
| Champion | Hib | Tank / debuffer | Debuffs, snares | Nsf | Nsf | Nsf | Unsure | PX-8 |
| Blademaster | Hib | Melee damage | Dual-wield burst | Needs peel | Nsf | Nsf | Unsure | R-8v8 |
| Valewalker | Hib (SI) | Melee hybrid | Scythe, instants | Instants used to cancel the bot's own swing (#126) | Damage table 21 (reference point) | Nsf | Unsure | PX-dmg |
| Druid | Hib | Healer (Nature roots / Nurture heals) | Instant and AE roots, pet | Nature Druid is exposed up front | Nsf | Nsf | Consensus | BT-druid |
| Bard | Hib | Healer / CC / speed | AE mez, instant amnesia, heals, songs | Must not get caught too far forward | Amnesia range cut discussed (snippet) | Dominant group healer (forum) | Consensus | BT-bard, R-eden |
| Warden | Hib | Support | Bladeturn, songs, heals | Nsf | Nsf | Nsf | Unsure | R-8v8 |
| Eldritch | Hib | Nuker (assist train) | Nearsight, Dex/Qui debuff, disease, burst | Squishy | Nsf | Nsf | Consensus | BT-eld |
| Enchanter | Hib | Main assist / nuker / pet | Burst, often the main assist | Squishy | Nsf | Nsf | Consensus | BT-eld |
| Mentalist | Hib | Nuker "hammer" | Burst, mez | Squishy | Nearsight and resist debuffs added (S1) | Nsf | Unsure | PX-S1 |
| Animist | Hib (SI) | Shroom (turret) caster | Ground-placed shrooms | Static | Nsf | Nsf | Unsure | REPO |
| Nightshade | Hib | Stealth | PA, then Garrote chain | Weak once seen | Nsf | Assassin RR5s kept | Consensus | BT-ns |
| Ranger | Hib | Stealth archer | Bow crit shot | Low melee | Bow damage −9%, crit 1.75× (2020) | Tracker's Alacrity came in *(1.111)* (Confirmed: CH-1.111) | Unsure | PX-arch, EDN-1219 |
| Sluaghbinder | Hib (custom) | Nsf (custom class) | Nsf | Nsf | Nsf | Nsf | Nsf | REPO |

## Table 2: positioning and movement

The same rule is applied to each realm's classes by role.

### Albion

| Class | Preferred range | Moves when | Kites / peels / holds | Notes | Confidence | Source |
|---|---|---|---|---|---|---|
| Armsman, Paladin | On the threat to its backline | A diver reaches a healer or caster | Peels; holds the line | Swaps Guard with the enemy's target | Consensus | BT-arms |
| Mercenary, Reaver | Melee on the assist target | The main assist calls a target | Holds on target | Doesn't chase deep | Consensus | CoW |
| Cleric, Friar | Midline, out of melee, in range of everyone | Enemy closes, or a member drifts out of range | Holds a post; steps back | Pre-heals a debuffed teammate | Consensus | BT-cleric |
| Sorcerer | 1,875 for the opener, then 1,500 | Enemy melee in reach | Kites (root, then step back) | Wins the opener from max range | Consensus | BT-sorc |
| Theurgist, Cabalist, Wizard | ~1,500 behind the tanks | Enemy melee in reach | Kites | Pets peel for the owner | Consensus | BT-theurg, #136 |
| Necromancer | Owner at range; pet in melee | The pet follows the assist | Owner holds; doesn't kite | Owner is the weak point | Consensus | BT-necro |
| Minstrel | Front/midline; flute mez up to 1,500 | Always moving | Interrupts; speed | Holds the flute pulse out of range | Consensus | BT-minst |
| Infiltrator, Scout | Stealth; Scout at bow range | Opening from stealth | Hit and reset | Scout's crit shot | Consensus | BT-sb, PX-arch |

### Midgard

| Class | Preferred range | Moves when | Kites / peels / holds | Notes | Confidence | Source |
|---|---|---|---|---|---|---|
| Warrior, Thane | Between the enemy melee and its backline | A diver nears its support | Peels (Slam, snares) | "Who dies if I do nothing?" | Consensus | BT-war |
| Berserker, Savage | Melee on the assist target | The main assist calls a target | Holds on target | Relies on celerity | Consensus | BT-healer |
| Healer (Pacification) | Just behind the tanks | Mez/stun windows | Pushes, then pulls back | Exposed | Consensus | BT-healer |
| Healer (Augmentation) | Backline | Enemy closes | Holds | Calls out overextended tanks | Consensus | BT-healer |
| Shaman | Midline, flexible | Its role changes | Holds/floats | Rebuffs after deaths | Consensus | BT-sham |
| Skald | Front/midline | Splitting off or committing | Interrupts | Splits when casters are free | Consensus | BT-skald |
| Runemaster, Spiritmaster, Bonedancer | ~1,500 behind the tanks | Enemy melee in reach | Kites; Bonedancer interrupts while moving | Bonedancer finishes kills | Consensus | BT-bd |
| Shadowblade, Hunter | Stealth; Hunter at bow range | Opening | Hit and reset | | Consensus | BT-sb |

### Hibernia

| Class | Preferred range | Moves when | Kites / peels / holds | Notes | Confidence | Source |
|---|---|---|---|---|---|---|
| Hero, Champion | On the casters (guard) | A diver reaches the casters | Peels | Babysits Enchanter/Eldritch | Consensus | BT-hero |
| Blademaster, Valewalker | Melee on the assist target | The main assist calls a target | Holds | | Unsure | R-8v8 |
| Druid (Nature) | Front, root range | Root angles | Roots, then the group kites back | Creates separation | Consensus | BT-druid |
| Druid (Nurture), Warden | Backline / midline | Enemy closes | Holds | | Consensus | BT-druid |
| Bard | Front/midline | Amnesia/interrupt windows | Interrupts; must keep up with a group that kites back | Watches both backlines | Consensus | BT-bard |
| Eldritch, Enchanter, Mentalist | ~1,500, kiting backwards as a group | Melee train incoming | Kites (disease/snare) | Assist train | Consensus | BT-eld |
| Animist | ~1,000 (shrooms) | Rarely | Holds | Bot code uses a 1,000 shroom standoff | Unsure | REPO |
| Nightshade, Ranger | Stealth; Ranger at bow range | Opening | Hit and reset | | Consensus | BT-ns, PX-arch |

## Table 3: crowd control and abilities

Immunity rules for every row (Confirmed, see question 14):
- Mez, root and nearsight leave 60 s of immunity.
- Style stuns and pet stuns leave 5× the stun duration *(1.72+)*.
- Purge leaves immunity timers in place.
- Casted stuns leave 60 s in this server's code.

### Albion

| Class | CC / key abilities | Typical use | Recast / immunity rules | Bot risk (loops, spam) | Confidence | Source |
|---|---|---|---|---|---|---|
| Sorcerer | AE mez (1,875), single mez, root | Opener on the enemy group; re-mez the loose ones | Never on mezzed or immune targets | AE mez chosen every 2 s (largest radius first) | Consensus / Confirmed (code) | BT-sorc, REPO |
| Minstrel | Flute mez (song), AE mez (5 s cast), instant stun, instant damage spells, pet | Flute mez on one high-priority target; instants to interrupt | AE mez rarely used in RvR | **#152: AE mez retried every 2 s, no immunity or enemy-count check** | Unsure (AE use) / Confirmed (code) | BT-minst, FH, REPO |
| Cleric | Casted stun (9 s) | On an enemy support during downtime | 60 s immunity | | Consensus | BT-cleric |
| Armsman, Paladin | Slam (9 s style stun), snare styles, Guard/Intercept | Slam on a diver | 5× immunity | Bots don't use Guard/Intercept/Protect | Consensus / Confirmed (code) | BT-arms, REPO |
| Theurgist | Air pets (stun procs), ice pets (snare) | Peel and interrupt | | | Consensus | BT-theurg |
| Necromancer | AF debuff, quickcast | Debuff on the kill target | Keep it rolling | | Consensus | BT-necro |
| Infiltrator, Scout | PA opener; Scout's Stop (shield stun) | Opener | 5× immunity | Stealth openers never used (filtered) | Consensus / Confirmed | REPO |

### Midgard

| Class | CC / key abilities | Typical use | Recast / immunity rules | Bot risk (loops, spam) | Confidence | Source |
|---|---|---|---|---|---|---|
| Healer | AE mez, single mez, casted stun | Pacification Healer opens; stuns a support for a kill window | "Better not to spam mez" | Same 2 s AE-first loop as the Minstrel | Consensus / Confirmed | CoW, BT-healer, REPO |
| Shaman | Roots, disease | Root casters, then pull back | 60 s immunity | Shaman PvE root path is separate (`ShamanPveRoot`) | Consensus | BT-sham |
| Skald | Instant mez, instant snare, 2 instant damage spells | Interrupt chain | | Instants used to cancel the bot's swing (fixed in #135) | Consensus | BT-skald |
| Warrior, Thane | Slam, snare styles | Peel | 5× immunity | No Guard/Intercept | Consensus | BT-war |
| Spiritmaster | Mez | Opener support | 60 s immunity | Same AE-first loop | Unsure | CoW |
| Bonedancer | Instant body debuff (interrupts before 1.111 only, CH-1.111), lifetap | Interrupt, then burst | Eden: 8 s debuff, 10 s recast | | Confirmed (Eden PDF data) | EDN-108 |
| Shadowblade | PA, then Creeping Death, then Stunning Stab | Opener | 5× immunity | Not used by bots | Consensus | BT-sb |

### Hibernia

| Class | CC / key abilities | Typical use | Recast / immunity rules | Bot risk (loops, spam) | Confidence | Source |
|---|---|---|---|---|---|---|
| Bard | AE mez, single mez, instant amnesia | Wins the "mezz war" opener | Never recast on mezzed or immune targets | Same AE-first loop; PvE mez kept out of the attack rotation (`BardBotCrowdControlPolicy`) | Consensus / Confirmed | BT-bard, REPO |
| Druid | Instant root, AE root, casted root | Root enemy supports, then kite away | 60 s immunity | | Consensus | BT-druid |
| Eldritch | Nearsight, Dex/Qui debuff, disease, snare | Nearsight on supports before the assist call | 60 s nearsight immunity | | Consensus | BT-eld |
| Enchanter, Mentalist | Mez, pets | Support CC | 60 s immunity | Same AE-first loop | Unsure | R-8v8 |
| Hero, Champion | Slam, Numb, snares, Guard | Peel | 5× immunity | No Guard/Intercept | Consensus | BT-hero |
| Warden | Bladeturn, songs | Support | | Chant anchor only (#32) | Confirmed (code) | REPO #32 |
| Nightshade, Ranger | PA, then Garrote; bow crit | Opener | | Not used by bots | Consensus | BT-ns |

## Table 4: target priority by role

The same rule applies in every realm; only the class names change.

### Albion

| Role | Priority order | Assist rules | Retreat triggers | Source |
|---|---|---|---|---|
| Healer (Cleric, Friar) | Self-safety → the teammate being assisted (pre-heal) → spread heal and cure → offensive stun when the group is healthy | Not on the train | Enemy tank on it; group out of range | BT-cleric |
| CC (Sorcerer, Minstrel) | Enemy supports → casters → divers (root) | Never hit or mez the main assist's target | Opener lost | BT-sorc, BT-minst |
| Nuker (Wizard, Cabalist, Theurgist, Necromancer) | The main assist's target; supports or debuffed targets | Strict assist | Melee in reach (kite) | CoW, BT-necro |
| Melee (Mercenary, Reaver, Paladin) | The main assist's target → overextended or low enemies | Strict assist; don't chase deep | Train gets peeled | CoW |
| Peel (Armsman) | Whoever is diving its healers or casters → pets interrupting them | Not on the train | Never leaves the backline | BT-arms |

### Midgard

| Role | Priority order | Assist rules | Retreat triggers | Source |
|---|---|---|---|---|
| Healer (Augmentation Healer, Shaman) | Self → the focused teammate → spread heal and cure → celerity | Not on the train | Enemy close; tanks too far forward | BT-healer |
| CC (Pacification Healer, Skald) | Enemy supports and the Sorcerer → free-casting casters | Skald joins the train only when a target is low | Pacification Healer pulls back after CC | BT-healer, BT-skald |
| Nuker (Runemaster, Spiritmaster, Bonedancer) | Main assist's target; interrupt healers until a window opens | Strict assist | Melee in reach | BT-bd |
| Melee (Berserker, Savage, Thane) | Main assist's target | Strict assist | Train gets peeled | BT-war |
| Peel (Warrior) | "Who dies if I do nothing?" | Not on the train | | BT-war |

### Hibernia

| Role | Priority order | Assist rules | Retreat triggers | Source |
|---|---|---|---|---|
| Healer (Nurture Druid, Warden, Bard) | Self → focused teammate → cures | Not on the train | Group kiting back (keep up) | BT-druid, BT-bard |
| CC (Bard, Nature Druid) | Opener AE mez → amnesia on whoever is casting → roots on chasers | Never hit mezzed enemies | Opener lost | BT-bard, BT-druid |
| Nuker (Eldritch, Enchanter, Mentalist) | Main assist's target (Enchanter is often the MA); nearsight supports first | Strict assist | Melee train incoming (kite back) | BT-eld |
| Melee (Blademaster, Champion, Valewalker) | Main assist's target | Strict assist | | R-8v8 |
| Peel (Hero) | Whoever dives the casters | Guards a caster | | BT-hero |

## Table 5: bot vs player gap

This was read only, from `main` on 2026-11-19. Bot code applies each rule by class role, so every gap applies to all
three realms unless noted. The bot types are:
- **All:** every bot.
- **Player-led:** companions, and gamebots in a player's group.
- **Autonomous:** gamebots that no player leads.

| Behavior | What players do | What bots do today | Gap | Related issue |
|---|---|---|---|---|
| AE mez use | Opener, or after immunities expire; never on mezzed or immune targets; only on a cluster | All: `BotBrain.PvpCrowdControl.cs`, every 2 s, mez before root, largest radius first; skips mezzed targets only; no `MezImmunity` check, no minimum enemy count, no check that allies are already hitting the targets; stops moving to cast | Retries AE mez on immune targets and stands through cast times | #152, #151 |
| CC reservation | A mezzed target is left alone; everything else is fair game | All: an AE mez attempt reserves every enemy in its radius for cast time + 2 s (`BotPvpCrowdControl.Reserve`). `NormalServerRules.IsAllowedToAttack` then **forbids other same-realm bots from attacking** those enemies (exception for player-led groups: the leader's target and enemies that recently hit the group) | **Hypothesis for #151:** a looping AE mez attempt can keep a whole enemy cluster "reserved", so other bots stand idle. Doesn't explain the keep lord case, since lords aren't player-shaped | #151, #152 |
| Main assist | Everyone takes the main assist's target | Player-led: the leader's target (`CompanionPvpEngagement.Observe`, `PlayerLedPullCoordinator.FindLeaderTarget`), within 2,000. Otherwise the enemy nearest the leader that hit the group. Autonomous: threat-based aggro list (`CalculateNextAttackTarget`) | Autonomous groups have no main assist; no preference for enemy healers or casters | #151, #126 |
| Target priority | Supports and exposed casters first; never split damage | Highest threat (PvE-style), or nearest | No role-aware priority | #151 |
| Healer behavior | Self-safety, pre-heal the focused teammate, offense when healthy | All: members under 65% need healing (under 40% counts as an emergency), pure healers also heal under 80%, lowest health first (`BotBrain.cs` ~5530); post 350-750 (`HealerPost.cs`); idle-combat assist when the group is healthy | Close to player play; no pre-heal on enemy debuff | #126, #48 |
| Peel / guard | Guard and Intercept the trained ally; snare and Slam divers | Melee and shield bots peel for healers and then casters (#136, `BotCasterSurvival`); no active Guard/Intercept/Protect use found (only reads existing Protect effects) | No Guard/Intercept | #136 |
| Caster kiting | Control, step back, cast | All, vs player-shaped enemies: `BotCasterSurvival.cs`, 550-unit legs, at most 3 per 20 s, quickcast control | Good match; Necromancer correctly doesn't kite | #136 |
| Reaction delay | People take a moment to notice | Autonomous only: 300-2,200 ms by hit type (`BotIncomingFireReaction.cs`) | Fine | #126 |
| Instants vs swing | An instant doesn't stop the swing | Fixed in #135 (`MinstrelBotCombatPolicy`, hybrid melee) | Resolved (tests only) | #126 |
| Song twisting | Anchor plus timed swaps; never recast a running song | `BotSongTwistPolicy.Choose`: anchor plus refresh on expiry; Paladin/Warden hold one chant | Fixed level-free rule (see #32 tiers) | #32 |
| Positional and stealth styles | PA openers; side and back snares | `BotMeleeStylePolicy.IsEligible` excludes positional and stealth styles | No openers, no back or side snares | — |
| Retreat / outnumbered | Fall back to line of sight or the keep; speed away | No group retreat logic found (searched for retreat/flee). Casters kite; wipe call "run!" is chat only (`AutonomousBotCombatCalls`) | Missing | #151 |
| Siege roles | Ram, artillery, wall clearing, door hold, lord push | Raids: `CompanionRaidSiege.cs` squads; autonomous `AutonomousSiege*` | Broadly matches | #114, #127 |
| Door passing | Click your own door; walk through rammed doors | Player-led: `PlayerBotKeepDoorPassage.cs` (3 s stall, 900 radius) | Review on #129 raised false triggers | #114, #127 |

## Ten findings for bot design

1. **Immunity rules come first.** Mez, root and nearsight give 60 s of immunity, and style stuns give 5× their duration
   *(1.72+; pet stuns too)* (Confirmed). Purge leaves the immunity in place (CH-1.108). A bot must never cast a CC on
   a target that is already CC'd or immune. Today's PvP CC loop
   doesn't check immunity (#152).
2. **AE mez is an opener, not a rotation.** Use it on a cluster of 2-3 or more free, non-immune enemies that groupmates
   aren't hitting, then hold it about 60 s. The Minstrel's real tool is the single-target flute mez. Its AE mez (5 s
   cast) was rarely used in RvR (Unsure), so a Minstrel bot should rank it last.
3. **CC reservations must not freeze the group.** An AE mez attempt makes nearby enemies unattackable to other bots
   until the claim expires. If CC fails or loops, the group idles. Check this first for #151.
4. **One main assist per group.** The player's target in player-led groups, as today. An autonomous group needs a
   chosen main assist too. Supports and peelers stay off the train.
5. **Role-aware priority:** enemy healers and supports, then exposed or debuffed casters, then low or overextended
   enemies, then the nearest. Never split damage, and never hit a mezzed enemy while a free one is near (the current
   rule matches that).
6. **Some "idle" is correct.** A healer on its post, a tank holding its line, casters waiting for a window, waiting out
   immunity. #151 is bots beside a *legal, free* target.
7. **Peel means Guard/Intercept plus snares.** Peel tanks should guard the teammate being trained and swap Guard as the
   enemy swaps targets. Bots have no Guard or Intercept use yet.
8. **Casters kite, healers hold posts.** Today's #136 and #48 rules match player practice. Keep the Necromancer
   exception (it doesn't kite).
9. **Songs:** never recast a running song or chant. Twist with timed swaps after a song "takes", and keep CC songs out
   of the twist loop (#32).
10. **Retreat needs a rule.** Players disengage when the opener is lost, a support dies or power runs out. Bots have no
    group retreat. Make the triggers settings.

## Open questions and unsure claims

- **Eden per-class changes:** not read (login wall). Next step: Jay's PDF export of `eden-daoc.net/wiki?content=changes`.
- **Phoenix:** its site is offline, so Phoenix notes rest on search snippets. Several (celerity range 1,500, Bard
  amnesia range cut, Minstrel charm change) are Unsure.
- **Minstrel AE mez:** the exact spell in our data and whether it's point-blank (#152 says so) were not checked against
  the spell table in this pass. The 5 s cast / 30 s duration figures are from one classic-era forum post.
- **Pet stun immunity:** this server gives none, where 1.72 gives 5×. That's a possible data/rules difference to
  look at later, not a #151/#152 cause.
- **Instant interrupts:** the patches remove interrupts from several instant debuffs and taunts (1.72, 1.111). Which
  instants interrupt in *this* server's classic data hasn't been checked.
- **Eden immunity reports:** a player reported being re-mezzed before 60 s on Eden (forum). That's an implementation
  question there, not a rule change.
- **#151 cause:** the CC-reservation idea is a hypothesis from reading code, not a confirmed cause. It doesn't cover the
  Jarl case (keep lords aren't player-shaped), so that needs its own look.
- **No numbers for retreat thresholds, bow ranges or pet damage share.** Treat them as settings.
- **Sluaghbinder:** custom, no outside source.
- **Questions marked "no source found":** Q22 (how roaming groups pick fights), Q23 pet damage share, exact bow ranges
  in Q9, numeric retreat triggers in Q18, and per-class Eden notes in Q2. The rest are answered at the labeled
  confidence.

## Camelot Herald (fandom) pages

Jay supplied these nine as PDFs on 2026-11-19 (see `CH-*` in the source list). Still wanted:
- The early patch-notes pages from `Category:Patch_Notes`. The category page lists only titles (versions 1.11 to
  1.113), so it can't show which early patch set the 60 s mez/root immunity or the classic Minstrel AE mez values.
- Candidates are the Shrouded Isles-era notes 1.50-1.64B and 1.66-1.70, plus the pre-SI class passes before them.
  Which ones matter can't be told from the titles.

## Sources (all accessed 2026-11-19)

| Key | Source |
|---|---|
| REPO | This repo, `main`: `source/server/GameServer/bots/*` (files named above), `spells/CCSpellHandler.cs`, `gameobjects/GameLiving.cs`, `serverrules/NormalServerRules.cs`, `serverproperty/ServerProperties.cs`; issues #32, #48, #108, #114, #126, #127, #136, #151, #152 |
| EDN-108 | Eden Bonedancer changes, from Jay's PDF as recorded on #108: https://github.com/DetShaft/OfflineDAoC-LivingServer/issues/108 |
| EDN-1219 | Eden forum, "Changes to 1.90 patch level": https://eden-daoc.net/viewtopic.php?t=1219 |
| EDN-1428 | Eden forum, "CC immunity": https://eden-daoc.net/viewtopic.php?t=1428 |
| EDN-1841 | Eden forum, "Full CC immunity / Breaking your own CC and Pet pulse charm": https://eden-daoc.net/viewtopic.php?t=1841 |
| EDN-prog | Eden forum: "Seasons!" https://eden-daoc.net/viewtopic.php?t=68 ; "Realm Loyalty" https://eden-daoc.net/viewtopic.php?t=69 ; "New Classes" https://eden-daoc.net/viewtopic.php?t=5246 |
| R-eden | r/daoc "Direction of class/realm balance on Eden" https://www.reddit.com/r/daoc/comments/1n0o0ge/ ; Eden forum "Bards" https://eden-daoc.net/viewtopic.php?start=10&t=3044 ; r/daoc "Best 8man setup for Eden s3" https://www.reddit.com/r/daoc/comments/1ken356/ |
| PX-S1 | Phoenix Season 1 patch notes (domain now offline; search snippet): https://playphoenix.online/patch-notes/season-1 |
| PX-arch | Phoenix "Archery Adjustments, AoM/PD and Champions" (Nov 2020; snippet): https://playphoenix.online/forum/get-involved/planned-changes/archery-adjustments-aompd-and-champions-01EP1Y432RWZT29C2JBRRRFCWJ |
| PX-dmg | Phoenix "Class Melee Damage Table" thread (snippet): https://playphoenix.online/forum/get-involved/ask-the-team/3628-phoenix-class-melee-damage-table |
| PX-res | r/daoc "Phoenix latest patch notes 2nd Feb 2021": https://www.reddit.com/r/daoc/comments/lbo1s5/ |
| PX-8 | Phoenix "8man setup" (snippet): https://playphoenix.online/forum/realm/albion/3501-8man-setup ; r/daoc "Phoenix typical 8v8": https://www.reddit.com/r/daoc/comments/i807o4/ |
| R-8v8 | r/daoc "Phoenix: style each faction has in RvR": https://www.reddit.com/r/daoc/comments/avfpyt/ |
| CoW | Crows of Winter, "8v8 Strategies for Hybrid Setup" (page now offline; search snippet): https://crowsofwinter.gamerlaunch.com/forums/viewtopic.php?t=11087693 |
| BT-* | Blackthorn DAoC wiki class guides (a classic-rules freeshard): https://blackthorn-daoc.com/wiki/<class>-guide. Read: minstrel, cleric, sorcerer, theurgist, necromancer, armsman, warrior, healer, shaman, skald, bonedancer, bard, druid, eldritch, hero, shadowblade; nightshade and infiltrator via search summary |
| MMB | MMOBread, "State of the Game: Dark Age of Camelot" (immunity timers): https://www.mmobread.com/daoc/news/state_of_the_game_dark_age_of_camelot.html |
| UTH | Uthgard forum, "question on stun styles" (quotes the 1.72 patch text): https://uthgard.net/forum/viewtopic.php?f=15&t=20057 |
| DOL | DOL `StyleStun.cs`: http://svn.code.sf.net/p/dolserver/code/DOLSharp/trunk/GameServer/spells/StyleStun.cs |
| 127D | Live 1.127D patch notes (CC scaling under level 50): https://www.darkageofcamelot.com/1127d-live-server-patch-notes/ |
| FH | FreddysHouse: "Balance issues in DAoC" https://forums.freddyshouse.com/threads/balance-issues-in-daoc-long.7497/ ; "Ways of killing overpowered hibs" (Minstrel AE mez) https://forums.freddyshouse.com/threads/ways-of-killing-overpowered-hibs.116421/page-2 ; FH-sav: "1.65H Some savage nerf" https://forums.freddyshouse.com/threads/1-65h-some-savage-nerf.149886/page-4 |
| BoC | Bards of Camelot, "Minstrel basic Twisting guide": https://www.tapatalk.com/groups/bardsofcamelot/minstrel-basic-twisting-guide-t741.html |
| DJ | Celer's siege and keep-door guides: http://daoc.djswifty.com/quick_siege.php , http://daoc.djswifty.com/keep_door_FAQ.php |
| CH-1.65 | Patch Notes: Version 1.65. PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.65 |
| CH-1.72 | Patch Notes: Version 1.72. PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.72 |
| CH-1.104 | Patch Notes: Version 1.104 (test notes, June 2010; assassin poison rework). PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.104 |
| CH-1.108 | Patch Notes: Version 1.108 (class balance; realm ability rework). PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.108 |
| CH-1.111 | Patch Notes: Version 1.111. PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Patch_Notes:_Version_1.111 |
| CH-QC | Quick Cast. PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Quick_Cast |
| CH-MoC | Mastery of Concentration. PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Mastery_of_Concentration |
| CH-DT | Damage Table (current wiki page, live values). PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Damage_Table |
| CH-cat | Category:Patch Notes (list of version pages only). PDF supplied by Jay, 2026-11-19. https://camelotherald.fandom.com/wiki/Category:Patch_Notes |
| CH | Charplan history (1.90-1.114 class change list): https://daoctb.sourceforge.net/Charplan_History.htm |

Unsure note: where a page is offline or login-gated, the summary comes from a search-index snippet and is labeled so.

— Squire - Grokbot

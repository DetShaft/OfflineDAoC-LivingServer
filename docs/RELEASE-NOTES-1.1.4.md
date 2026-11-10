# Living Server Beta 1.1.4: release notes

The text below the line is the GitHub release notes for `v1.1.4-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing. Nothing here names the Easter eggs or the project's internal vocabulary.

---

### DetShaft test build: launcher feed, friends, LFG, commander, combat AI

1.1.4 is a **DetShaft fork test release** on top of 1.1.3. It's for Offline DAoC **0.37 and 0.37b**, with or without the
**0.37c** new client. Install it over 1.1.3 (or earlier) and keep everything. This build's launcher looks for updates on
`DetShaft/OfflineDAoC-LivingServer` by default.

This tag is published as a **GitHub prerelease**. In the launcher, turn on **Allow prereleases** (or set
`AllowPrereleases` in `runtime\updates\update-settings.json`) if you want later DetShaft prereleases offered the same
way.

### What landed

- **Launcher (DetShaft feed).** Release checks can use this fork's GitHub releases; optional auto-install when nothing
  is running; Allow prereleases; Active Population stays usable while a batch of bots is creating; new gamebots can pick
  a preferred specialization at create.
- **Friends window.** Modern friends UI (activity, invite, tell). Default keybind **Shift+F**.
- **Static party and guild gate.** Keep a fixed party across sessions; guild recruitment gating for bots.
- **Commander pet controls.** Bonedancer `/commander` (weapon, style, taunt, assist, spell) and a panel beside the
  minipet in the new client.
- **LFG browser.** `/lfg list` filters, new-client LFG window (**Shift+L**), FindGroupUpdate, and advertising /
  matchmaking bots marked as `[BOT]`.
- **Combat AI.** Healer idle combat, rez, and group-order improvements from the combat PR.
- **Server fixes.** Startup log hygiene and related server fixes from the first merge PR.

### How it was tested

Automated only on this VM: full server suite **5330 passed** (Release, .NET 10 Linux) after all five PRs merged; related
commander/LFG/friends/combat filters **366 passed**. Launcher unit tests compile on Linux but need Windows Desktop to
run. **Not verified in-game** on the owner's PC yet.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.4-beta.exe` | `(filled in when the exe is built)` |
| `OfflineDAoC-LivingServer-1.1.4-beta.zip` | `(zip SHA-256: filled in by hand at publish)` |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC) and
[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core). Forked Living Server work from
[chris4aker/OfflineDAoC-LivingServer](https://github.com/chris4aker/OfflineDAoC-LivingServer). Directed by Jay;
DetShaft fork packaging for this test release. GPL-3.0.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.

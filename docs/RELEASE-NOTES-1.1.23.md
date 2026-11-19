# Living Server Beta 1.1.23: release notes

The text below the line is the GitHub release notes for `v1.1.23-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Bots fight better, and group bots finally attack keep lords

1.1.23 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.23`.

**Keep lords.** A keep lord stayed shielded from bots while any wall or tower door was still standing, so your group
stood around Jarl Killaloe and only pets fought. Only the gate and the keep door count now.

**Crowd control.** Casters stop spamming area mezzes: no mezzing enemies that are immune or already being hit, and a
pause after each try. A failed mez no longer keeps groupmates from attacking.

**Siege engineer.** The engineer from the Siegemaster follows at realm speed and walks to the door before building a ram.

**New client.** You can't walk while stunned, mezzed or rooted. The loading screen waits for the server's NPCs. `/bug`
opens in your own browser.

**Guild bots and Killaloe.** Gamebots in your guild answer a group invite from far away. The nine stray Killaloe
posterns are gone.

All of this is from unit tests and headless client checks. A feature counts as working in game only after it shows up in
a log from Jay's PC. The full list is in `CHANGELOG-LIVING-SERVER.md`.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.23-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.23-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.

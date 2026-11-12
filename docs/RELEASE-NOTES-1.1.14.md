# Living Server Beta 1.1.14: release notes

The text below the line is the GitHub release notes for `v1.1.14-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Friends come to you, helpers level with you, ADD TARGET, /reply

1.1.14 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.14`.

**Friends come to you.** Inviting a bot friend (one on your /friend list) that is far away no longer tells you to
meet up first. The friend sends you an "omw" tell and joins your group on arrival: 30 seconds from your own zone, 30
more for each zone between you, at most five minutes. Everything is checked again when it arrives; a friend that got a
duty, died or logged off in the meantime doesn't come, and you are told why. A friend in a group of its own turns down
the first invite and asks you to try again in about a minute; the next invite after that is accepted. A bot that isn't
your friend still has to be met first.

**/spawn helpers level up with you.** A /spawn helper in your group that is below your level jumps to your level on its
next idle moment: stats, spec points, spells, styles, a new kit for that level (never worse than what it wore) and its
pet resummoned. You see "<name> is now level N, matching you." It only goes up, and autonomous gamebots are unchanged.

**ADD TARGET and befriended helpers.** The Friends window has an ADD TARGET button, and `/friend target` does the same
from chat: it adds your target if it is a player or a gamebot of your realm. Each refusal says why. ADD TARGET on your
own /spawn helper makes it a friend and a real gamebot: when it leaves you, it logs in as a gamebot where it stood, with
its name, class, level, specs and gear. Keeping its gear is an exception, for befriended helpers only, to gamebots
starting without armor.

**/reply.** `/reply` and `/r` answer the last person who sent you a private message, player or gamebot. In the new
client, clicking a tell line in chat opens the typing line with `/send <name> `.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.14-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.14-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.

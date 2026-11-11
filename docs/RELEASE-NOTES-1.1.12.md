# Living Server Beta 1.1.12: release notes

The text below the line is the GitHub release notes for `v1.1.12-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing.

---

### Commander talk, pet assist, Friends and LFG, Snarls

1.1.12 is a **DetShaft fork prerelease**. Same Offline DAoC **0.37 / 0.37b / 0.37c** base. The 1.1.9 class-cache
repair is unchanged: INSTALL/Setup still restore `global_script_class_cache.cfg` and never delete it.
The banner reads `LivingServer client patch 1.1.12`.

**Commander.** Right-click your commander to talk to it. A left click only targets it, and the ORDERS button
beside the pet window is gone. The dialog reads as one exchange: the commander's answer to your last order is
on top in its own colour, and the orders under it show what it does now (the weapon it holds, the style it
favors, whether its minions assist and whether it taunts) instead of the whole block repeating under every
click. Returned and Decayed commanders, which have no minions, no longer offer minion orders.

Every weapon a commander can hold now has styles. It learns the Midgard sword, axe and hammer styles up to its
own level, on top of its template's styles; a hammer had none before. Only styles that open on their own or from
a position can be picked as a favorite. Chain and defensive styles stay in its own rotation.

**Pet assist mode.** Every pet with a pet window can assist you, the way your party bots do. It attacks your
target as soon as you swing at it, shoot it or start a harmful spell on it, and follows you onto your next
target. Pick ASSIST on the new client's pet window (beside RELEASE; it can be dragged onto a quickbar), or type
`/petassist` on the classic client (`/petassist off` ends it). Assist fights like Defensive otherwise. Picking
Aggressive, Defensive or Passive turns it off, and it stays on for the next pet you summon until you do. It
applies to your main pet. A Bonedancer commander's minions follow the commander's target through its own minion
assist order, and bot pets are unchanged.

**Friends and Looking For Group.** Both windows fill their frame (a dark backing under the stone frame closes the
gap that showed the world through the top edge), and their column headers sort: click one to sort by it, again to
reverse it. Looking For Group shows full class names and real zone names instead of four-letter class codes and
zone numbers, drops the LFG/LFM column, and its CLASS / LEVEL / ZONE filters are boxed fields above the headers.
Friends that do not fit scroll.

**Snarls.** The named wolf in East Svealand can be targeted once it shows itself. It hides until three of its
wolves die; when it came out, the classic client kept it untargetable and nameless while it attacked. Any change
to an NPC's can't-target, don't-show-name, peace or ghost flag now removes the NPC from every client and creates
it again, so the client takes the new flags. This also covers Stripe, the other mobs that hide until their adds
die, and the day/night spawns. The new client rebuilds an NPC whose targeting flags change.

These notes are from unit tests and headless client checks. A feature counts as working in game only after it
shows up in a log from Jay's PC.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.12-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.12-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC). DetShaft fork packaging. GPL-3.0.

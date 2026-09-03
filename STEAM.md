# Steam Workshop description

Paste the text below into the Workshop item's description field when publishing
(the BBCode renders on Steam, but not in-game - `About/About.xml` carries its own
plain-text description).

```
[h1]Better Spells[/h1]
Autocast your click-and-forget spells, and never miss a ready cooldown again. Everything is optional - nothing runs until you turn it on.

[hr]
[h2]What it does[/h2]

[b]Autocasting[/b]
[list]
[_]Eligible spells get an extra toggle button right next to their cast button - just like the deathrest auto-wake button. It stays visible in both on and off states.
[_]While on, the spell is queued automatically the moment its cooldown finishes - as an ordinary queued job, so sleep, current work and your own orders are never interrupted.
[_]Only click-and-forget spells qualify: self-buffs and auras that need no target. Targeted spells, world-targeted spells, ritual starters and confirmation-dialog casts are never autocast.
[_]The toggle is shared by all pawns: selecting several shows a single merged button, never one per pawn.
[*]Autocast attempts pause during mental breaks and resume when the pawn recovers.
[/list]

[b]Cooldown-ready letters[/b]
[list]
[_]When an ability's cooldown completes, a dismissable letter arrives - same system as raid, orbital trader or masterwork notifications, with a per-pawn breakdown. Click it to jump to the pawns.
[_]Letter style is configurable: neutral (trader-like), positive (masterwork-like) or raid-like (red, threat sound) - much harder to miss than the small default message.
[_]Spells handled by autocasting don't letter, and very short cooldowns (under a configurable threshold, default 12 hours) are skipped.
[_]Targeted abilities (e.g. Unnatural healing) can letter too - that's often exactly when you want to be told.
[*]Letters fire even while a pawn is asleep, drafted, in a mental break or traveling with a caravan.
[/list]

[b]Psychic rituals (Anomaly)[/b]
[list]
[_]Chronophagy, Void provocation, Blood rain and friends are a separate system from abilities - when their global cooldown ends, the game only shows a small message.
[_]Optionally they get the same dismissable letters. Rituals are never autocast - they need a ritual spot, participants and often targets.
[/list]

[hr]
[h2]Settings[/h2]
[list]
[_]Master switches for autocasting and for letters (both off by default)
[_]Searchable per-spell allow list with live on/off state
[_]Show autocast buttons for all eligible spells instead of only the allowed ones
[_]Letter style and minimum cooldown threshold
[_]Include targeted abilities / Anomaly psychic rituals in the letters
[_]Rich debug logging for troubleshooting
[/list]

[hr]
[h2]Compatibility[/h2]
[list]
[_]Requires the Harmony mod.
[_]For RimWorld 1.6 - all DLCs are optional.
[_]Royalty psycasts, Ideology role abilities, Biotech, Anomaly, Odyssey and modded abilities alike - nothing is hardcoded, eligibility is derived from each ability's definition.
[_]The mod stores only its own settings and keeps no data in your saves - safe to add or remove anytime.
[/list]

[hr]
Source code and details: [url]https://github.com/Riketta/BetterSpells[/url]
```

# Steam Workshop description

Paste the text below into the Workshop item's description field when publishing
(the BBCode renders on Steam, but not in-game - `About/About.xml` carries its own
plain-text description).

```
[h3]Better Spells[/h3]
Autocast your click-and-forget spells, and never miss a ready cooldown again. Everything is optional - nothing runs until you turn it on.

[h3]What it does[/h3]
[b]Autocasting[/b]
[list][*]Eligible spells get an extra toggle button right next to their cast button - just like the deathrest auto-wake button. It stays visible in both on and off states.
[*]While on, the spell is queued automatically the moment its cooldown finishes - as an ordinary queued job, so sleep, current work and your own orders are never interrupted.
[*]Only click-and-forget spells qualify: self-buffs and auras that need no target. Targeted spells, world-targeted spells, ritual starters and confirmation-dialog casts are never autocast.
[*]The toggle is shared by all pawns: selecting several shows a single merged button, never one per pawn.
[*]Autocast attempts pause during mental breaks and resume when the pawn recovers.[/list]

[b]Cooldown-ready letters[/b]
[list][*]When an ability's cooldown completes, a dismissable letter arrives - same system as raid, orbital trader or masterwork notifications, with a per-pawn breakdown. Click it to jump to the pawns.
[*]Letter style is configurable: neutral (trader-like), positive (masterwork-like) or raid-like (red, threat sound) - much harder to miss than the small default message.
[*]Spells handled by autocasting don't letter, and very short cooldowns (under a configurable threshold, default 12 hours) are skipped.
[*]Targeted abilities (e.g. Unnatural healing) can letter too - that's often exactly when you want to be told.
[*]Letters fire even while a pawn is asleep, drafted, in a mental break or traveling with a caravan.[/list]

[b]Psychic rituals (Anomaly)[/b]
[list][*]Chronophagy, Void provocation, Blood rain and friends are a separate system from abilities - when their global cooldown ends, the game only shows a small message.
[*]Optionally they get the same dismissable letters. Rituals are never autocast - they need a ritual spot, participants and often targets.[/list]

[h3]Settings[/h3]
Master switches for autocasting and for letters, both off by default. A searchable per-spell allow list carries the live on/off state, and buttons can be shown for all eligible spells instead of only the allowed ones. Letter style and the minimum cooldown threshold are configurable, as are targeted-ability and Anomaly psychic ritual letters. Rich debug logging helps with troubleshooting.

[h3]Compatibility[/h3]
Requires RimWorld 1.6 and [url=https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077]Harmony[/url]; all DLCs are optional.
Pure Harmony mod: no def changes, nothing saved to the game state - safe to add and remove at any time. Royalty psycasts, Ideology role abilities, Biotech, Anomaly, Odyssey and modded abilities work automatically - nothing is hardcoded, eligibility is derived from each ability's definition.

Source code and details: [url]https://github.com/Riketta/BetterSpells[/url]
```

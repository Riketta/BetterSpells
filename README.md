# Better Spells

A RimWorld 1.6 mod adding optional **autocasting** for click-and-forget abilities and
**dismissable cooldown-ready letters**. Nothing is active by default - you opt in per
feature and per spell.

Works with Royalty psycasts, Ideology role and ritual abilities, Biotech, Anomaly,
Odyssey and modded abilities alike: nothing is hardcoded, eligibility is derived from
each ability's definition.

## Autocasting

- Eligible spells get an extra toggle button **right next to the cast button** (like
  the deathrest auto-wake button). It flips autocasting on and off, stays visible in
  both states, and is shared by all pawns - selecting several pawns shows a single
  merged toggle.
- While on, the spell is queued for every pawn that has it as soon as its cooldown
  finishes and it can be cast. Casts are enqueued as ordinary queued jobs, so sleep,
  current work and your own orders are never interrupted.
- Eligible spells are the ones that need no target - self-buffs and auras like
  Neuroquake or Combat command. Abilities that need a picked target or a world tile,
  start rituals, or show a confirmation dialog are never autocast.

Spells are allowed per spell in **Mod settings** (searchable, with live on/off state);
allowed spells start on, and the in-game button is the live switch. A setting can
show the button for every eligible spell instead of only the allowed ones.

## Cooldown-ready letters

When an ability's cooldown completes, a letter arrives - the same system as raid,
orbital trader and masterwork notifications. It is dismissable, lists every pawn
whose spell just finished its cooldown, and jumps to them when clicked.

- Autocast spells don't letter - they are cast automatically.
- Abilities with very short cooldowns (under a configurable threshold, default
  12 hours) don't letter.
- Targeted abilities (e.g. Unnatural healing) letter too - that's often exactly when
  you want to be told - but can be excluded.
- Letter style: neutral (trader-like), positive (masterwork-like) or raid-like
  (red, threat sound).

Letters fire on the same tick and from the same event the game's own ability-ready
notification uses, so they work for pawns that are asleep, drafted, in a mental
break or traveling with a caravan.

## Settings

Everything is optional and off by default: master switches for autocasting and
letters, per-spell allow list, letter style, cooldown threshold, targeted-ability
inclusion, button visibility, and a debug logging toggle that traces eligibility
decisions, autocast attempts, and letter sends (prefixed `[BetterSpells]`) in the
game log.

## Compatibility

- RimWorld 1.6, all DLCs optional; no DLC content is required.
- Modded abilities are picked up automatically from their definitions.
- Safe to add or remove at any time: the mod stores only its own settings and keeps
  no data inside your saves.

## Technical notes

Three Harmony postfixes, no transpilers:

- `Ability.CooldownTick` - the ready event, same source and tick as the vanilla
  ability-ready notification; drives cooldown tracking and letters.
- `TickManager.DoSingleTick` - flushes pending letters and periodically retries
  autocast attempts.
- `Ability.GetGizmos` - injects the autocast toggle after the cast command.

Autocast jobs reuse the vanilla self-cast path (`Ability.GetJob` + job queue), so
modded ability classes keep working. Tracking state is session-scoped (no letters
piled up after loading a save), and the periodic scan is exception-guarded so a
broken modded ability cannot break the tick loop.

Ships with Harmony 2.3.3 (MIT, see `Assemblies/0Harmony.LICENSE.txt`).

## Building from source

Requires the .NET SDK. The csproj assumes a default Steam install path; point it at
yours if needed. Build the Release configuration for the dll you ship - a plain
`dotnet build` defaults to Debug:

```
cd Source/BetterSpells
dotnet build -c Release -p:RimWorldDir="C:\Path\To\RimWorld"
```

The output lands in `Assemblies/BetterSpells.dll`; the whole `BetterSpells` folder
can be copied or linked into the game's `Mods` directory.

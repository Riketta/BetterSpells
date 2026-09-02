# Better Spells

A RimWorld 1.6 mod adding **optional autocasting** for click-and-forget abilities and
**dismissable cooldown-ready letters**. Nothing is active by default.

## Autocasting

- An extra toggle button appears **right next to the ability's cast button** (like the
  deathrest auto-wake button). The setting is **per spell and shared by every pawn**;
  with several pawns selected the buttons merge into a single toggle.
- Two-layer state: the mod settings checkbox means **this spell may be autocast**
  (it starts on when checked); the in-game button flips the live **on/off** state and
  stays visible in both states, so a spell switched off can be re-enabled anytime.
- When a spell is on, it is queued automatically for every pawn that has it as soon
  as its cooldown finishes and it can be cast. Casts go in as **ordinary queued
  jobs** (`jobQueue.EnqueueLast`), so sleep, current work and player orders are never
  interrupted.
- Eligibility is derived from ability defs at runtime - nothing is hardcoded:
  - `targetRequired == false` (vanilla self-casts these without targeting), and
  - not world-cell targeted, no confirmation dialog, not a ritual/speech starter.
- Targeted abilities (unit or area, e.g. Skip, Unnatural healing), world-targeted
  abilities, ritual speeches and confirmation-dialog casts are never autocast.

Spells can also be allowed/disallowed in **Mod settings**, with search - the list
shows each spell's live on/off state. By default the in-game toggle button only
appears for spells allowed there; a setting can show it for every eligible spell
instead.

## Cooldown-ready letters

When an ability's cooldown completes, a **letter** arrives (same system as raid,
orbital trader and masterwork notifications - dismissable, with a detailed per-pawn
breakdown; click to jump to the pawn):

- one letter per ability per ready period (a new cooldown means a new letter later);
- pawns whose instances of the same spell finish together share one letter;
- spells handled by autocasting **never** letter;
- abilities whose total cooldown is under the configurable threshold (**default 12
  hours**) never letter;
- targeted abilities are included by default (that's the point: Unnatural healing is
  ready and you should know), configurable;
- letter style is configurable: neutral (trader-like), positive (masterwork-like), or
  raid-like (red, threat sound).

## Debug logging

Mod settings have a debug toggle. When on, the mod logs (prefixed `[BetterSpells]`)
eligibility decisions per ability def, cooldown-ready transitions, letter sends and
skip reasons, autocast attempts with skip reasons, and toggle clicks.

## Files

```
About/               mod metadata
Languages/English/   translations
Source/BetterSpells  C# source + csproj
Assemblies/          compiled BetterSpells.dll + 0Harmony.dll (2.3.3)
```

## Building

Requires the .NET SDK. The csproj defaults to
`E:\SteamLibrary\steamapps\common\RimWorld`; override with your install path:

```
cd Source/BetterSpells
dotnet build -p:RimWorldDir="C:\Path\To\RimWorld"
```

Output lands in `Assemblies/BetterSpells.dll`. The whole `BetterSpells` folder can be
symlinked/copied into the game's `Mods` directory.

## Technical notes

- Harmony patches: `TickManager.DoSingleTick` (postfix, flushes letters and drives
  the periodic autocast scan), `Ability.CooldownTick` (postfix - the same method
  and tick the built-in cooldown notification uses, so ready events are caught for
  every pawn: asleep, in a mental break, drafted, on a caravan) and
  `Ability.GetGizmos` (postfix, injects the toggle after the cast command).
- Cooldown tracking is event-driven and self-seeding: abilities seen mid-cooldown
  after a save load are picked up automatically, and the ready edge fires on the
  exact tick vanilla's own notification fires. Tracking state is session-scoped;
  the ability scan is wrapped in a defensive try/catch (like vanilla's alert loop),
  so a broken modded ability cannot break the tick loop.
- Autocast attempts are gated on player control (deliberately not fired during
  mental breaks); once the break ends, the periodic scan casts the still-ready
  ability. Letters are gated on faction only (stable during breaks).
- The toggle gizmo is cached per ability instance (ConditionalWeakTable), matching
  vanilla's cached cast gizmo - no per-frame allocations or translation lookups.
- Autocast retries back off exponentially (250 ticks doubling to 2500) while an
  ability's queued job keeps vanishing without a cast; the counter resets whenever
  the ability is observed casting or on cooldown.
- Multi-pawn gizmo merging: the toggle overrides `GroupsWith` so same-spell toggles
  collapse into one button, and `InheritInteractionsFrom` returns false (the setting
  is global, so the grid's activate-all-in-group behavior must not run).
- Eligibility and settings-list caches rebuild automatically when the loaded ability
  def count changes (dev "reload mods").

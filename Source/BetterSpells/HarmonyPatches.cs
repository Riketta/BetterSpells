using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterSpells
{
	[StaticConstructorOnStartup]
	public static class BetterSpellsInit
	{
		static BetterSpellsInit()
		{
			Harmony harmony = new Harmony("Riketta.BetterSpells");
			harmony.PatchAll(typeof(BetterSpellsInit).Assembly);
			if (BetterSpellsMod.DebugLogging)
			{
				BetterSpellsCore.DebugLog("Harmony patches applied");
			}
		}
	}

	/// <summary>Drives the autocast/ready-tracking engine once per game tick.</summary>
	[HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
	public static class TickManager_DoSingleTick_BetterSpellsPatch
	{
		public static void Postfix()
		{
			BetterSpellsCore.Tick();
		}
	}

	/// <summary>Same event source as the built-in cooldown-complete notification:
	/// Ability.CooldownTick runs from Pawn.Tick for every pawn regardless of mental
	/// state, sleep or player control, so ready events are never missed. (A polling
	/// scan gated on player control silently loses events during e.g. mental breaks,
	/// because IsColonistPlayerControlled is false while a break is active.)</summary>
	[HarmonyPatch(typeof(Ability), "CooldownTick")]
	public static class Ability_CooldownTick_BetterSpellsPatch
	{
		public static void Postfix(Ability __instance)
		{
			BetterSpellsCore.OnAbilityCooldownTick(__instance);
		}
	}

	/// <summary>Psychic rituals (Anomaly) are not abilities: Chronophagy, Void provocation
	/// and friends are PsychicRitualDefs whose global per-def cooldowns live in
	/// GameComponent_PsychicRitualManager, which also emits the built-in "can be cast
	/// again" message in the same tick it clears an expired entry. This prefix hands the
	/// same just-expired set to the letter engine before the original runs.</summary>
	[HarmonyPatch(typeof(GameComponent_PsychicRitualManager), nameof(GameComponent_PsychicRitualManager.GameComponentTick))]
	public static class GameComponent_PsychicRitualManager_GameComponentTick_BetterSpellsPatch
	{
		// The cooldown map is private. The public GetAvailableTick cannot distinguish "not
		// on cooldown" from "expires exactly now", so the map is read directly; a dictionary
		// reference read costs nothing per tick and the map is empty without Anomaly.
		private static readonly FieldInfo ritualCooldownsField = AccessTools.Field(
			typeof(GameComponent_PsychicRitualManager), "ritualCooldowns");

		public static void Prefix(GameComponent_PsychicRitualManager __instance)
		{
			BetterSpellsCore.OnPsychicRitualTick(
				ritualCooldownsField?.GetValue(__instance) as Dictionary<PsychicRitualDef, int>);
		}
	}

	/// <summary>Appends the autocast toggle right after the ability's cast button.
	/// Ability.GetGizmos is an iterator, so the result enumerable is wrapped and the
	/// toggle inserted after the first (cast) command; its Order is set slightly above
	/// the cast button's so the sorted gizmo grid keeps them adjacent.</summary>
	[HarmonyPatch(typeof(Ability), nameof(Ability.GetGizmos))]
	public static class Ability_GetGizmos_BetterSpellsPatch
	{
		public static void Postfix(Ability __instance, ref IEnumerable<Command> __result)
		{
			if (__result == null || !BetterSpellsMod.ShowToggleFor(__instance))
			{
				return;
			}
			__result = BetterSpellsCore.InjectAutocastToggle(__instance, __result);
		}
	}

	/// <summary>The autocast toggle. Autocast is configured per spell (shared by every
	/// pawn that has it), so when several pawns are selected the toggles merge into a
	/// single button instead of one per pawn.</summary>
	public class Command_ToggleAutocast : Command_Toggle
	{
		private readonly AbilityDef def;

		public Command_ToggleAutocast(AbilityDef def)
		{
			this.def = def;
		}

		public override bool GroupsWith(Gizmo other)
		{
			return other is Command_ToggleAutocast toggle && toggle.def == def;
		}

		/// <summary>State is a single shared setting, not per instance. The gizmo grid
		/// re-activates every grouped toggle whose interactions are inherited, which for
		/// a shared state would flip it once per pawn (a no-op with an even count), so
		/// only the clicked instance may act.</summary>
		public override bool InheritInteractionsFrom(Gizmo other)
		{
			return false;
		}

		public override bool ShowPawnDetailsWith(Gizmo other)
		{
			// The shared setting makes every instance identical, so pawn details are
			// meaningless here; keep the merged button clean.
			return false;
		}
	}

	public static partial class BetterSpellsCore
	{
		/// <summary>Gizmos are re-enumerated every UI frame, so the toggle is cached per
		/// ability instance (like vanilla caches its cast gizmo). ConditionalWeakTable
		/// needs no manual pruning: entries die with their ability.</summary>
		private static readonly ConditionalWeakTable<Ability, Command_ToggleAutocast> toggleCache =
			new ConditionalWeakTable<Ability, Command_ToggleAutocast>();

		public static IEnumerable<Command> InjectAutocastToggle(Ability ability, IEnumerable<Command> source)
		{
			bool inserted = false;
			foreach (Command command in source)
			{
				yield return command;
				if (inserted || (command.defaultLabel != null && command.defaultLabel.StartsWith("DEV:")))
				{
					continue;
				}
				inserted = true;
				if (!toggleCache.TryGetValue(ability, out Command_ToggleAutocast toggle))
				{
					toggle = MakeAutocastToggle(ability, command.Order);
					toggleCache.Add(ability, toggle);
				}
				yield return toggle;
			}
		}

		private static Command_ToggleAutocast MakeAutocastToggle(Ability ability, float castOrder)
		{
			AbilityDef def = ability.def;
			Command_ToggleAutocast command_Toggle = new Command_ToggleAutocast(def)
			{
				defaultLabel = "BetterSpells_AutocastLabel".Translate().CapitalizeFirst(),
				defaultDesc = "BetterSpells_AutocastDesc".Translate(def.LabelCap.Named("ABILITY")).Resolve(),
				icon = def.uiIcon,
				Order = castOrder + 0.00001f,
				isActive = () => BetterSpellsMod.IsSpellAutocastEnabled(def),
				toggleAction = delegate
				{
					bool newValue = !BetterSpellsMod.IsSpellAutocastEnabled(def);
					BetterSpellsMod.SetSpellAutocast(def, newValue);
					if (BetterSpellsMod.DebugLogging)
					{
						DebugLog($"toggle clicked: {def.defName} -> {(newValue ? "on" : "off")}");
					}
				}
			};
			return command_Toggle;
		}
	}
}

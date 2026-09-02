using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterSpells
{
	public class BetterSpellsSettings : ModSettings
	{
		/// <summary>Master switch for the whole autocast feature. Off by default.</summary>
		public bool autocastEnabled;

		/// <summary>Master switch for cooldown-ready letters. Off by default.</summary>
		public bool readyLettersEnabled;

		/// <summary>When false, letters only cover autocastable (no-target) abilities;
		/// when true (default) targeted abilities like Unnatural healing letter too.</summary>
		public bool lettersIncludeTargeted = true;

		/// <summary>0 = Neutral (like orbital trader), 1 = Positive (like masterwork),
		/// 2 = ThreatBig (raid-like red, most aggressive).</summary>
		public int letterStyle;

		/// <summary>Abilities whose total cooldown is shorter than this never letter.
		/// Default 12 hours; 0 letters everything.</summary>
		public float minCooldownHours = BetterSpellsCore.DefaultMinCooldownHours;

		/// <summary>When false (default), autocast toggle buttons only appear for spells
		/// enabled here; when true, every eligible spell gets a toggle button.</summary>
		public bool showTogglesForAllEligible;

		public bool debugLogging;

		/// <summary>defNames of abilities allowed to be autocast (the settings opt-in
		/// that also controls whether the in-game autocast button appears). Empty by
		/// default.</summary>
		public List<string> autocastSpells = new List<string>();

		/// <summary>defNames of allowed abilities whose autocast was switched off via
		/// the in-game button. Allowed spells are on by default; entries here are the
		/// explicitly off ones (the button stays visible so they can be re-enabled).</summary>
		public List<string> autocastDisabled = new List<string>();

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref autocastEnabled, "autocastEnabled", false);
			Scribe_Values.Look(ref readyLettersEnabled, "readyLettersEnabled", false);
			Scribe_Values.Look(ref lettersIncludeTargeted, "lettersIncludeTargeted", true);
			Scribe_Values.Look(ref letterStyle, "letterStyle", 0);
			Scribe_Values.Look(ref minCooldownHours, "minCooldownHours", BetterSpellsCore.DefaultMinCooldownHours);
			Scribe_Values.Look(ref showTogglesForAllEligible, "showTogglesForAllEligible", false);
			Scribe_Values.Look(ref debugLogging, "debugLogging", false);
			Scribe_Collections.Look(ref autocastSpells, "autocastSpells", LookMode.Value);
			Scribe_Collections.Look(ref autocastDisabled, "autocastDisabled", LookMode.Value);
			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
			{
				autocastSpells ??= new List<string>();
				autocastDisabled ??= new List<string>();
			}
		}
	}

	public class BetterSpellsMod : Mod
	{
		public static BetterSpellsMod Instance;

		public BetterSpellsSettings settings;

		private Vector2 scrollPosition = Vector2.zero;

		private string search = "";

		private string minCooldownBuffer;

		private static List<AbilityDef> sortedEligibleCache;

		private static int sortedEligibleDefCount = -1;

		public BetterSpellsMod(ModContentPack content) : base(content)
		{
			Instance = this;
			settings = GetSettings<BetterSpellsSettings>();
		}

		public static bool AutocastActive => Instance != null && Instance.settings.autocastEnabled;

		public static bool DebugLogging => Instance?.settings.debugLogging ?? false;

		/// <summary>Whether autocasting is currently ON for the spell (allowed and not
		///	switched off via the in-game button). Drives autocast attempts and letter
		///	suppression.</summary>
		public static bool IsSpellAutocastEnabled(AbilityDef def)
		{
			BetterSpellsSettings s = Instance?.settings;
			if (s == null)
			{
				return false;
			}
			return s.autocastSpells.Contains(def.defName) && !s.autocastDisabled.Contains(def.defName);
		}

		/// <summary>Button path: flips the live autocast state. Disabling keeps the spell
		/// allowed (so the button stays visible for re-enabling); enabling also opts the
		/// spell in, covering the show-buttons-for-all mode.</summary>
		public static void SetSpellAutocast(AbilityDef def, bool value)
		{
			BetterSpellsSettings s = Instance?.settings;
			if (s == null)
			{
				return;
			}
			bool changed;
			if (value)
			{
				changed = !s.autocastSpells.Contains(def.defName) || s.autocastDisabled.Contains(def.defName);
				if (!s.autocastSpells.Contains(def.defName))
				{
					s.autocastSpells.Add(def.defName);
				}
					s.autocastDisabled.Remove(def.defName);
			}
			else
			{
				changed = !s.autocastDisabled.Contains(def.defName);
				if (changed && !s.autocastSpells.Contains(def.defName))
				{
					// Not allowed in settings: nothing to disable.
					changed = false;
				}
				if (changed)
				{
					s.autocastDisabled.Add(def.defName);
				}
			}
			if (changed)
			{
				// The gizmo toggle changes settings outside the settings dialog, so
				// persist immediately (the dialog path relies on vanilla's WriteSettings
				// on close instead).
				Instance.WriteSettings();
			}
		}

		/// <summary>Whether the autocast toggle gizmo should be offered for this ability.</summary>
		public static bool ShowToggleFor(Ability ability)
		{
			if (!AutocastActive || ability?.def == null || !BetterSpellsCore.IsAutocastable(ability.def))
			{
				return false;
			}
			BetterSpellsSettings s = Instance.settings;
			if (!s.showTogglesForAllEligible && !s.autocastSpells.Contains(ability.def.defName))
			{
				return false;
			}
			Pawn pawn = ability.pawn;
			return pawn != null && (pawn.IsColonistPlayerControlled || pawn.IsColonyMechPlayerControlled
				|| pawn.IsColonySubhumanPlayerControlled || pawn.IsColonyAnimal);
		}

		public override string SettingsCategory()
		{
			return "BetterSpells_SettingsCategory".Translate();
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			Text.Font = GameFont.Small;
			List<AbilityDef> eligible = SortedEligibleDefs();

			float footerHeight = Text.LineHeight * 3f + 8f;
			Rect footerRect = inRect.BottomPartPixels(footerHeight);
			Rect scrollRect = inRect.TopPartPixels(inRect.height - footerHeight - 4f);

			string intro = "BetterSpells_Intro".Translate();
			string enableAutocasting = "BetterSpells_EnableAutocasting".Translate();
			string showToggles = "BetterSpells_ShowTogglesForAll".Translate();
			string enableLetters = "BetterSpells_EnableLetters".Translate();
			string includeTargeted = "BetterSpells_LettersIncludeTargeted".Translate();
			string letterStyleLabel = "BetterSpells_LetterStyle".Translate();
			string minCooldownLabel = "BetterSpells_MinCooldownHours".Translate();

			float width = scrollRect.width - 24f;
			float headerHeight = Text.CalcHeight(intro, width) + 8f;
			headerHeight += RowHeight(enableAutocasting, width);
			if (settings.autocastEnabled)
			{
				headerHeight += RowHeight(showToggles, width);
			}
			headerHeight += RowHeight(enableLetters, width);
			if (settings.readyLettersEnabled)
			{
				headerHeight += RowHeight(includeTargeted, width);
				headerHeight += Mathf.Max(34f, RowHeight(letterStyleLabel, width));
				headerHeight += Text.LineHeight + 2f;
			}
			headerHeight += Text.LineHeight + 6f;
			headerHeight += Text.CalcHeight("BetterSpells_SpellsListHeader".Translate(
				eligible.Count.ToString().Named("COUNT"),
				settings.autocastSpells.Count.ToString().Named("ALLOWED"),
				ActiveAutocastCount().ToString().Named("ACTIVE")), width) + 8f;

			float listHeight = 0f;
			foreach (AbilityDef def in eligible)
			{
				if (MatchesSearch(def))
				{
					listHeight += RowHeight(SpellRowLabel(def), width);
				}
			}
			Rect viewRect = new Rect(0f, 0f, scrollRect.width - 20f, headerHeight + listHeight + 60f);

			Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect);
			Listing_Standard listing = new Listing_Standard();
			listing.Begin(viewRect);
			listing.Label(intro);

			listing.GapLine();
			listCheckbox(listing, enableAutocasting, ref settings.autocastEnabled,
				"BetterSpells_EnableAutocastingTip");
			if (settings.autocastEnabled)
			{
				listCheckbox(listing, showToggles, ref settings.showTogglesForAllEligible,
					"BetterSpells_ShowTogglesForAllTip");
			}

			listing.GapLine();
			listCheckbox(listing, enableLetters, ref settings.readyLettersEnabled,
				"BetterSpells_EnableLettersTip");
			if (settings.readyLettersEnabled)
			{
				listCheckbox(listing, includeTargeted, ref settings.lettersIncludeTargeted,
					"BetterSpells_LettersIncludeTargetedTip");
				string[] styleNames = new string[3]
				{
					"BetterSpells_LetterStyleNeutral".Translate(), "BetterSpells_LetterStylePositive".Translate(),
					"BetterSpells_LetterStyleRaid".Translate()
				};
				settings.letterStyle = Mathf.Clamp(settings.letterStyle, 0, 2);
				if (listing.ButtonTextLabeled(letterStyleLabel, styleNames[settings.letterStyle]))
				{
					settings.letterStyle = (settings.letterStyle + 1) % 3;
				}
				settings.minCooldownHours = Mathf.Clamp(settings.minCooldownHours, 0f, 1000f);
				listing.TextFieldNumericLabeled(minCooldownLabel, ref settings.minCooldownHours,
					ref minCooldownBuffer, 0f, 1000f);
			}

			listing.GapLine();
			Rect searchRect = listing.GetRect(Text.LineHeight);
			search = Widgets.TextField(searchRect, search);
			if (!search.NullOrEmpty())
			{
				Rect clearRect = new Rect(searchRect.xMax - 24f, searchRect.y, 24f, searchRect.height);
				if (Widgets.ButtonImage(clearRect, TexButton.Delete, Color.white, GenUI.SubtleMouseoverColor))
				{
					search = "";
				}
			}

			listing.Label("BetterSpells_SpellsListHeader".Translate(eligible.Count.ToString().Named("COUNT"),
				settings.autocastSpells.Count.ToString().Named("ALLOWED"),
				ActiveAutocastCount().ToString().Named("ACTIVE")));

			foreach (AbilityDef def in eligible)
			{
				if (!MatchesSearch(def))
				{
					continue;
				}
				// The checkbox means "this spell may be autocast" (button opt-in); the live
				// on/off state is shown in the label and flipped by the in-game button.
				bool allowed = settings.autocastSpells.Contains(def.defName);
				bool was = allowed;
				string rowLabel = SpellRowLabel(def);
				listing.CheckboxLabeled(rowLabel, ref allowed, def.defName);
				if (allowed != was)
				{
					SetSpellAllowedQuiet(def, allowed);
				}
			}
			listing.End();
			Widgets.EndScrollView();

			Listing_Standard footer = new Listing_Standard();
			footer.Begin(footerRect);
			footer.CheckboxLabeled("BetterSpells_DebugLogging".Translate(), ref settings.debugLogging,
				"BetterSpells_DebugLoggingTip".Translate());
			if (footer.ButtonText("BetterSpells_ClearSpellList".Translate()))
			{
				settings.autocastSpells.Clear();
				settings.autocastDisabled.Clear();
			}
			footer.End();
		}

		private static void listCheckbox(Listing_Standard listing, string label, ref bool value, string tooltipKey)
		{
			bool v = value;
			listing.CheckboxLabeled(label, ref v, tooltipKey.Translate());
			value = v;
		}

		private static float RowHeight(string label, float width)
		{
			return Mathf.Max(Text.LineHeight, Text.CalcHeight(label, width)) + 2f;
		}

		/// <summary>Settings checkbox path: allows a spell for autocasting (on by
		/// default) or removes it entirely, button and all.</summary>
		private static void SetSpellAllowedQuiet(AbilityDef def, bool value)
		{
			BetterSpellsSettings s = Instance.settings;
			if (value)
			{
				if (!s.autocastSpells.Contains(def.defName))
				{
					s.autocastSpells.Add(def.defName);
				}
				s.autocastDisabled.Remove(def.defName);
			}
			else
			{
				s.autocastSpells.Remove(def.defName);
				s.autocastDisabled.Remove(def.defName);
			}
		}

		private static int ActiveAutocastCount()
		{
			int count = 0;
			foreach (string defName in Instance.settings.autocastSpells)
			{
				if (!Instance.settings.autocastDisabled.Contains(defName))
				{
					count++;
				}
			}
			return count;
		}

		private static string SpellRowLabel(AbilityDef def)
		{
			string pack = def.modContentPack?.Name;
			string label = pack.NullOrEmpty() ? def.LabelCap : $"{def.LabelCap} [{pack}]";
			if (Instance.settings.autocastSpells.Contains(def.defName))
			{
				label += " " + (IsSpellAutocastEnabled(def)
					? "BetterSpells_StateOn".Translate().Resolve()
					: "BetterSpells_StateOff".Translate().Resolve());
			}
			return label;
		}

		private bool MatchesSearch(AbilityDef def)
		{
			if (search.NullOrEmpty())
			{
				return true;
			}
			string needle = search.ToLower();
			return def.label != null && def.label.ToLower().Contains(needle)
				|| def.defName.ToLower().Contains(needle)
				|| (def.modContentPack?.Name != null && def.modContentPack.Name.ToLower().Contains(needle));
		}

		private static List<AbilityDef> SortedEligibleDefs()
		{
			if (sortedEligibleCache == null || DefDatabase<AbilityDef>.DefCount != sortedEligibleDefCount)
			{
				sortedEligibleDefCount = DefDatabase<AbilityDef>.DefCount;
				sortedEligibleCache = DefDatabase<AbilityDef>.AllDefs
					.Where(BetterSpellsCore.IsAutocastable)
					.OrderBy(d => d.modContentPack?.Name)
					.ThenBy(d => d.label)
					.ToList();
			}
			return sortedEligibleCache;
		}
	}
}

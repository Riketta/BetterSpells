using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterSpells
{
	/// <summary>Engine: decides which abilities can be autocast, tracks cooldown
	/// completion, queues autocast jobs and sends cooldown-ready letters. Session-scoped
	/// state (deliberately not persisted: after a load the tracking re-baselines
	/// without firing a pile of letters).</summary>
	public static partial class BetterSpellsCore
	{
		private const int ScanIntervalTicks = 60;

		/// <summary>Base time between autocast attempts for the same ability, so a
		/// job that fails to start (e.g. repeatedly invalid conditions) cannot spam.
		/// Doubles per consecutive failed attempt up to AutocastMaxBackoffTicks.</summary>
		private const int AutocastAttemptIntervalTicks = 250;

		private const int AutocastMaxBackoffTicks = 2500;

		/// <summary>Grace period after enqueuing an autocast before a still-unused
		/// ability counts as a failed attempt (warmup and job start take time).</summary>
		private const int AutocastGraceTicks = 600;

		public const float DefaultMinCooldownHours = 12f;

		private static readonly Dictionary<AbilityDef, bool> autocastableCache = new Dictionary<AbilityDef, bool>();

		/// <summary>Defs loaded when the eligibility cache was built; on mismatch (dev
		/// "reload mods" adding/removing ability defs) the cache is rebuilt.</summary>
		private static int eligibilityDefCount = -1;

		private static readonly HashSet<Ability> wasOnCooldown = new HashSet<Ability>();

		private static readonly Dictionary<Ability, int> lastAutocastAttemptTick = new Dictionary<Ability, int>();

		/// <summary>Consecutive autocast attempts whose queued job vanished without the
		/// ability ever casting; drives the retry backoff.</summary>
		private static readonly Dictionary<Ability, int> autocastFailures = new Dictionary<Ability, int>();

		/// <summary>Tick of the last enqueued autocast job still awaiting its outcome.</summary>
		private static readonly Dictionary<Ability, int> pendingAutocastTick = new Dictionary<Ability, int>();

		private static readonly HashSet<Ability> seenAbilities = new HashSet<Ability>();

		/// <summary>Per scan: ability instances that just finished cooldown and will get
		/// a letter, grouped by def afterwards so same-def pawns share one letter.</summary>
		private static readonly List<Ability> newlyReady = new List<Ability>();

		private static readonly Dictionary<AbilityDef, List<Ability>> readyByDef = new Dictionary<AbilityDef, List<Ability>>();

		private static readonly List<Pawn> letterTargets = new List<Pawn>();

		/// <summary>Per tick: psychic rituals whose global cooldown just ended (vanilla is
		/// about to clear and message them).</summary>
		private static readonly List<PsychicRitualDef> expiredRituals = new List<PsychicRitualDef>();

		private static Game trackedGame;

		/// <summary>Called every game tick via a Harmony postfix on TickManager.DoSingleTick.</summary>
		public static void Tick()
		{
			Game game = Current.Game;
			if (game == null)
			{
				return;
			}
			if (trackedGame != game)
			{
				if (BetterSpellsMod.DebugLogging)
				{
					DebugLog("game changed; resetting tracking state");
				}
				trackedGame = game;
				wasOnCooldown.Clear();
				lastAutocastAttemptTick.Clear();
				autocastFailures.Clear();
				pendingAutocastTick.Clear();
			}
			if (newlyReady.Count > 0)
			{
				// Letters flush every tick so same-tick completions group into one letter;
				// the heavier scan runs on its own cadence below.
				SendReadyLetters();
			}
			if (Find.TickManager.TicksGame % ScanIntervalTicks != 0)
			{
				return;
			}
			try
			{
				Scan();
			}
			catch (Exception ex)
			{
				// Never let a modded ability break the game tick loop through our postfix
				// (same defensive pattern vanilla uses for its alert loop).
				Log.ErrorOnce($"[BetterSpells] Exception during ability scan: {ex}", 8465123);
			}
		}

		/// <summary>Called every tick per ability via a Harmony postfix on
		/// Ability.CooldownTick - the same method (and tick) the built-in notification
		/// uses. Tracks the on/off-cooldown edge and queues ready letters. Seeding is
		/// automatic: mid-cooldown abilities are seen every tick, including right
		/// after a save load.</summary>
		internal static void OnAbilityCooldownTick(Ability ability)
		{
			if (!ability.HasCooldown)
			{
				return;
			}
			Pawn pawn = ability.pawn;
			// Faction is stable across mental breaks (unlike IsColonistPlayerControlled),
			// so cooldown events for colony pawns are never lost.
			if (pawn == null || pawn.Faction != Faction.OfPlayer)
			{
				return;
			}
			if (ability.OnCooldown)
			{
				if (!wasOnCooldown.Contains(ability))
				{
					wasOnCooldown.Add(ability);
					// A cast started this cooldown: any prior autocast attempt succeeded,
					// so retry bookkeeping resets.
					ClearAttemptState(ability);
				}
				return;
			}
			if (wasOnCooldown.Remove(ability) && ability.CooldownTicksTotal > 0 && !ability.Casting)
			{
				if (ShouldSendLetter(ability, BetterSpellsMod.AutocastActive, out string reason))
				{
					newlyReady.Add(ability);
				}
				else if (BetterSpellsMod.DebugLogging)
				{
					DebugLog($"ready, no letter ({reason}): {LogName(ability)}");
				}
			}
		}

		private static void Scan()
		{
			seenAbilities.Clear();
			bool autocastOn = BetterSpellsMod.AutocastActive;
			// Maps + caravans + travelling transport pods, player faction. Cooldown events
			// come from the CooldownTick postfix; this scan only retries autocast attempts
			// and prunes stale tracking state. Pawns in a mental break are seen (pruning)
			// but not given autocast attempts - casts resume once the break ends.
			List<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
			for (int p = 0; p < pawns.Count; p++)
			{
				Pawn pawn = pawns[p];
				if (pawn.abilities == null)
				{
					continue;
				}
				bool controlled = PlayerControls(pawn);
				List<Ability> abilities = pawn.abilities.AllAbilitiesForReading;
				for (int i = 0; i < abilities.Count; i++)
				{
					Ability ability = abilities[i];
					seenAbilities.Add(ability);
					if (!controlled || !autocastOn || ability.Casting)
					{
						if (ability.Casting)
						{
							ClearAttemptState(ability);
						}
						continue;
					}
					if (!ability.HasCooldown || ability.OnCooldown)
				{
						continue;
					}
					TryAutocast(ability);
				}
			}
			PruneUnseen();
		}

		private static bool PlayerControls(Pawn pawn)
		{
			return pawn.IsColonistPlayerControlled || pawn.IsColonyMechPlayerControlled
				|| pawn.IsColonySubhumanPlayerControlled || pawn.IsColonyAnimal;
		}

		/// <summary>Whether this ready period should produce a cooldown-ready letter.</summary>
		private static bool ShouldSendLetter(Ability ability, bool autocastOn, out string reason)
		{
			BetterSpellsSettings settings = BetterSpellsMod.Instance?.settings;
			if (settings == null || !settings.readyLettersEnabled)
			{
				reason = "letters disabled";
				return false;
			}
			if (autocastOn && BetterSpellsMod.IsSpellAutocastEnabled(ability.def))
			{
				reason = "autocast will handle it";
				return false;
			}
			float minTicks = settings.minCooldownHours * 2500f;
			if (ability.CooldownTicksTotal < minTicks)
			{
				reason = $"cooldown {ability.CooldownTicksTotal} ticks below threshold {minTicks:F0}";
				return false;
			}
			if (!settings.lettersIncludeTargeted && !IsAutocastable(ability.def))
			{
				reason = "targeted abilities excluded";
				return false;
			}
			reason = null;
			return true;
		}

		private static void TryAutocast(Ability ability)
		{
			AbilityDef def = ability.def;
			if (!IsAutocastable(def) || !BetterSpellsMod.IsSpellAutocastEnabled(def))
			{
				return;
			}
			Pawn pawn = ability.pawn;
			if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Deathresting || pawn.jobs == null)
			{
				return;
			}
			// CanQueueCast covers: castable now (cooldown/charges/comps, psycast psyfocus),
			// and not already queued or being cast for this ability. Reaching this point
			// also means any previously queued autocast job is gone - if it never led to a
			// cast, that attempt failed.
			if (!ability.CanQueueCast)
			{
				return;
			}
			if (!ability.CanApplyOn((LocalTargetInfo)pawn))
			{
				if (BetterSpellsMod.DebugLogging)
				{
					DebugLog($"autocast skipped, cannot apply on self: {LogName(ability)}");
				}
				return;
			}
			int now = Find.TickManager.TicksGame;
			if (pendingAutocastTick.TryGetValue(ability, out int pendingAt))
			{
				pendingAutocastTick.Remove(ability);
				if (now - pendingAt > AutocastGraceTicks)
				{
					autocastFailures.TryGetValue(ability, out int fails);
					autocastFailures[ability] = fails + 1;
					if (BetterSpellsMod.DebugLogging)
					{
						DebugLog($"autocast attempt failed ({fails + 1} consecutive), backing off: {LogName(ability)}");
					}
				}
			}
			autocastFailures.TryGetValue(ability, out int failureCount);
			int shift = failureCount < 4 ? failureCount : 4;
			int interval = AutocastAttemptIntervalTicks << shift;
			if (interval > AutocastMaxBackoffTicks)
			{
				interval = AutocastMaxBackoffTicks;
			}
			if (lastAutocastAttemptTick.TryGetValue(ability, out int last) && now - last < interval)
			{
				return;
			}
			lastAutocastAttemptTick[ability] = now;
			if (ability.verb?.verbProps?.nonInterruptingSelfCast == true)
			{
				// Vanilla casts these instantly without a job. Only do that while awake -
				// there is no queue path for them, and an instant cast while sleeping
				// would disturb the pawn.
				if (!pawn.Awake())
				{
					return;
				}
				if (!ability.verb.TryStartCastOn(ability.verb.Caster))
				{
					autocastFailures[ability] = failureCount + 1;
					return;
				}
				pendingAutocastTick[ability] = now;
				if (BetterSpellsMod.DebugLogging)
				{
					DebugLog($"autocast (instant): {LogName(ability)}");
				}
				return;
			}
			// Queue as a normal queued job: current work, sleep and player orders are
			// left untouched; the cast runs when the pawn gets to it (mirrors the
			// vanilla self-cast path from Command_Ability.ProcessInput).
			Job job = ability.GetJob(pawn, LocalTargetInfo.Invalid);
			pawn.jobs.jobQueue.EnqueueLast(job);
			pendingAutocastTick[ability] = now;
			if (BetterSpellsMod.DebugLogging)
			{
				DebugLog($"autocast (queued): {LogName(ability)}");
			}
		}

		private static void ClearAttemptState(Ability ability)
		{
			lastAutocastAttemptTick.Remove(ability);
			autocastFailures.Remove(ability);
			pendingAutocastTick.Remove(ability);
		}

		/// <summary>One letter per ability def per scan: all pawns whose instances of the
		/// def completed cooldown in this scan share a single, dismissable letter.</summary>
		private static void SendReadyLetters()
		{
			readyByDef.Clear();
			for (int i = 0; i < newlyReady.Count; i++)
			{
				Ability ability = newlyReady[i];
				if (!readyByDef.TryGetValue(ability.def, out List<Ability> list))
				{
					list = new List<Ability>();
					readyByDef[ability.def] = list;
				}
				list.Add(ability);
			}
			newlyReady.Clear();
			foreach (KeyValuePair<AbilityDef, List<Ability>> pair in readyByDef)
			{
				SendLetterForDef(pair.Key, pair.Value);
			}
			readyByDef.Clear();
		}

		private static void SendLetterForDef(AbilityDef def, List<Ability> abilities)
		{
			StringBuilder text = new StringBuilder();
			text.AppendLine("BetterSpells_LetterText".Translate().Resolve());
			letterTargets.Clear();
			for (int i = 0; i < abilities.Count; i++)
			{
				Ability ability = abilities[i];
				Pawn pawn = ability.pawn;
				if (pawn == null)
				{
					continue;
				}
				letterTargets.Add(pawn);
				string cooldown = ability.CooldownTicksTotal.ToStringTicksToPeriod();
				string tail = IsAutocastable(def)
					? "BetterSpells_LetterLineAutocastable".Translate().Resolve()
					: "BetterSpells_LetterLineTargeted".Translate().Resolve();
				text.AppendLine("  - " + "BetterSpells_LetterLine".Translate(
					pawn.NameShortColored.Resolve().Named("PAWN"),
					cooldown.Named("COOLDOWN"),
					tail.Named("TAIL")).Resolve());
			}
			if (letterTargets.Count == 0)
			{
				return;
			}
			string label = letterTargets.Count == 1
				? "BetterSpells_LetterLabelSingle".Translate(def.LabelCap)
				: "BetterSpells_LetterLabel".Translate(def.LabelCap, letterTargets.Count.ToString());
			Find.LetterStack.ReceiveLetter(label, text.ToString().TrimEndNewlines(), LetterDefFor(),
				new LookTargets(letterTargets));
			if (BetterSpellsMod.DebugLogging)
			{
				DebugLog($"letter sent: {def.defName} x{letterTargets.Count}");
			}
			letterTargets.Clear();
		}

		private static LetterDef LetterDefFor()
		{
			switch (BetterSpellsMod.Instance?.settings.letterStyle ?? 0)
			{
				case 1:
					return LetterDefOf.PositiveEvent;
				case 2:
					// "Raid-like": red tab and threat sound, for maximum visibility.
					return LetterDefOf.ThreatBig;
				default:
					return LetterDefOf.NeutralEvent;
			}
		}

		/// <summary>Called every tick via a Harmony prefix on
		/// GameComponent_PsychicRitualManager.GameComponentTick - the same event and tick
		/// the built-in "can be cast again" message uses. The map entries whose end tick
		/// has been reached are exactly the rituals vanilla is about to clear and message,
		/// so no separate tracking state is needed: map membership is the cooldown state,
		/// and the game saves the map itself.</summary>
		internal static void OnPsychicRitualTick(Dictionary<PsychicRitualDef, int> ritualCooldowns)
		{
			if (ritualCooldowns == null || ritualCooldowns.Count == 0)
			{
				return;
			}
			BetterSpellsSettings settings = BetterSpellsMod.Instance?.settings;
			if (settings == null || !settings.readyLettersEnabled || !settings.ritualReadyLetters)
			{
				return;
			}
			int now = Find.TickManager.TicksGame;
			float minTicks = settings.minCooldownHours * 2500f;
			expiredRituals.Clear();
			foreach (KeyValuePair<PsychicRitualDef, int> pair in ritualCooldowns)
			{
				if (pair.Value <= now)
				{
					expiredRituals.Add(pair.Key);
				}
			}
			for (int i = 0; i < expiredRituals.Count; i++)
			{
				PsychicRitualDef def = expiredRituals[i];
				// Same condition the vanilla message applies: a ritual locked behind
				// unfinished research is not announced as ready.
				if (def.researchPrerequisite != null && !def.researchPrerequisite.IsFinished)
				{
					if (BetterSpellsMod.DebugLogging)
					{
						DebugLog($"ritual ready, no letter (research unfinished): {def.defName}");
					}
					continue;
				}
				if (def.cooldownHours * 2500f < minTicks)
				{
					if (BetterSpellsMod.DebugLogging)
					{
						DebugLog($"ritual ready, no letter (cooldown {def.cooldownHours}h below threshold): {def.defName}");
					}
					continue;
				}
				SendPsychicRitualLetter(def);
			}
			expiredRituals.Clear();
		}

		/// <summary>Psychic ritual cooldowns are global per def, so there is no pawn or
		/// spot to jump to; the letter is plain dismissable text.</summary>
		private static void SendPsychicRitualLetter(PsychicRitualDef def)
		{
			Find.LetterStack.ReceiveLetter(
				"BetterSpells_RitualReadyLabel".Translate(def.LabelCap),
				"BetterSpells_RitualLetterText".Translate(def.LabelCap), LetterDefFor());
			if (BetterSpellsMod.DebugLogging)
			{
				DebugLog($"ritual letter sent: {def.defName}");
			}
		}

		private static void PruneUnseen()
		{
			wasOnCooldown.RemoveWhere(a => !seenAbilities.Contains(a));
			RemoveKeysNotSeen(lastAutocastAttemptTick);
			RemoveKeysNotSeen(autocastFailures);
			RemoveKeysNotSeen(pendingAutocastTick);
		}

		private static void RemoveKeysNotSeen(Dictionary<Ability, int> dict)
		{
			List<Ability> stale = null;
			foreach (KeyValuePair<Ability, int> pair in dict)
			{
				if (!seenAbilities.Contains(pair.Key))
				{
					stale ??= new List<Ability>();
					stale.Add(pair.Key);
				}
			}
			if (stale != null)
			{
				for (int i = 0; i < stale.Count; i++)
				{
					dict.Remove(stale[i]);
				}
			}
		}

		internal static void DebugLog(string message)
		{
			Log.Message($"[BetterSpells] {message}");
		}

		private static string LogName(Ability ability)
		{
			return $"{ability.pawn?.LabelShort ?? "null"} - {ability.def.defName}";
		}

		/// <summary>An ability can be autocast only if it needs no picked target:
		/// vanilla self-casts it through QueueCastingJob(pawn, Invalid). Anything that
		/// opens targeting, world targeting, a confirmation dialog or a ritual UI is
		/// excluded - those must stay click-and-forget by the player.</summary>
		public static bool IsAutocastable(AbilityDef def)
		{
			if (DefDatabase<AbilityDef>.DefCount != eligibilityDefCount)
			{
				eligibilityDefCount = DefDatabase<AbilityDef>.DefCount;
				autocastableCache.Clear();
			}
			if (autocastableCache.TryGetValue(def, out bool cached))
			{
				return cached;
			}
			bool result = ComputeAutocastable(def, out string reason);
			autocastableCache[def] = result;
			if (BetterSpellsMod.DebugLogging)
			{
				DebugLog($"eligibility {def.defName}: {(result ? "autocastable" : "not autocastable (" + reason + ")")}");
			}
			return result;
		}

		private static bool ComputeAutocastable(AbilityDef def, out string reason)
		{
			if (def == null)
			{
				reason = "null def";
				return false;
			}
			if (def.targetRequired)
			{
				reason = "requires a target";
				return false;
			}
			if (def.targetWorldCell)
			{
				reason = "world-cell target";
				return false;
			}
			if (!def.confirmationDialogText.NullOrEmpty())
			{
				reason = "has confirmation dialog";
				return false;
			}
			if (!def.comps.NullOrEmpty())
			{
				for (int i = 0; i < def.comps.Count; i++)
				{
					// Speeches / ritual starters (incl. subclasses) open their own flow.
					if (def.comps[i] is CompProperties_AbilityStartRitual)
					{
						reason = "starts a ritual";
						return false;
					}
				}
			}
			reason = null;
			return true;
		}
	}
}

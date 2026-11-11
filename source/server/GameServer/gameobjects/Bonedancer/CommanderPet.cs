using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.Styles;
using DOL.Language;

namespace DOL.GS
{
	public class CommanderPet : BdPet
	{
		private static new readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public readonly Lock ControlledNpcListLock = new();

		public enum eCommanderType
		{
			ReturnedCommander,
			DecayedCommander,
			SkeletalCommander,
			BoneCommander,
			DreadCommander,
			DreadArcher,
			DreadGuardian,
			DreadLich,
			DreadLord,
			Unknown
		}

		public eCommanderType CommanderType { get; protected set; }

		/// <summary>
		/// Classic/SI subordinate slots granted by this commander rank.  This is
		/// deliberately independent of the generic NPC tether field: Returned and
		/// Decayed commanders have none, Skeletal has one, Bone has two, and every
		/// level-45 commander variant has three.
		/// </summary>
		public int SubPetCapacity => ControlledNpcList?.Length ?? 0;

		/// <summary>
		/// True when commander is taunting.
		/// </summary>
		public bool Taunting { get; protected set; } = false;


		/// <summary>
		/// Are the minions assisting this commander?
		/// </summary>
		public bool MinionsAssisting { get; protected set; } = true;

		/// <summary>
		/// Style the commander prefers when set (sticky via styleComponent.NextCombatStyle).
		/// Null means the NPC auto-picker chooses.
		/// </summary>
		public Style PreferredCombatStyle { get; private set; }

		/// <summary>Player-facing taunt toggle for the commander window / /commander command.</summary>
		public void SetTaunting(bool taunting) => Taunting = taunting;

		/// <summary>Player-facing minion-assist toggle for the commander window / /commander command.</summary>
		public void SetMinionsAssisting(bool assisting)
		{
			MinionsAssisting = assisting;
			if (ControlledNpcList == null || ControlledNpcList.Length < 1)
				return;
			if (Brain is not IControlledBrain commBrain)
				return;
			foreach (IControlledBrain minBrain in ControlledNpcList)
			{
				if (minBrain != null)
					minBrain.SetAggressionState(commBrain.AggressionState);
			}
		}

		/// <summary>Player-facing Guardian/Lich preferred-spell pick.</summary>
		public void SetPreferredSpell(eCommanderPreferredSpell preferred) => PreferredSpell = preferred;

		/// <summary>Sticky preferred style, or null to resume auto styles.</summary>
		public void SetPreferredCombatStyle(Style style)
		{
			PreferredCombatStyle = style;
			if (styleComponent != null)
				styleComponent.NextCombatStyle = style;
		}

		/// <summary>Whether this commander type can swap into the given weapon slot.</summary>
		public bool AllowsWeaponSlot(eActiveWeaponSlot slot) => CanUseWeaponSlot(slot);

		/// <summary>Weapon types this commander may equip (for /commander and the UI).</summary>
		public IList<eWeaponType> AvailableWeaponTypes()
		{
			var list = new List<eWeaponType>();
			if (CanUseWeaponSlot(eActiveWeaponSlot.Standard))
			{
				list.Add(eWeaponType.OneHandAxe);
				list.Add(eWeaponType.OneHandHammer);
				list.Add(eWeaponType.OneHandSword);
			}
			if (CanUseWeaponSlot(eActiveWeaponSlot.TwoHanded))
			{
				list.Add(eWeaponType.TwoHandAxe);
				list.Add(eWeaponType.TwoHandHammer);
				list.Add(eWeaponType.TwoHandSword);
			}
			if (CanUseWeaponSlot(eActiveWeaponSlot.Distance))
				list.Add(eWeaponType.Bow);
			return list;
		}

		/// <summary>Parse a short weapon token (axe, 1h-sword, bow, …) into a weapon type.</summary>
		public static bool TryParseWeaponType(string token, out eWeaponType weaponType)
		{
			weaponType = eWeaponType.None;
			if (string.IsNullOrWhiteSpace(token))
				return false;
			string key = token.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
			weaponType = key switch
			{
				"1h-axe" or "1haxe" or "onehandaxe" or "axe1h" => eWeaponType.OneHandAxe,
				"1h-hammer" or "1hhammer" or "onehandhammer" or "hammer1h" => eWeaponType.OneHandHammer,
				"1h-sword" or "1hsword" or "onehandsword" or "sword1h" => eWeaponType.OneHandSword,
				"2h-axe" or "2haxe" or "twohandaxe" or "axe2h" or "twohanded-axe" or "two-handed-axe" => eWeaponType.TwoHandAxe,
				"2h-hammer" or "2hhammer" or "twohandhammer" or "hammer2h" or "twohanded-hammer" or "two-handed-hammer" => eWeaponType.TwoHandHammer,
				"2h-sword" or "2hsword" or "twohandsword" or "sword2h" or "twohanded-sword" or "two-handed-sword" => eWeaponType.TwoHandSword,
				"bow" or "archer" or "distance" => eWeaponType.Bow,
				"axe" => eWeaponType.OneHandAxe,
				"hammer" => eWeaponType.OneHandHammer,
				"sword" => eWeaponType.OneHandSword,
				_ => eWeaponType.None,
			};
			return weaponType != eWeaponType.None;
		}

		/// <summary>
		/// Words the commander offers in the clickable dialog. The bracketed words are what the
		/// player whispers back.
		/// </summary>
		public static string WeaponPhrase(eWeaponType type) => type switch
		{
			eWeaponType.TwoHandSword => "Twohanded Sword",
			eWeaponType.TwoHandAxe => "Twohanded Axe",
			eWeaponType.TwoHandHammer => "Twohanded Hammer",
			eWeaponType.OneHandHammer => "Hammer",
			eWeaponType.OneHandSword => "Sword",
			eWeaponType.OneHandAxe => "Axe",
			eWeaponType.Bow => "Bow",
			_ => type.ToString(),
		};

		/// <summary>
		/// A one-handed sword and a one-handed axe share a slot, so GameNPC.SwitchWeapon does not
		/// broadcast. The client keeps the old model until a later slot change. This is the extra
		/// update that same-slot swap still has to send.
		/// </summary>
		public static bool NeedsExtraEquipmentBroadcast(eActiveWeaponSlot before, eActiveWeaponSlot after)
			=> before == after && after != eActiveWeaponSlot.Distance;

		/// <summary>How many same-slot weapon changes asked for an equipment update. Tests read this.</summary>
		public int SameSlotEquipmentUpdates { get; private set; }

		public const string DialogAppendPrefix = "\u0001";

		public static bool IsDialogMenu(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return true;
			string key = text.Trim().ToLowerInvariant();
			return key is "commander" or "menu" or "orders";
		}

		/// <summary>True when this style's weapon requirement or spec line matches the item.</summary>
		public bool StyleMatchesWeapon(Style style) => StyleFits(style, ActiveWeapon);

		public static bool StyleFits(Style style, DbInventoryItem weapon)
		{
			if (style == null)
				return false;
			int requirement = style.WeaponTypeRequirement;
			if (requirement == Style.SpecialWeaponType.AnyWeapon)
				return weapon != null;
			if (requirement == Style.SpecialWeaponType.DualWield || weapon == null)
				return false;
			int have = weapon.Object_Type;
			if (have == (int)eObjectType.LeftAxe)
				have = (int)eObjectType.Axe;
			if (requirement == have)
				return true;
			string spec = style.Spec;
			if (string.IsNullOrEmpty(spec) || spec.Equals("None", StringComparison.OrdinalIgnoreCase))
				return false;
			string weaponSpec = SkillBase.ObjectTypeToSpec((eObjectType)have);
			return !string.IsNullOrEmpty(weaponSpec) && spec.Equals(weaponSpec, StringComparison.OrdinalIgnoreCase);
		}

		public List<Style> StylesForEquippedWeapon() => StylesMatching(AllCommanderStyles(), ActiveWeapon);

		public static List<Style> StylesMatching(IEnumerable<Style> styles, DbInventoryItem weapon)
		{
			var list = new List<Style>();
			if (styles == null)
				return list;
			var seen = new HashSet<int>();
			foreach (Style style in styles)
			{
				if (style == null || !seen.Add(style.ID))
					continue;
				if (StyleFits(style, weapon))
					list.Add(style);
			}
			list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			return list;
		}

		private IEnumerable<Style> AllCommanderStyles()
		{
			foreach (IEnumerable<Style> range in new IEnumerable<Style>[]
			{
				Styles, StylesAnytime, StylesFront, StylesSide, StylesBack, StylesDefensive, StylesChain,
			})
			{
				if (range == null)
					continue;
				foreach (Style style in range)
					yield return style;
			}
		}

		/// <summary>
		/// Styles the player can name as the commander's favorite: the equipped weapon's styles that
		/// open on their own or from a position. Chain and defensive styles need an opening the
		/// commander cannot wait for every swing, so they stay in its own rotation only.
		/// </summary>
		public List<Style> PickableStylesForEquippedWeapon()
			=> PickableStyles(StylesForEquippedWeapon());

		public static List<Style> PickableStyles(IEnumerable<Style> styles)
		{
			var list = new List<Style>();
			if (styles == null)
				return list;
			foreach (Style style in styles)
			{
				if (style == null || style.OpeningRequirementType == Style.eOpening.Defensive)
					continue;
				if (style.OpeningRequirementType == Style.eOpening.Offensive && style.OpeningRequirementValue > 0)
					continue;
				list.Add(style);
			}
			list.Sort((a, b) =>
			{
				int byLevel = a.SpecLevelRequirement.CompareTo(b.SpecLevelRequirement);
				return byLevel != 0 ? byLevel : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
			});
			return list;
		}

		/// <summary>The weapon the commander has out, as a dialog type (None when it holds nothing it can name).</summary>
		public eWeaponType EquippedWeaponType()
		{
			DbInventoryItem weapon = ActiveWeapon;
			if (weapon == null)
				return eWeaponType.None;
			bool twoHanded = ActiveWeaponSlot == eActiveWeaponSlot.TwoHanded;
			return (eObjectType)weapon.Object_Type switch
			{
				eObjectType.CompositeBow or eObjectType.Longbow or eObjectType.RecurvedBow or eObjectType.Fired => eWeaponType.Bow,
				eObjectType.Staff => eWeaponType.Staff,
				eObjectType.Sword => twoHanded ? eWeaponType.TwoHandSword : eWeaponType.OneHandSword,
				eObjectType.Axe or eObjectType.LeftAxe => twoHanded ? eWeaponType.TwoHandAxe : eWeaponType.OneHandAxe,
				eObjectType.Hammer => twoHanded ? eWeaponType.TwoHandHammer : eWeaponType.OneHandHammer,
				_ => eWeaponType.None,
			};
		}

		private static string WithArticle(string phrase, bool link)
		{
			string shown = link ? "[" + phrase + "]" : phrase;
			bool vowel = phrase.Length > 0 && "AEIOUaeiou".IndexOf(phrase[0]) >= 0;
			return (vowel ? "an " : "a ") + shown;
		}

		/// <summary>"a, b or c" (one item: "a"; two: "a or b").</summary>
		public static string JoinChoices(IList<string> items, string last = "or")
		{
			if (items == null || items.Count == 0)
				return string.Empty;
			if (items.Count == 1)
				return items[0];
			return string.Join(", ", items.Take(items.Count - 1)) + " " + last + " " + items[^1];
		}

		/// <summary>
		/// What the commander says when you speak to it: a line of speech for each order you can
		/// give, each with its [keywords]. The first line is the answer to what you just said
		/// (or its greeting), so the dialog reads as one exchange instead of a growing log.
		/// </summary>
		public string DialogMenu(string reply = null)
		{
			var lines = new List<string> { string.IsNullOrWhiteSpace(reply) ? "What are your orders, master?" : reply };

			var have = new HashSet<eWeaponType>(AvailableWeaponTypes());
			eWeaponType holding = EquippedWeaponType();
			var oneHand = new[] { eWeaponType.OneHandSword, eWeaponType.OneHandAxe, eWeaponType.OneHandHammer }
				.Where(have.Contains).Select(t => WithArticle(WeaponPhrase(t), true)).ToList();
			var twoHand = new[] { eWeaponType.TwoHandSword, eWeaponType.TwoHandAxe, eWeaponType.TwoHandHammer }
				.Where(have.Contains).Select(t => WithArticle(WeaponPhrase(t), true)).ToList();
			if (oneHand.Count > 0 || twoHand.Count > 0)
			{
				string weapons = holding == eWeaponType.None || holding == eWeaponType.Bow
					? "I can take up "
					: "I hold " + WithArticle(WeaponPhrase(holding), false) + ". I can take up ";
				if (oneHand.Count > 0 && twoHand.Count > 0)
					weapons += JoinChoices(oneHand) + ", or in both hands " + JoinChoices(twoHand) + ".";
				else
					weapons += JoinChoices(oneHand.Count > 0 ? oneHand : twoHand) + ".";
				if (have.Contains(eWeaponType.Bow))
					weapons += " I also carry a [Bow].";
				lines.Add(weapons);
			}
			else if (have.Contains(eWeaponType.Bow))
				lines.Add("I carry a [Bow].");

			List<Style> styles = PickableStylesForEquippedWeapon();
			if (holding != eWeaponType.None && holding != eWeaponType.Bow)
			{
				if (styles.Count == 0)
					lines.Add("I know no styles with this weapon yet.");
				else
				{
					string known = "With it I know " + JoinChoices(styles.Select(s => "[" + s.Name + "]").ToList(), "and") + ".";
					known += PreferredCombatStyle != null
						? " I favor " + PreferredCombatStyle.Name + ", or I can [choose] my own."
						: " Name one and I will favor it. For now I choose my own.";
					lines.Add(known);
				}
			}

			if (SubPetCapacity > 0)
			{
				lines.Add(MinionsAssisting
					? "My minions fight my target. Shall they [split] and pick their own?"
					: "My minions pick their own targets. Shall they [assist] me again?");
			}

			lines.Add(Taunting
				? "I am holding the enemy's eye. Shall I [stop taunting]?"
				: "Shall I [taunt] the enemy away from you?");
			return string.Join("\n", lines);
		}

		/// <summary>
		/// A clicked keyword. Null when the text is not one of the commander's orders (the caller
		/// leaves it for the normal whisper path).
		/// </summary>
		public string ReplyToKeyword(string text)
		{
			if (string.IsNullOrWhiteSpace(text) || IsDialogMenu(text))
				return null;
			string key = text.Trim().ToLowerInvariant();
			if (key == "assist")
			{
				SetMinionsAssisting(true);
				return "My minions will assist me.";
			}
			if (key == "split")
			{
				SetMinionsAssisting(false);
				return "My minions will split.";
			}
			if (key == "taunt")
			{
				SetTaunting(!Taunting);
				return Taunting ? "I will taunt." : "I will no longer taunt.";
			}
			if (key is "stop taunting" or "stop")
			{
				SetTaunting(false);
				return "I will no longer taunt.";
			}
			if (key is "auto" or "clear" or "choose")
			{
				SetPreferredCombatStyle(null);
				return "I will choose my own styles.";
			}
			if (TryParseWeaponType(text, out eWeaponType weapon))
			{
				if (!AvailableWeaponTypes().Contains(weapon))
					return "I cannot use a " + WeaponPhrase(weapon) + ".";
				if (!CommanderSwitchWeapon(weapon))
					return "I cannot take up a " + WeaponPhrase(weapon) + " right now.";
				return "I will wield the " + WeaponPhrase(weapon) + ".";
			}
			Style match = PickableStylesForEquippedWeapon().FirstOrDefault(style =>
				style.Name != null && style.Name.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
			if (match == null)
				return null;
			SetPreferredCombatStyle(match);
			return "I will use " + match.Name + ".";
		}

		public void ShowDialog(GamePlayer player, bool append)
		{
			if (player == null)
				return;
			Tell(player, DialogMenu(), append);
		}

		private void Tell(GamePlayer player, string text, bool append)
		{
			if (player?.Out == null || string.IsNullOrEmpty(text))
				return;
			player.Out.SendMessage((append ? DialogAppendPrefix : string.Empty) + text,
				eChatType.CT_Say, eChatLoc.CL_PopupWindow);
		}

		#region Level styles
		/// <summary>
		/// The Midgard classes whose sword, axe and hammer styles a commander learns, in order of
		/// preference. The Warrior trains all three lines; the rest only fill a line it lacks.
		/// </summary>
		private static readonly int[] StyleTeachers =
		{
			(int)eCharacterClass.Warrior, (int)eCharacterClass.Thane, (int)eCharacterClass.Skald,
			(int)eCharacterClass.Berserker, (int)eCharacterClass.Savage,
		};

		/// <summary>Where level styles come from. Tests replace it; the server reads the style table.</summary>
		public static Func<string, int, List<Style>> StyleSource = SkillBase.GetStyleList;

		/// <summary>
		/// Styles a commander of this level knows in each weapon line: every style of the line
		/// up to the commander's level, so a higher commander knows more of them. The npc template's
		/// own styles covered only some weapons (a hammer had none).
		/// </summary>
		public static List<Style> LevelStyles(int level, IEnumerable<string> specs, Func<string, int, List<Style>> stylesOf = null)
		{
			stylesOf ??= StyleSource;
			var result = new List<Style>();
			var seen = new HashSet<int>();
			if (specs == null || stylesOf == null)
				return result;
			foreach (string spec in specs.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				List<Style> line = null;
				foreach (int classId in StyleTeachers)
				{
					line = stylesOf(spec, classId);
					if (line != null && line.Count > 0)
						break;
				}
				if (line == null || line.Count == 0)
					line = stylesOf(spec, 0);
				if (line == null)
					continue;
				foreach (Style style in line.OrderBy(s => s.SpecLevelRequirement).ThenBy(s => s.ID))
				{
					if (style != null && style.SpecLevelRequirement <= level && seen.Add(style.ID))
						result.Add(style);
				}
			}
			return result;
		}

		/// <summary>The weapon lines this commander can hold.</summary>
		public IEnumerable<string> StyleSpecs()
		{
			var specs = new List<string>();
			var types = new List<eWeaponType>(AvailableWeaponTypes());
			eWeaponType holding = EquippedWeaponType();
			if (holding != eWeaponType.None)
				types.Add(holding);
			foreach (eWeaponType type in types)
			{
				string spec = type switch
				{
					eWeaponType.OneHandSword or eWeaponType.TwoHandSword => Specs.Sword,
					eWeaponType.OneHandAxe or eWeaponType.TwoHandAxe => Specs.Axe,
					eWeaponType.OneHandHammer or eWeaponType.TwoHandHammer => Specs.Hammer,
					_ => null,
				};
				if (spec != null && !specs.Contains(spec))
					specs.Add(spec);
			}
			return specs;
		}

		private HashSet<Style> _levelStyles = new(ReferenceEqualityComparer.Instance);
		private bool _commanderReady;

		/// <summary>
		/// Adds the level styles of every weapon this commander can hold to its styles, keeping
		/// the template's own. Runs at summon and whenever the commander's level changes.
		/// </summary>
		public void RefreshLevelStyles()
		{
			var kept = new List<Style>();
			var ids = new HashSet<int>();
			if (Styles != null)
			{
				foreach (Style style in Styles)
				{
					if (style == null || _levelStyles.Contains(style))
						continue;
					kept.Add(style);
					ids.Add(style.ID);
				}
			}

			List<Style> learned;
			try
			{
				learned = LevelStyles(Math.Max((int)Level, 1), StyleSpecs());
			}
			catch (Exception e)
			{
				log.Warn($"CommanderPet.RefreshLevelStyles(): could not read the style table for {Name}: {e.Message}");
				learned = new List<Style>();
			}

			var added = new HashSet<Style>(ReferenceEqualityComparer.Instance);
			foreach (Style style in learned)
			{
				if (ids.Add(style.ID))
				{
					kept.Add(style);
					added.Add(style);
				}
			}
			_levelStyles = added;
			Styles = kept;
			if (PreferredCombatStyle != null && !Styles.Any(s => s.ID == PreferredCombatStyle.ID))
				SetPreferredCombatStyle(null);
		}

		public override byte Level
		{
			get => base.Level;
			set
			{
				byte before = base.Level;
				base.Level = value;
				if (_commanderReady && base.Level != before)
					RefreshLevelStyles();
			}
		}
		#endregion

		/// <summary>
		/// Create a commander.
		/// </summary>
		/// <param name="npcTemplate"></param>
		/// <param name="owner"></param>
		public CommanderPet(INpcTemplate npcTemplate)
			: base(npcTemplate)
		{
			// Set the Commander Type
			CommanderType = ResolveCommanderType(npcTemplate, Name);
			if (CommanderType == eCommanderType.Unknown)
			{
				log.Warn($"CommanderPet(): unrecognized commander name {Name} in npcTemplate {npcTemplate.TemplateId}, is Name in server default language?");
			}

			// Classic/SI ranks own 0/1/2/3 slots. TetherRange is a generic NPC
			// field and must not turn a commander into a five-pet controller.
			int subPetSlots = CommanderType switch
			{
				eCommanderType.SkeletalCommander => 1,
				eCommanderType.BoneCommander => 2,
				eCommanderType.DreadCommander or
				eCommanderType.DreadArcher or
				eCommanderType.DreadLich or
				eCommanderType.DreadGuardian or
				eCommanderType.DreadLord => 3,
				eCommanderType.Unknown when npcTemplate.TetherRange > 0 => Math.Min(3, npcTemplate.TetherRange),
				_ => 0,
			};
			InitControlledBrainArray(subPetSlots);

			// The native BdPetBrain formation calculation uses the commander's
			// controlled-list index, producing one file behind the commander.
			Formation = eFormationType.Line;
			FormationSpacing = 1;
			if (CommanderType == eCommanderType.Unknown && subPetSlots == 0)
				log.Warn($"CommanderPet(): npcTemplate for unknown commander {Name} does not specify number of subpets, defaulting to 0");

			// Choose a melee weapon. CommanderSwitchWeapon keeps the offhand in
			// sync: a one-handed commander always has its square shield while a
			// two-handed commander always has an empty offhand.
			switch (CommanderType)
			{
				case eCommanderType.DreadGuardian:
				case eCommanderType.DreadLich:
					CommanderSwitchWeapon(eWeaponType.Staff, false);
					break;
				default:
					var choices = CommanderWeapons.Where(w => CanUseWeaponSlot(w.Type is eWeaponType.TwoHandSword or eWeaponType.TwoHandHammer
						? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard)).ToArray();
					if (choices.Length > 0)
					{
						var (type, model) = choices[Util.Random(choices.Length - 1)];
						CommanderSwitchWeapon(type, true, model);
					}
					break;
			}

			// Get a bow if we can use one
			if (CanUseWeaponSlot(eActiveWeaponSlot.Distance))
				CommanderSwitchWeapon(eWeaponType.Bow);

			_commanderReady = true;
			RefreshLevelStyles();
		}

		public override bool AddToWorld()
		{
			// Equipment templates and pet initialization can run in different
			// orders. Reassert the final visible loadout before the create packet
			// is sent so every client receives the shield in the initial update.
			EnsureCommanderOffhandMatchesMeleeWeapon();
			// The summon sets the level after the constructor; learn that level's styles.
			RefreshLevelStyles();
			return base.AddToWorld();
		}

		private static eCommanderType ResolveCommanderType(INpcTemplate template, string name)
		{
			// These are the stock Bonedancer commander templates.  Resolving by
			// template first avoids a localized/translated display name turning a
			// valid commander into an Unknown commander with zero minion slots.
			return template?.TemplateId switch
			{
				49 => eCommanderType.ReturnedCommander,
				50 => eCommanderType.SkeletalCommander,
				51 => eCommanderType.BoneCommander,
				52 => eCommanderType.DreadCommander,
				_ => ResolveCommanderTypeByName(name),
			};
		}

		private static eCommanderType ResolveCommanderTypeByName(string name)
		{
			string normalized = NormalizeCommanderName(name);
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.ReturnedCommander")) || normalized == "returnedcommander")
				return eCommanderType.ReturnedCommander;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.DecayedCommander")) || normalized == "decayedcommander")
				return eCommanderType.DecayedCommander;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.SkeletalCommander")) || normalized == "skeletalcommander")
				return eCommanderType.SkeletalCommander;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.BoneCommander")) || normalized == "bonecommander")
				return eCommanderType.BoneCommander;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.DreadCommander")) || normalized == "dreadcommander")
				return eCommanderType.DreadCommander;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.DreadArcher")) || normalized == "dreadarcher")
				return eCommanderType.DreadArcher;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.DreadLich")) || normalized == "dreadlich")
				return eCommanderType.DreadLich;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.DreadGuardian")) || normalized == "dreadguardian")
				return eCommanderType.DreadGuardian;
			if (normalized == NormalizeCommanderName(LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.DreadLord")) || normalized == "dreadlord")
				return eCommanderType.DreadLord;
			return eCommanderType.Unknown;
		}

		private static string NormalizeCommanderName(string value) =>
			new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

		/// <summary>
		/// Load BD pet weapon templates on first use. Deferred from a static ctor so pure
		/// helpers (TryParseWeaponType, enums) work in unit tests without GameServer.Database.
		/// </summary>
		private static void EnsureWeaponTemplates()
		{
			if (WEAPON_TEMPLATES.Count > 0)
				return;

			lock (WeaponTemplatesLock)
			{
				if (WEAPON_TEMPLATES.Count > 0)
					return;

				if (GameServer.Database == null)
					return;

				for (int i = (int)eWeaponType.MIN; i <= (int)eWeaponType.MAX; i++)
				{
					DbItemTemplate itemTemp = GameServer.Database.FindObjectByKey<DbItemTemplate>(WEAPON_KEYS[i]);

					if (itemTemp == null)
					{
						itemTemp = CreateBdPetWeapon((eWeaponType)i);

						if (itemTemp == null)
							log.Error($"Unable to find item template: {WEAPON_KEYS[i]}, could not create a new template.");
						else
							log.Info($"Unable to find item template: {WEAPON_KEYS[i]}, using default template.");
					}

					WEAPON_TEMPLATES.Add(itemTemp);
				}
			}
		}

		/// <summary>
		/// Called when owner sends a whisper to the pet
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="arguments"></param>
		/// <returns>True, if string needs further processing.</returns>
		public override bool WhisperReceive(GameLiving source, string str)
		{
			if (source is not GamePlayer player || player != Owner)
				return false;

			bool menu = IsDialogMenu(str);
			string reply = menu ? null : ReplyToKeyword(str);
			if (!menu && reply == null)
				return false;

			// One message: the answer on top and the orders under it, replacing what the dialog
			// showed (owner 2026-11-11: each click repeated the whole block below the last one).
			Tell(player, DialogMenu(reply), append: false);
			return true;
		}

		public override bool Interact(GamePlayer player)
		{
			if (player == null || Owner == null || player != Owner)
				return false;
			return WhisperReceive(player, "commander");
		}

		#region Inventory
		public enum eWeaponType
		{
			None = -1,
			MIN = 0,
			Bow = 0,
			OneHandAxe = 1,
			OneHandHammer = 2,
			OneHandSword = 3,
			TwoHandAxe = 4,
			TwoHandHammer = 5,
			TwoHandSword = 6,
			Staff = 7,
			Dagger = 8,
			CommanderShield = 9,
			GuardianBuckler = 10,
			MAX = 10
		}

		private static readonly string[] WEAPON_KEYS =
		{
			"BD_Archer_Distance_bow",
			"BD_Pet_1H_Axe",
			"BD_Pet_1H_Hammer",
			"BD_Pet_1H_Sword",
			"BD_Pet_2H_Axe",
			"BD_Pet_2H_Hammer",
			"BD_Pet_2H_Sword",
			"BD_Pet_Staff",
			"BD_Archer_Melee_Dagger",
			"BD_Commander_Square_Shield",
			"BD_Guardian_Round_Buckler"
		};

		private static readonly List<DbItemTemplate> WEAPON_TEMPLATES = new List<DbItemTemplate>((int)eWeaponType.MAX + 1);
		private static readonly object WeaponTemplatesLock = new();

		/// <summary>
		/// 1.65 Bonedancer pet weapons (owner 2026-10-13): plain Norse weapons from the 1.64 retail client, not the
		/// later bone items 3462-3469. A commander picks one of these nine at summon and carries no shield or helm.
		/// </summary>
		public static readonly (eWeaponType Type, ushort Model)[] CommanderWeapons =
		{
			(eWeaponType.TwoHandSword, 572), (eWeaponType.TwoHandHammer, 576),
			(eWeaponType.OneHandSword, 313), (eWeaponType.OneHandSword, 310), (eWeaponType.OneHandHammer, 322),
			(eWeaponType.OneHandAxe, 319), (eWeaponType.OneHandHammer, 320), (eWeaponType.OneHandAxe, 316),
			(eWeaponType.OneHandAxe, 578)
		};

		/// <summary>The model a pet's weapon of this type shows when no particular one was chosen (the minions').</summary>
		public static ushort PeriodModel(eWeaponType type) => type switch
		{
			eWeaponType.Bow => 564,           // Norse Composite bow (archers)
			eWeaponType.OneHandAxe => 578,    // Norse Hand Axe
			eWeaponType.OneHandHammer => 321, // norse hammer 1H (guardians)
			eWeaponType.OneHandSword => 311,  // Norse Short Sword (debuffers)
			eWeaponType.TwoHandAxe => 577,    // Norse 2h battleaxe
			eWeaponType.TwoHandHammer => 576, // norse 2h greathammer
			eWeaponType.TwoHandSword => 572,  // Norse Great Sword
			eWeaponType.Staff => 327,         // Norse staff (casters)
			eWeaponType.Dagger => 311,        // Norse Short Sword (archers' melee weapon)
			_ => 0
		};

		/// <summary>
		/// Get the item template for a BD pet
		/// </summary>
		public static DbItemTemplate GetWeaponTemplate(eWeaponType type)
		{
			if (type < eWeaponType.MIN || type > eWeaponType.MAX)
				return null;

			EnsureWeaponTemplates();
			if (WEAPON_TEMPLATES.Count <= (int)type)
				return null;

			return WEAPON_TEMPLATES[(int)type];
		}

		/// <summary>
		/// Create a new BD pet weapon using server default language to generate key and name.
		/// </summary>
		/// <param name="weaponType">Weapon key from GetWeaponKey()</param>
		/// <returns>New weapon item template, null if template could not be created</returns>
		public static DbItemTemplate CreateBdPetWeapon(eWeaponType weaponType)
		{
			DbItemTemplate temp = new DbItemTemplate();

			string weaponName;

			if (weaponType == eWeaponType.Bow)
			{
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "SkillBase.RegisterPropertyNames.ShortBow");
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				temp.Model = 3467;
				temp.Object_Type = (int)eObjectType.CompositeBow;
				temp.Type_Damage = (int)eDamageType.Thrust;
				temp.SPD_ABS = 45;
				temp.Item_Type = (int)eInventorySlot.DistanceWeapon;
				temp.Hand = (int)eActiveWeaponSlot.Distance;
			}
			else if (weaponType == eWeaponType.OneHandAxe)
			{
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.WR.Const.1HandedAxe");
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				temp.Model = 3469;
				temp.Object_Type = (int)eObjectType.Axe;
				temp.Type_Damage = (int)eDamageType.Slash;
				temp.SPD_ABS = 37;
				temp.Item_Type = (int)eInventorySlot.RightHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.Standard;
			}
			else if (weaponType == eWeaponType.OneHandHammer)
			{
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.WR.Const.1HandedHammer");
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				temp.Model = 3466;
				temp.Object_Type = (int)eObjectType.Hammer;
				temp.Type_Damage = (int)eDamageType.Crush;
				temp.SPD_ABS = 37;
				temp.Item_Type = (int)eInventorySlot.RightHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.Standard;
			}
			else if (weaponType == eWeaponType.OneHandSword)
			{
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.WR.Const.1HandedSword");
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				temp.Model = 3463;
				temp.Object_Type = (int)eObjectType.Sword;
				temp.Type_Damage = (int)eDamageType.Slash;
				temp.SPD_ABS = 34;
				temp.Item_Type = (int)eInventorySlot.RightHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.Standard;
			}
			else if (weaponType == eWeaponType.TwoHandAxe)
			{
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.WR.Const.2HandedAxe");
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				temp.Model = 3468;
				temp.Object_Type = (int)eObjectType.Axe;
				temp.Type_Damage = (int)eDamageType.Slash;
				temp.SPD_ABS = 50;
				temp.Item_Type = (int)eInventorySlot.TwoHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.TwoHanded;
			}
			else if (weaponType == eWeaponType.TwoHandHammer)
			{
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.WR.Const.2HandedHammer");
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				temp.Model = 3465;
				temp.Object_Type = (int)eObjectType.Hammer;
				temp.Type_Damage = (int)eDamageType.Crush;
				temp.SPD_ABS = 50;
				temp.Item_Type = (int)eInventorySlot.TwoHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.TwoHanded;
			}
			else if (weaponType == eWeaponType.TwoHandSword)
			{
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "GameObjects.CommanderPet.WR.Const.2HandedSword");
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				temp.Model = 3462;
				temp.Object_Type = (int)eObjectType.Sword;
				temp.Type_Damage = (int)eDamageType.Slash;
				temp.SPD_ABS = 45;
				temp.Item_Type = (int)eInventorySlot.TwoHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.TwoHanded;
			}
			else if (weaponType == eWeaponType.Staff)
			{
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				weaponName = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "SkillBase.RegisterPropertyNames.Staff");
				temp.Model = 3464;
				temp.Object_Type = (int)eObjectType.Staff;
				temp.Type_Damage = (int)eDamageType.Crush;
				temp.SPD_ABS = 50;
				temp.Item_Type = (int)eInventorySlot.TwoHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.TwoHanded;
			}
			else if (weaponType == eWeaponType.Dagger)
			{
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				weaponName = "bone dagger";
				temp.Model = 3463;
				temp.Object_Type = (int)eObjectType.Sword;
				temp.Type_Damage = (int)eDamageType.Thrust;
				temp.SPD_ABS = 24;
				temp.Item_Type = (int)eInventorySlot.RightHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.Standard;
			}
			else if (weaponType is eWeaponType.CommanderShield or eWeaponType.GuardianBuckler)
			{
				temp.Id_nb = WEAPON_KEYS[(int)weaponType];
				// 3460 is a campfire in the client, so the commander's shield never
				// showed. 3554 is the skull shield the Necromancer's reanimated servant carries.
				weaponName = weaponType == eWeaponType.CommanderShield ? "bone skull shield" : "bone round buckler";
				temp.Model = weaponType == eWeaponType.CommanderShield ? 3554 : 1045;
				temp.Object_Type = (int)eObjectType.Shield;
				temp.Type_Damage = 1;
				temp.SPD_ABS = 0;
				temp.Item_Type = (int)eInventorySlot.LeftHandWeapon;
				temp.Hand = (int)eActiveWeaponSlot.Standard;
			}
			else
				return null;

			string bonedancer = LanguageMgr.GetTranslation(LanguageMgr.DefaultLanguage, "PlayerClass.Name.Bonedancer");
			temp.Name = $"{bonedancer} {weaponName}";
			temp.Level = 1;
			temp.Quality = 100;
			temp.Condition = 50000;
			temp.MaxCondition = 50000;
			temp.Durability = 50000;
			temp.MaxDurability = 50000;
			temp.MaxCount = 1;
			temp.IsIndestructible = true;
			temp.IsNotLosingDur = true;
			temp.IsDropable = false;
			temp.IsPickable = false;

			return temp;
		}

		/// <summary>
		/// Can the pet swap this eActiveWeaponSlot?
		/// </summary>
		/// <param name="slot">Which slot to check</param>
		protected bool CanUseWeaponSlot(eActiveWeaponSlot slot)
		{
			switch (CommanderType)
			{
				case eCommanderType.ReturnedCommander:
				case eCommanderType.DecayedCommander:
					return slot == eActiveWeaponSlot.Standard || slot == eActiveWeaponSlot.TwoHanded;
				case eCommanderType.SkeletalCommander:
				case eCommanderType.BoneCommander:
				case eCommanderType.DreadCommander:
					return slot == eActiveWeaponSlot.Standard || slot == eActiveWeaponSlot.TwoHanded;
				case eCommanderType.DreadArcher:
					return slot == eActiveWeaponSlot.Standard || slot == eActiveWeaponSlot.Distance;
				case eCommanderType.DreadGuardian:
				case eCommanderType.DreadLich:
				case eCommanderType.DreadLord:
					// These pets can't change weapons at all
					return false;
				default:
					// Pets we don't recognize can swap weapons populated by their npcTemplate.EquipmentTemplateID
					if (Inventory != null)
						switch (slot)
						{
							case eActiveWeaponSlot.Distance:
								return Inventory.GetItem(eInventorySlot.DistanceWeapon) != null;
							case eActiveWeaponSlot.Standard:
								return Inventory.GetItem(eInventorySlot.RightHandWeapon) != null;
							case eActiveWeaponSlot.TwoHanded:
								return Inventory.GetItem(eInventorySlot.TwoHandWeapon) != null;
						}
					return false;
			}
		}

		/// <summary>
		/// Changes the commander's weapon to the specified weapon spec and slot
		/// </summary>
		/// <param name="slot">Weapon slot</param>
		/// <param name="weaponSpec">Weapon spec name</param>
		/// <returns></returns>
		public void CommanderSwitchWeapon(eActiveWeaponSlot slot, string weaponSpec)
		{
			if (!CanUseWeaponSlot(slot))
				return;

			if (slot == eActiveWeaponSlot.Distance)
				CommanderSwitchWeapon(eWeaponType.Bow);

			if (Owner is GamePlayer player)
			{
				string upperWeapon = weaponSpec.ToUpper();

				if (slot == eActiveWeaponSlot.Standard)
				{
					if (upperWeapon == LanguageMgr.GetTranslation(player.Client.Account.Language, "SkillBase.RegisterPropertyNames.Axe").ToUpper())
						CommanderSwitchWeapon(eWeaponType.OneHandAxe);

					if (upperWeapon == LanguageMgr.GetTranslation(player.Client.Account.Language, "SkillBase.RegisterPropertyNames.Hammer").ToUpper())
						CommanderSwitchWeapon(eWeaponType.OneHandHammer);

					if (upperWeapon == LanguageMgr.GetTranslation(player.Client.Account.Language, "SkillBase.RegisterPropertyNames.Sword").ToUpper())
						CommanderSwitchWeapon(eWeaponType.OneHandSword);
				}
				else
				{
					if (upperWeapon == LanguageMgr.GetTranslation(player.Client.Account.Language, "SkillBase.RegisterPropertyNames.Axe").ToUpper())
						CommanderSwitchWeapon(eWeaponType.TwoHandAxe);

					if (upperWeapon == LanguageMgr.GetTranslation(player.Client.Account.Language, "SkillBase.RegisterPropertyNames.Hammer").ToUpper())
						CommanderSwitchWeapon(eWeaponType.TwoHandHammer);

					if (upperWeapon == LanguageMgr.GetTranslation(player.Client.Account.Language, "SkillBase.RegisterPropertyNames.Sword").ToUpper())
						CommanderSwitchWeapon(eWeaponType.TwoHandSword);
				}
			}
		}

		/// <summary>
		/// Switch the commander to the specified weapon type
		/// </summary>
		/// <param name="checkCanUse">Make sure the commander can use this weapon type?</param>
		public bool CommanderSwitchWeapon(eWeaponType weaponType, bool checkCanUse = true, ushort model = 0)
		{
			DbItemTemplate itemTemp = GetWeaponTemplate(weaponType);

			if (itemTemp == null || (checkCanUse && !CanUseWeaponSlot((eActiveWeaponSlot)itemTemp.Hand)))
				return false;

			DbInventoryItem weapon;

			weapon = GameInventoryItem.Create(itemTemp);
			if (weapon != null)
			{
				ushort shown = model > 0 ? model : PeriodModel(weaponType);
				if (shown > 0)
					weapon.Model = shown;
				if (Inventory == null)
					Inventory = new GameNPCInventory(new GameNpcInventoryTemplate());
				else
				{
					if (itemTemp.Hand == (int)eActiveWeaponSlot.Distance)
						Inventory.RemoveItem(Inventory.GetItem(eInventorySlot.DistanceWeapon));
					else if (CommanderType == eCommanderType.Unknown)
					{
						// Only empty the slot we're about to fill, as we check the other weapon slots
						//	to determine which weapon types unknown commanders can switch to
						Inventory.RemoveItem(Inventory.GetItem((eInventorySlot)weapon.Item_Type));
					}
					else
					{
						// Remove melee weapons
						Inventory.RemoveItem(Inventory.GetItem(eInventorySlot.RightHandWeapon));
						Inventory.RemoveItem(Inventory.GetItem(eInventorySlot.TwoHandWeapon));
					}

				}

				Inventory.AddItem((eInventorySlot)weapon.Item_Type, weapon);

				if (weaponType is eWeaponType.OneHandAxe or eWeaponType.OneHandHammer or eWeaponType.OneHandSword or eWeaponType.Dagger)
					ClearCommanderOffhand(); // 1.65: a commander carries no shield
				else if (weaponType is eWeaponType.TwoHandAxe or eWeaponType.TwoHandHammer or eWeaponType.TwoHandSword or eWeaponType.Staff)
					ClearCommanderOffhand();

				// A 1h sword and a 1h axe stay in the standard slot. SwitchWeapon only broadcasts
				// when the slot changes, so the client kept the old model until a later click
				// landed on the other slot.
				eActiveWeaponSlot previousSlot = ActiveWeaponSlot;
				if (ActiveWeaponSlot == eActiveWeaponSlot.Distance)
				{
					if (ObjectState == eObjectState.Active)
						BroadcastLivingEquipmentUpdate();
				}
				else
					SwitchWeapon((eActiveWeaponSlot)weapon.Hand);

				if (NeedsExtraEquipmentBroadcast(previousSlot, ActiveWeaponSlot))
				{
					SameSlotEquipmentUpdates++;
					if (ObjectState == eObjectState.Active)
						BroadcastLivingEquipmentUpdate();
				}

				if (PreferredCombatStyle != null && !StyleMatchesWeapon(PreferredCombatStyle))
					SetPreferredCombatStyle(null);
				return true;
			}

			return false;
		}

		private void CommanderEquipOffhand(eWeaponType shieldType)
		{
			DbItemTemplate template = GetWeaponTemplate(shieldType);
			DbInventoryItem shield = template == null ? null : GameInventoryItem.Create(template);
			if (shield == null)
				return;

			Inventory ??= new GameNPCInventory(new GameNpcInventoryTemplate());
			Inventory.RemoveItem(Inventory.GetItem(eInventorySlot.LeftHandWeapon));
			Inventory.AddItem(eInventorySlot.LeftHandWeapon, shield);

			if (ObjectState == eObjectState.Active)
				BroadcastLivingEquipmentUpdate();
		}

		private void ClearCommanderOffhand()
		{
			if (Inventory?.GetItem(eInventorySlot.LeftHandWeapon) == null)
				return;

			Inventory.RemoveItem(Inventory.GetItem(eInventorySlot.LeftHandWeapon));
			if (ObjectState == eObjectState.Active)
				BroadcastLivingEquipmentUpdate();
		}

		private void EnsureCommanderOffhandMatchesMeleeWeapon()
		{
			if (CommanderType == eCommanderType.Unknown || Inventory == null)
				return;

			// 1.65: no shield with either weapon (and no helm: the commander templates carry none).
			ClearCommanderOffhand();
		}
		#endregion

		#region Spells
		// Dread Guardian & Lich spell lists
		public Spell CommSpellDamage { protected set; get; } = null;
		public Spell CommSpellDamageDebuff { protected set; get; } = null;
		public Spell CommSpellDebuff { protected set; get; } = null;
		public Spell CommSpellDot { protected set; get; } = null;
		public Spell CommSpellOther { protected set; get; } = null;

		public enum eCommanderPreferredSpell
		{
			Damage,
			Debuff,
			Other, // Lifedrain for guardian, dd+snare for lich
			None // No selected spell, use base CheckSpells()
		}

		// Both guardian and lich default to debuff spells
		public eCommanderPreferredSpell PreferredSpell { protected set; get; } = eCommanderPreferredSpell.None;
		/// <summary>
		/// Sort damaging spells into specialized lists for Guardians and Liches
		/// </summary>
		public override void SortSpells()
		{
			if (Spells.Count < 1)
				return;

			base.SortSpells();

			if (HarmfulSpells != null && HarmfulSpells.Count < 1)
				return;

			if (CommanderType != eCommanderType.DreadGuardian && CommanderType != eCommanderType.DreadLich)
				return;

			CommSpellDamage = null;
			CommSpellDamageDebuff = null;
			CommSpellDebuff = null;
			CommSpellOther = null;

			foreach (Spell spell in HarmfulSpells)
			{
				if (spell != null)
				{
					switch (spell.SpellType)
					{
                        case eSpellType.DamageOverTime:
							CommSpellDot = spell;
							break;
                        case eSpellType.DirectDamage:
							CommSpellDamage = spell;
							break;
                        case eSpellType.DirectDamageWithDebuff:
							CommSpellDamageDebuff = spell;
							break;
                        case eSpellType.Disease:
							CommSpellDebuff = spell;
							break;
                        case eSpellType.Lifedrain:
                        case eSpellType.DamageSpeedDecrease:
							CommSpellOther = spell;
							break;
					}
				}
			}

			// Remove selected spells so the base method doesn't have to deal with them
			if (CommSpellDamage != null)
				HarmfulSpells.Remove(CommSpellDamage);
			if (CommSpellDamageDebuff != null)
				HarmfulSpells.Remove(CommSpellDamageDebuff);
			if (CommSpellDebuff != null)
				HarmfulSpells.Remove(CommSpellDebuff);
			if (CommSpellDot != null)
				HarmfulSpells.Remove(CommSpellDot);
			if (CommSpellOther != null)
				HarmfulSpells.Remove(CommSpellOther);
			HarmfulSpells.TrimExcess();

			// Make sure we have a valid preferred spell
			switch (PreferredSpell)
			{
				case eCommanderPreferredSpell.Damage:
					if (CommSpellDamage == null && CommSpellDamageDebuff == null && CommSpellDot == null)
						PreferredSpell = eCommanderPreferredSpell.None;
					break;
				case eCommanderPreferredSpell.Debuff:
					if (CommSpellDebuff == null)
						PreferredSpell = eCommanderPreferredSpell.None;
					break;
				case eCommanderPreferredSpell.Other:
					if (CommSpellOther == null)
						PreferredSpell = eCommanderPreferredSpell.None;
					break;
			}

			// Pick a valid default preferred spell
			if (PreferredSpell == eCommanderPreferredSpell.None)
			{
				if (CommSpellDebuff != null)
					PreferredSpell = eCommanderPreferredSpell.Debuff;
				else if (CommSpellDamage != null || CommSpellDamageDebuff != null || CommSpellDot != null)
					PreferredSpell = eCommanderPreferredSpell.Damage;
				else if (CommSpellOther != null)
					PreferredSpell = eCommanderPreferredSpell.Other;
			}
		}
		#endregion

		/// <summary>
		/// Adds a pet to the current array of pets
		/// </summary>
		/// <param name="controlledBrain">The brain to add to the list</param>
		/// <returns>Whether the pet was added or not</returns>
		public override bool AddControlledBrain(IControlledBrain controlledBrain)
		{
			lock (ControlledNpcListLock)
			{
				if (ControlledNpcList == null)
					return false;

				bool foundSpot = false;

				for (int i = 0; i < ControlledNpcList.Length; i++)
				{
					if (ControlledNpcList[i] == null)
					{
						foundSpot = true;
						ControlledNpcList[i] = controlledBrain;

						if (Brain is IControlledBrain commanderBrain)
							controlledBrain.SetAggressionState(commanderBrain.AggressionState);

						UpdatePetCount(controlledBrain.Body as GameSummonedPet, true);
						break;
					}
				}

				return foundSpot;
			}
		}

		/// <summary>
		/// Removes the brain from
		/// </summary>
		/// <param name="controlledBrain">The brain to find and remove</param>
		/// <returns>Whether the pet was removed</returns>
		public override bool RemoveControlledBrain(IControlledBrain controlledBrain)
		{
			bool foundBrain = false;

			lock (ControlledNpcListLock)
			{
				if (controlledBrain == null)
					return false;

				for (int i = 0; i < ControlledNpcList.Length; i++)
				{
					if (ControlledNpcList[i] == controlledBrain)
					{
						foundBrain = true;

						if (controlledBrain is ControlledMobBrain controlledNpcBrain)
							controlledNpcBrain.StripCastedBuffs();

						ControlledNpcList[i] = null;
						break;
					}
				}
			}

			return foundBrain;
		}
	}
}

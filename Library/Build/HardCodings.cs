using System.Text.RegularExpressions;
using static Library.Common;

namespace Library
{
	// For multiple reasons, a small subset of data (or expected data) is hardcoded.
	// Naturally hardcoding things comes with risks and may require review after each patch. For that reason all hardcoded items are kept together here.
	// Hardcoding may be done for the following reasons: Datamining is not realistic, or a route is not known. Or the data is apparently server-side.
	// Notably Map Markers are the main offenders of this
	public static partial class BuildTools
	{
		public static string NoWaterEspPath { get; } = "mappalachia_noWater.esp";

		public static string DefaultTextureFormID { get; } = "0x000AB07E";

		public static Regex SpaceFormIDRegex { get; } = new Regex(@"\[(WRLD|CELL):([0-9A-F]{8})\]");

		public static Regex SignatureFormIDRegex { get; } = new Regex(@"\[[A-Z_]{4}:([0-9A-F]{8})\]");

		public static Regex FormIDRegex { get; } = new Regex(@"[0-9A-F]{8}");

		public static Regex RemoveTrailingReferenceRegex { get; } = new Regex("(.*) " + SignatureFormIDRegex);

		public static Regex QuotedTermRegex { get; } = new Regex(".* :QUOT:(.*):QUOT: " + SignatureFormIDRegex);

		public static Regex TitleCaseAddSpaceRegex { get; } = new Regex("(.*[a-z])([A-Z].*)");

		public static Regex LockLevelRegex { get; } = new Regex(@"(Novice(?= 1| 25)|Advanced|Expert|Master)");

		public static Regex ValidateLockLevel { get; } = new Regex("^(Novice|Advanced|Expert|Master|Inaccessible|RequiresKey|RequiresTerminal)$");

		public static Regex ValidatePrimitiveShape { get; } = new Regex("^(Box|Line|Plane|Sphere|Ellipsoid|Cylinder)$");

		public static Regex ValidateSignature { get; } = new Regex("^(ACTI|ALCH|AMMO|ARMO|ASPC|BNDS|BOOK|CONT|DOOR|FLOR|FURN|HAZD|IDLM|KEYM|LIGH|LVLI|MISC|MSTT|NOTE|NPC_|PROJ|SCOL|SOUN|STAT|TACT|TERM|TXST|WEAP)$");

		public static Regex ValidateComponent { get; } = new Regex("^(Acid|Adhesive|Aluminum|Antiseptic|Asbestos|Ballistic Fiber|Bone|Ceramic|Circuitry|Cloth|Concrete|Copper|Cork|Crystal|Fertilizer|Fiber Optics|Fiberglass|Gear|Glass|Gold|Lead|Leather|Nuclear Material|Oil|Plastic|Rubber|Screw|Silver|Spring|Steel|Wood)$");

		public static Regex ValidIconFolder { get; } = new Regex("DefineSprite_[0-9]{1,3}_(([A-Z].*Marker)|WhitespringResort|NukaColaQuantumPlant|TrainTrackMark)$");

		public static string MapMarkerIconInitialFileName { get; } = "1.svg"; // Frame 1

		// Defines if the worldspace should have a menu background image, and a watermask
		public static List<string> PrimaryWorldspaceEditorIds { get; } = new List<string>()
		{
			"Commonwealth", "DLC03FarHarbor", "NukaWorld",
		};

		public static readonly Dictionary<string, string> MarkerIconCorrection = new Dictionary<string, string>()
		{
			{ "GovtBuildingMonumentMarker", "MonumentMarker" },
			{ "MetroStationMarker", "MetroMarker" },
			{ "OfficeCivicBuildingMarker", "OfficeMarker" },
			{ "NaturalLandmarkMarker", "LandmarkMarker" },
			{ "RuinsUrbanMarker", "UrbanRuinsMarker" },
			{ "RuinsTownMarker", "TownRuinsMarker" },
			{ "FactoryIndustrialSiteMarker", "FactoryMarker" },
			{ "SewerUtilityTunnelsMarker", "SewerMarker" },
			{ "SanctuaryMarker", "SancHillsMarker" },
			{ "SwanPondMarker", "SwanPondMarker" },
			{ "BrownstoneTownhouseMarker", "BrownstoneMarker" },
			{ "LowRiseBuildingMarker", "LowRiseMarker" },
			{ "MechanistLairRaidersettlementVassalsettlementPotentialVassalsettlementMarker", "MechanistMarker" },
			{ "Custom66Marker", "RaiderSettlementMarker" },
			{ "Custom70Marker", "GalacticMarker" },
			{ "Custom73Marker", "MonorailMarker" },
			{ "Custom74Marker", "RidesMarker" },
			{ "Custom75Marker", "SafariMarker" },
			{ "Custom78Marker", "DisciplesMarker" },
			{ "Custom79Marker", "OperatorsMarker" },
			{ "Custom80Marker", "PackMarker" },
		};

		public static Dictionary<string, string> MarkerLabelCorrection { get; } = new Dictionary<string, string>()
		{
			{ "O�Neill Family Manufacturing", "O'Neill Family Manufacturing" },
		};

		public static List<string> MapMarkersToRemove { get; } = new List<string>()
		{
			"ERROR: <<NO REF LOCATION>>",
		};

		// Spaces which appear to be copy/pastes or alternates of the same thing, and therefore can and should share scaling/adjustments
		public static List<List<string>> SisterSpaces { get; } = new List<List<string>>()
		{
			new List<string>() { "ElevTransHiTech", "ElevTransHiTechInstitute" },
			new List<string>() { "ElevMinUseTransUtil", "ElevTransUtil" },
		};

		// Provides the WHERE clause for a query which defines the rules of which cells we should discard, as they are understood to be cut or otherwise inaccessible.
		public static string DiscardCellsQuery { get; } =
			"spaceDisplayName = '' OR " +
			"spaceDisplayName LIKE '%Test%World%' OR " +
			"spaceDisplayName LIKE '%Test%Cell%' OR " +
			"spaceEditorID LIKE 'zUnused%' OR " +
			"spaceEditorID LIKE 'zCUT%' OR " +
			"spaceEditorID LIKE 'zz%' OR " +
			"spaceEditorID LIKE '%OLD' OR " +
			"spaceEditorID LIKE 'Warehouse%' OR " +
			"spaceEditorID LIKE 'DLC04Warehouse%' OR " +
			"spaceEditorID LIKE 'Test%' OR " +
			"spaceEditorID LIKE '%Debug%' OR " +
			"spaceEditorID LIKE '%Nav%Test%' OR " +
			"spaceEditorID LIKE 'PackIn%' OR " +
			"spaceEditorID LIKE 'COPY%' OR " +
			"spaceEditorID LIKE '%AliasCell' OR " +
			"spaceEditorID LIKE '%HoldingCell' OR " +
			"spaceEditorID = 'SanctuaryRosaHouse' OR " +
			"spaceEditorID = 'DiamondCityFX' OR " +
			"spaceEditorID = 'QASmoke' OR " +
			"spaceEditorID = 'PrewarTVStudio' OR " +
			"spaceEditorID = 'REObject02Interior' OR " +
			"spaceEditorID = 'aaaMarkers' OR " +
			"spaceEditorID = 'ThicketExcavations01' OR " +
			"spaceEditorID = 'Vault111Start' OR " +
			"spaceEditorID = 'ccTOSFO4001'";

		// Operates against already-finalized labels
		public static string? GetCorrectedMarkerIcon(string markerName)
		{
			switch (markerName)
			{
				default:
					return null;
			}
		}

		// Values passed with the -xm argument to the render command
		public static List<string> RenderExcludeModels { get; } = new List<string>()
		{ };

		// The Form ID of NorthMarker
		public static uint NorthMarkerFormID { get; } = HexToInt("00000003");

		// Some spaces have 2 North Markers - we specify which one we want to pick to give the north angle
		// Key = space FormID in Hex, Value = northMarker instance FormID in Hex
		public static Dictionary<string, string> NorthMarkerPreference { get; } = new Dictionary<string, string>()
		{ };

		// Format the map marker icon name into one more consistent with the SWF file structure
		public static string CleanupMapMarkerIconNames(string label)
		{
			return label
			.Replace(" ", string.Empty)
			.Replace("-", string.Empty)
			.Replace("/", string.Empty)
			.Replace("'", string.Empty) + "Marker";
		}

		// Returns the known world border(s) of the given space
		// Empty list if not known or doesn't exist
		public static async Task<List<Region>> GetWorldBorders(this Space space)
		{
			switch (space.EditorID.ToUpper())
			{
				case "COMMONWEALTH":
				case "NUKAWORLD":
					return await CommonDatabase.GetRegionsByLikeTerm(GetNewConnection(), space, $"'%Border%'");

				case "DLC03FARHARBOR":
					return await CommonDatabase.GetRegionsByLikeTerm(GetNewConnection(), space, $"'DLC03BorderRegion'");

				default:
					return new List<Region>();
			}
		}

		// List of Globals (GLOB) by EditorID which we should keep
		public static List<string> GlobalsToKeep { get; } = new List<string>()
		{ };
	}
}

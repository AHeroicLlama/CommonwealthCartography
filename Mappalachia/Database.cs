using System.Text.RegularExpressions;
using Library;
using Microsoft.Data.Sqlite;
using static Library.Common;
using static Library.CommonDatabase;

namespace Mappalachia
{
	static class Database
	{
		public static SqliteConnection Connection { get; private set; } = null!;

		public static List<Space> AllSpaces { get; private set; } = null!;

		static List<MapMarker>? AllMapMarkers { get; set; }

		static Regex SubstituteScrap { get; } = new Regex("derived|scrap", RegexOptions.IgnoreCase);

		static char EscapeChar { get; } = '`';

		static float LookupFuzzyOptimizationClampRange { get; } = 65536; // 2^16

		public static async Task Initialize()
		{
			Connection = GetNewConnection(Paths.DatabasePath);
			AllSpaces = await GetSpaces(Connection, "SELECT * FROM Space ORDER BY isWorldspace DESC, spaceDisplayName ASC");
		}

		// The core database search function - returns a collection of GroupedInstance from the given search params
		public static async Task<List<GroupedSearchResult>> Search(Settings settings)
		{
			string searchTerm = ProcessSearchString(settings.SearchSettings.SearchTerm);
			string allSpacesClause = settings.SearchSettings.SearchInAllSpaces ? string.Empty : $"AND spaceFormID = {settings.Space.FormID} ";
			string initiallyDisabledClause = settings.SearchSettings.IncludeInitiallyDisabled ? string.Empty : "AND initiallyDisabled != 1 ";
			string optionalClause = allSpacesClause + initiallyDisabledClause;
			bool searchForFormID = searchTerm.IsHexFormID() && settings.SearchSettings.Advanced;

			List<GroupedSearchResult> results = new List<GroupedSearchResult>();

			results.AddRange(await StandardSearch(settings, searchTerm, optionalClause, searchForFormID));
			results.AddRange(await ContainerSearch(settings, searchTerm, optionalClause, searchForFormID));
			results.AddRange(await ScrapSearch(settings, searchTerm, optionalClause));
			results.AddRange(await RegionSearch(settings, searchTerm, allSpacesClause, searchForFormID));
			results.AddRange(await LocationSearch(settings, searchTerm, allSpacesClause, searchForFormID));
			results.AddRange(await TeleportsToSearch(settings, searchTerm, allSpacesClause, searchForFormID));
			results.AddRange(await InstanceSearch(searchTerm, searchForFormID));

			// We use LINQ to filter out results here, to avoid joining on the Space table in each search function
			if (settings.SearchSettings.SearchInInstancesOnly)
			{
				return results.Where(
					r => r is SingularSearchResult ||
					r.Space.IsInstanceable)
					.ToList();
			}

			return results;
		}

		static async Task<List<GroupedSearchResult>> StandardSearch(Settings settings, string searchTerm, string optionalClause, bool searchForFormID)
		{
			List<GroupedSearchResult> results = new List<GroupedSearchResult>();

			string query = "SELECT referenceFormID, editorID, displayName, signature, spaceFormID, count, label, lockLevel, boundX1, boundY1, boundZ1, boundX2, boundY2, boundZ2, initiallyDisabled " +
				"FROM Position_PreGrouped " +
				"JOIN Entity ON Entity.entityFormID = Position_PreGrouped.referenceFormID " +
				$"WHERE (label LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' " +
				$"OR editorID LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' " +
				$"OR displayName LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' " +
				$"{(searchForFormID ? $"OR referenceFormID = '{HexToInt(searchTerm)}'" : string.Empty)}) " +
				optionalClause +
				$"AND Position_PreGrouped.lockLevel IN {settings.SearchSettings.SelectedLockLevels.Select(l => l.ToStringForQuery()).ToSqliteCollection()} " +
				$"AND Entity.signature IN {settings.SearchSettings.SelectedSignatures.ToSqliteCollection()};";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				results.Add(new GroupedSearchResult(
					new Entity(
						reader.GetUInt("referenceFormID"),
						reader.GetString("editorID"),
						reader.GetString("displayName"),
						reader.GetSignature(),
						reader.GetBounds()),
					GetSpaceByFormID(reader.GetUInt("spaceFormID")),
					reader.GetInt("count"),
					1,
					reader.GetString("label"),
					reader.GetLockLevel(),
					false,
					reader.GetBool("initiallyDisabled")));
			}

			return results;
		}

		static async Task<List<GroupedSearchResult>> ContainerSearch(Settings settings, string searchTerm, string optionalClause, bool searchForFormID)
		{
			List<GroupedSearchResult> results = new List<GroupedSearchResult>();

			string query = "SELECT contentFormID, editorID, displayName, signature, spaceFormID, SUM(count) as count, lockLevel, quantity, initiallyDisabled " +
				"FROM Container " +
				"JOIN Entity ON Container.contentFormID = Entity.entityFormID " +
				"JOIN Position_PreGrouped ON Position_PreGrouped.referenceFormID = Container.containerFormID " +
				$"WHERE (label LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' OR editorID LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' OR displayName LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' {(searchForFormID ? $"OR contentFormID = '{HexToInt(searchTerm)}'" : string.Empty)}) " +
				optionalClause +
				$"AND Position_PreGrouped.lockLevel IN {settings.SearchSettings.SelectedLockLevels.Select(l => l.ToStringForQuery()).ToSqliteCollection()} " +
				$"AND Entity.signature IN {settings.SearchSettings.SelectedSignatures.ToSqliteCollection()} " +
				$"GROUP BY editorID, contentFormID, lockLevel, quantity, spaceFormID;";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				results.Add(new GroupedSearchResult(
					new Entity(
						reader.GetUInt("contentFormID"),
						reader.GetString("editorID"),
						reader.GetString("displayName"),
						reader.GetSignature()),
					GetSpaceByFormID(reader.GetUInt("spaceFormID")),
					reader.GetInt("count"),
					reader.GetInt("quantity"),
					string.Empty,
					reader.GetLockLevel(),
					true,
					reader.GetBool("initiallyDisabled")));
			}

			return results;
		}

		static async Task<List<GroupedSearchResult>> ScrapSearch(Settings settings, string searchTerm, string optionalClause)
		{
			List<GroupedSearchResult> results = new List<GroupedSearchResult>();

			if (!settings.SearchSettings.ShouldSearchForScrap())
			{
				return results;
			}

			string substituteSearch = SubstituteScrap.Replace(searchTerm, string.Empty).Trim();

			// Note: We are not looking inside of containers here
			string query = "SELECT component, spaceFormID, componentQuantity, SUM(count) AS properCount, initiallyDisabled " +
				"FROM Scrap " +
				"JOIN Position_PreGrouped ON Position_PreGrouped.referenceFormID = Scrap.junkFormID " +
				$"WHERE (component LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' " +
				$"OR component LIKE '%{substituteSearch}%' ESCAPE '{EscapeChar}') " +
				optionalClause +
				"GROUP BY component, spaceFormID, componentQuantity;";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				results.Add(new GroupedSearchResult(
					new DerivedScrap(reader.GetString("component")),
					GetSpaceByFormID(reader.GetUInt("spaceFormID")),
					reader.GetInt("properCount"),
					reader.GetDouble("componentQuantity"),
					string.Empty,
					LockLevel.None,
					reader.GetBool("initiallyDisabled")));
			}

			return results;
		}

		static async Task<List<GroupedSearchResult>> RegionSearch(Settings settings, string searchTerm, string optionalClause, bool searchForFormID)
		{
			List<GroupedSearchResult> results = new List<GroupedSearchResult>();

			if (!settings.SearchSettings.ShouldSearchForRegion())
			{
				return results;
			}

			string query =
				"SELECT regionFormID, regionEditorID, spaceFormID " +
				"FROM Region " +
				$"WHERE (regionEditorID LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' " +
				$"OR regionFormID = '{searchTerm}' " +
				$"{(searchForFormID ? $"OR regionFormID = '{HexToInt(searchTerm)}'" : string.Empty)}) " +
				optionalClause +
				"GROUP BY regionFormID;";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				Space space = GetSpaceByFormID(reader.GetUInt("spaceFormID"));

				results.Add(new GroupedSearchResult(
					new Library.Region(
						reader.GetUInt("regionFormID"),
						reader.GetString("regionEditorID"),
						space),
					space));
			}

			return results;
		}

		static async Task<List<GroupedSearchResult>> LocationSearch(Settings settings, string searchTerm, string optionalClause, bool searchForFormID)
		{
			List<GroupedSearchResult> results = new List<GroupedSearchResult>();

			if (!settings.SearchSettings.ShouldSearchForLocation())
			{
				return results;
			}

			string query =
				"SELECT locationFormID, locationEditorID, locationDisplayName, spaceFormID " +
				"FROM Location " +
				$"WHERE (locationEditorID LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' " +
				$"OR locationDisplayName LIKE '%{searchTerm}%' ESCAPE '{EscapeChar}' " +
				$"OR locationFormID = '{searchTerm}' " +
				$"{(searchForFormID ? $"OR locationFormID = '{HexToInt(searchTerm)}'" : string.Empty)}) " +
				optionalClause +
				"GROUP BY locationFormID;";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				Space space = GetSpaceByFormID(reader.GetUInt("spaceFormID"));

				results.Add(new GroupedSearchResult(
					new Location(
						reader.GetUInt("locationFormID"),
						reader.GetString("locationEditorID"),
						reader.GetString("locationDisplayName"),
						space),
					space));
			}

			return results;
		}

		static async Task<List<GroupedSearchResult>> TeleportsToSearch(Settings settings, string searchTerm, string optionalClause, bool searchForFormID)
		{
			List<GroupedSearchResult> results = new List<GroupedSearchResult>();

			if (!searchForFormID)
			{
				return results;
			}

			string query =
				"SELECT referenceFormID, editorID, displayName, signature, spaceFormID, label, lockLevel, count(*) AS count " +
				"FROM Position " +
				"JOIN Entity ON Entity.entityFormID = Position.referenceFormID " +
				$"WHERE ((teleportsToFormID = '{HexToInt(searchTerm)}') " +
				optionalClause +
				$"AND lockLevel IN {settings.SearchSettings.SelectedLockLevels.Select(l => l.ToStringForQuery()).ToSqliteCollection()} " +
				$"AND Entity.signature IN {settings.SearchSettings.SelectedSignatures.ToSqliteCollection()}) " +
				"GROUP BY spaceFormID, Position.referenceFormID, teleportsToFormID, lockLevel, label;";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				results.Add(new GroupedSearchResult(
					new Entity(
						reader.GetUInt("referenceFormID"),
						reader.GetString("editorID"),
						reader.GetString("displayName"),
						reader.GetSignature()),
					GetSpaceByFormID(reader.GetUInt("spaceFormID")),
					reader.GetInt("count"),
					1,
					reader.GetString("label"),
					reader.GetLockLevel()));
			}

			return results;
		}

		static async Task<List<SingularSearchResult>> InstanceSearch(string searchTerm, bool searchForFormID)
		{
			List<SingularSearchResult> results = new List<SingularSearchResult>();

			if (!searchForFormID)
			{
				return results;
			}

			// We intentionally ignore search filters, because the user is searching for a specific FormID
			string query =
				"SELECT instanceFormID, referenceFormID, editorID, displayName, signature, spaceFormID, label, lockLevel, boundX1, boundY1, boundZ1, boundX2, boundY2, boundZ2, initiallyDisabled " +
				"FROM Position " +
				"JOIN Entity ON Entity.entityFormID = Position.referenceFormID " +
				$"WHERE instanceFormID = '{HexToInt(searchTerm)}';";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				results.Add(new SingularSearchResult(
					reader.GetUInt("instanceFormID"),
					new Entity(
						reader.GetUInt("referenceFormID"),
						reader.GetString("editorID"),
						reader.GetString("displayName"),
						reader.GetSignature(),
						reader.GetBounds()),
					GetSpaceByFormID(reader.GetUInt("spaceFormID")),
					reader.GetString("label"),
					reader.GetLockLevel(),
					reader.GetBool("initiallyDisabled")));
			}

			return results;
		}

		// Returns all Instances of the given GroupedSearchResult
		public static async Task<List<Instance>> GetInstances(GroupedSearchResult searchResult, Space? space = null)
		{
			space ??= searchResult.Space;

			List<Instance> instances = new List<Instance>();

			if (searchResult is SingularSearchResult singular)
			{
				Instance? instance = await GetSingularInstance(singular, space);

				if (instance is not null)
				{
					instances.Add(instance);
				}
			}
			else if (searchResult.InContainer)
			{
				instances.AddRange(await GetInContainerInstances(searchResult, space));
			}
			else if (searchResult.Entity is Library.Region)
			{
				Instance? instance = await GetRegionInstance(searchResult, space);

				if (instance is not null)
				{
					instances.Add(instance);
				}
			}
			else if (searchResult.Entity is Location)
			{
				instances.AddRange(await GetLocationInstance(searchResult, space));
			}
			else if (searchResult.Entity.GetType() == typeof(Entity))
			{
				instances.AddRange(await GetStandardInstances(searchResult, space));
			}
			else if (searchResult.Entity is DerivedScrap)
			{
				instances.AddRange(await GetScrapInstances(searchResult, space));
			}

			return instances;
		}

		// Returns all instances in the given space which teleport to another space
		public static async Task<List<Instance>> GetTeleporters(Space space)
		{
			List<Instance> teleporters = new List<Instance>();

			string query = "SELECT x, y, z, instanceFormID, teleportsToFormID, primitiveShape, boundX, boundY, boundZ, rotX, rotY, rotZ, " +
				"referenceFormID, editorID, displayName, signature FROM Position " +
				$"JOIN Entity ON Entity.entityFormID = Position.referenceFormID " +
				$"WHERE spaceFormID = {space.FormID} AND teleportsToFormID IS NOT NULL AND teleportsToFormID != {space.FormID}";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				teleporters.Add(new Instance(
					new Entity(
						reader.GetUInt("referenceFormID"),
						reader.GetString("editorID"),
						reader.GetString("displayName"),
						reader.GetSignature()),
					space,
					reader.GetCoord(),
					reader.GetUInt("instanceFormID"),
					string.Empty,
					GetSpaceByFormID(reader.GetUInt("teleportsToFormID")),
					LockLevel.None,
					reader.GetShape(),
					1,
					false,
					reader.GetRotation()));
			}

			return teleporters;
		}

		// Return all instances within range of the coord
		// Fuzzy generously includes entities whose bounds *may* overlap with the range, regardless of their rotation however
		public static async Task<List<Instance>> GetInstancesInRange(Coord coord, double range, Settings settings)
		{
			bool fuzzy = settings.MapSettings.LookupFuzzySearch;

			List<Instance> instances = new List<Instance>();

			double xBoundMin = coord.X - range;
			double yBoundMin = coord.Y - range;
			double xBoundMax = coord.X + range;
			double yBoundMax = coord.Y + range;

			if (fuzzy)
			{
				xBoundMin -= LookupFuzzyOptimizationClampRange;
				yBoundMin -= LookupFuzzyOptimizationClampRange;
				xBoundMax += LookupFuzzyOptimizationClampRange;
				yBoundMax += LookupFuzzyOptimizationClampRange;
			}

			string boundsRangeClause = fuzzy ? "+ (SQRT(((boundX2 - boundX1) * (boundX2 - boundX1)) + ((boundY2 - boundY1) * (boundY2 - boundY1))) / 2) " : string.Empty;

			string query = $"SELECT x, y, z, signature, entityFormID, editorID, displayName, label, instanceFormID, lockLevel, teleportsToFormID, primitiveShape, " +
				$"boundX, boundY, boundZ, rotX, rotY, rotZ, boundX1, boundY1, boundZ1, boundX2, boundY2, boundZ2, " +
				$"SQRT(((x - {coord.X}) * (x - {coord.X})) + ((y - {coord.Y}) * (y - {coord.Y}))) as distance " +
				$"FROM Position " +
				$"JOIN Entity ON Entity.entityFormID = Position.referenceFormID " +
				$"WHERE spaceFormID = {settings.Space.FormID} AND " +
				$"signature IN {settings.SearchSettings.SelectedSignatures.ToSqliteCollection()} AND " +
				$"x BETWEEN {xBoundMin} AND {xBoundMax} AND " +
				$"y BETWEEN {yBoundMin} AND {yBoundMax} AND " +
				$"distance <= {range} " + boundsRangeClause +
				$"ORDER BY distance, z;";

			using SqliteDataReader reader = await GetReader(Connection, query);

			Console.WriteLine(query);

			while (reader.Read())
			{
				instances.Add(new Instance(
					new Entity(
						reader.GetUInt("entityFormID"),
						reader.GetString("editorID"),
						reader.GetString("displayName"),
						reader.GetSignature(),
						reader.GetBounds()),
					settings.Space,
					reader.GetCoord(),
					reader.GetUInt("instanceFormID"),
					reader.GetString("label"),
					GetSpaceByFormID(reader.GetUInt("teleportsToFormID")),
					reader.GetLockLevel(),
					reader.GetShape(),
					1,
					false,
					reader.GetRotation()));
			}

			return instances;
		}

		// Return all 'standard' instances of the given GroupedSearchResult in the given space
		static async Task<List<Instance>> GetStandardInstances(GroupedSearchResult searchResult, Space space)
		{
			List<Instance> instances = new List<Instance>();

			string query = "SELECT x, y, z, instanceFormID, teleportsToFormID, primitiveShape, boundX, boundY, boundZ, rotX, rotY, rotZ FROM Position " +
				$"WHERE referenceFormID = {searchResult.Entity.FormID} AND spaceFormID = {space.FormID} AND lockLevel = '{searchResult.LockLevel.ToStringForQuery()}' AND label = '{searchResult.Label}' AND initiallyDisabled = {searchResult.InitiallyDisabled}";

			using SqliteDataReader reader = await GetReader(Connection, query);

			while (reader.Read())
			{
				instances.Add(new Instance(
					searchResult.Entity,
					space,
					reader.GetCoord(),
					reader.GetUInt("instanceFormID"),
					searchResult.Label,
					GetSpaceByFormID(reader.GetUInt("teleportsToFormID")),
					searchResult.LockLevel,
					reader.GetShape(),
					1,
					false,
					reader.GetRotation()));
			}

			return instances;
		}

		// Returns the instance of a SingularSearchResult
		static async Task<Instance?> GetSingularInstance(SingularSearchResult searchResult, Space space)
		{
			Instance? instance = null;

			// Intentionally only filterting by instance formID and not other properties, as this is an intentional reference to a specific instance
			string query = "SELECT x, y, z, teleportsToFormID, primitiveShape, boundX, boundY, boundZ, rotX, rotY, rotZ, spaceFormID FROM Position " +
				$"WHERE instanceFormID = {searchResult.InstanceFormID} AND spaceFormID = {space.FormID}";

			using SqliteDataReader reader = await GetReader(Connection, query);

			if (reader.Read())
			{
				instance = new Instance(
					searchResult.Entity,
					space,
					reader.GetCoord(),
					searchResult.InstanceFormID,
					searchResult.Label,
					GetSpaceByFormID(reader.GetUInt("teleportsToFormID")),
					searchResult.LockLevel,
					reader.GetShape(),
					1,
					false,
					reader.GetRotation());
			}

			return instance;
		}

		// Returns the instances of a GroupedSearchResult which is inContainer
		static async Task<List<Instance>> GetInContainerInstances(GroupedSearchResult searchResult, Space space)
		{
			List<Instance> instances = new List<Instance>();

			string query = "SELECT x, y, z, instanceFormID FROM Position " +
				"JOIN Container ON Container.containerFormID = Position.referenceFormID " +
				$"WHERE contentFormID = {searchResult.Entity.FormID} AND quantity = $spawnWeight AND lockLevel = '{searchResult.LockLevel.ToStringForQuery()}' AND spaceFormID = {space.FormID} AND initiallyDisabled = {searchResult.InitiallyDisabled};";

			Dictionary<string, object> parameters = new Dictionary<string, object>
			{
				{ "$spawnWeight", searchResult.SpawnWeight },
			};

			using SqliteDataReader reader = await GetReader(Connection, query, parameters);

			while (reader.Read())
			{
				instances.Add(new Instance(
					searchResult.Entity,
					space,
					reader.GetCoord(),
					reader.GetUInt("instanceFormID"),
					searchResult.Label,
					null,
					searchResult.LockLevel,
					null,
					searchResult.SpawnWeight,
					true));
			}

			return instances;
		}

		// Return the instance of the Location, with Cells populated
		static async Task<List<Instance>> GetLocationInstance(GroupedSearchResult searchResult, Space space)
		{
			string query = "SELECT x, y FROM Cell " +
				$"WHERE locationFormId = '{searchResult.Entity.FormID}' AND spaceFormID = {space.FormID};";

			using SqliteDataReader reader = await GetReader(Connection, query);

			Location location = new Location(
				searchResult.Entity.FormID,
				searchResult.Entity.EditorID,
				searchResult.Entity.DisplayName,
				space);

			while (reader.Read())
			{
				Cell cell = new Cell(
					location,
					reader.GetInt("x"),
					reader.GetInt("y"));

				location.AddCell(cell);
			}

			if (location.Cells.Count == 0)
			{
				// If there are no cells, location probably not at this space
				return new List<Instance>();
			}

			Instance instance = new Instance(
				location,
				space,
				location.Cells.Select(c => c.Coord).GetCentroid(),
				0,
				string.Empty,
				null,
				LockLevel.None,
				null);

			return new List<Instance> { instance };
		}

		// Returns the instance of a GroupedSearchResult which is a Region
		static async Task<Instance?> GetRegionInstance(GroupedSearchResult searchResult, Space space)
		{
			return await GetRegion(searchResult.Entity.EditorID, space);
		}

		// Return the region with the given EditorID from the given Space
		static async Task<Instance?> GetRegion(string editorID, Space space)
		{
			string regionQuery = "SELECT minLevel, maxLevel, regionFormID FROM Region " +
				$"WHERE regionEditorID = '{editorID}' AND spaceFormID = {space.FormID};";

			using SqliteDataReader regionReader = await GetReader(Connection, regionQuery);

			if (!regionReader.Read())
			{
				return null;
			}

			Library.Region region = new Library.Region(
				regionReader.GetUInt("regionFormID"),
				editorID,
				space,
				regionReader.GetUInt("minLevel"),
				regionReader.GetUInt("maxLevel"));

			string pointQuery = "SELECT x, y, subRegionIndex, coordIndex FROM RegionPoints " +
				$"WHERE regionFormID = {region.FormID};";

			using SqliteDataReader pointReader = await GetReader(Connection, pointQuery);

			while (pointReader.Read())
			{
				region.AddPoint(
					new RegionPoint(
						region,
						new Coord(
							pointReader.GetDouble("x"),
							pointReader.GetDouble("y")),
						pointReader.GetUInt("subRegionIndex"),
						pointReader.GetUInt("coordIndex")));
			}

			Instance instance = new Instance(
				region,
				space,
				region.GetAllSubRegions().First().GetCentroid(),
				0,
				string.Empty,
				null,
				LockLevel.None,
				null);

			return instance;
		}

		// Returns the instances of a GroupedSearchResult which is scrap
		static async Task<List<Instance>> GetScrapInstances(GroupedSearchResult searchResult, Space space)
		{
			List<Instance> instances = new List<Instance>();

			string query = "SELECT x, y, z, Position.instanceFormID FROM Position " +
				"JOIN Scrap ON Scrap.junkFormID = Position.referenceFormID " +
				$"WHERE component = '{searchResult.Entity.EditorID}' AND Scrap.componentQuantity = $spawnWeight AND Position.spaceFormID = {space.FormID} AND initiallyDisabled = {searchResult.InitiallyDisabled}";

			Dictionary<string, object> parameters = new Dictionary<string, object>
			{
				{ "$spawnWeight", searchResult.SpawnWeight },
			};

			using SqliteDataReader reader = await GetReader(Connection, query, parameters);

			while (reader.Read())
			{
				instances.Add(new Instance(
					searchResult.Entity,
					space,
					reader.GetCoord(),
					reader.GetUInt("instanceFormID"),
					searchResult.Label,
					null,
					LockLevel.None,
					null,
					searchResult.SpawnWeight));
			}

			return instances;
		}

		public static async Task<List<MapMarker>> GetMapMarkersForSpace(Space space) =>
			await GetMapMarkers(Connection, $"SELECT * FROM MapMarker WHERE SpaceFormID = {space.FormID} ORDER BY Y");

		static async Task<float> GetGlobal(string key)
		{
			string query = $"SELECT value FROM Global WHERE key = '{key}'";

			using SqliteDataReader reader = await GetReader(Connection, query);

			reader.Read();

			return reader.GetFloat("value");
		}

		// Returns the min (item1) and max (item2) z coordinate of all instances of the itemsToPlot in the current space
		public static async Task<(double, double)> GetZRange(List<GroupedSearchResult> itemsToPlot, Settings settings)
		{
			if (itemsToPlot.Count == 0)
			{
				return (0, 0);
			}

			List<Instance> allInstances = (await Task.WhenAll(itemsToPlot.Select(async item => await GetInstances(item, settings.Space))))
				.SelectMany(instance => instance).ToList();

			if (allInstances.Count == 0)
			{
				return (0, 0);
			}

			return (allInstances.Min(i => i.HeightForTopograph), allInstances.Max(i => i.HeightForTopograph));
		}

		// Return the fully-populated version of the same space from the database
		// This can be used to ensure JSON-ignored data are restored
		// Failing this, fall back to the first space
		public static Space PopulateFromData(this Space space)
		{
			return AllSpaces.Where(s => s.Equals(space)).FirstOrDefault() ?? throw new Exception($"Space {space.EditorID} could not be found in the database.");
		}

		public static async Task<string> GetGameVersion()
		{
			return await CommonDatabase.GetGameVersion(Connection);
		}

		// Escape functional SQL characters and wildcard on space
		static string ProcessSearchString(string input)
		{
			return input.Trim()
				.Replace("'", "''")
				.Replace("_", $"{EscapeChar}_")
				.Replace("%", $"{EscapeChar}%")
				.Replace(" ", "%");
		}

		static string ToStringForQuery(this LockLevel lockLevel)
		{
			if (lockLevel == LockLevel.None)
			{
				return string.Empty;
			}

			return lockLevel.ToString();
		}

		static Space GetSpaceByFormID(uint formID)
		{
			if (formID == 0)
			{
				return null!;
			}

			return AllSpaces.FirstOrDefault(space => space.FormID == formID) ?? throw new Exception($"Space with formID {formID.ToHex()} not found!");
		}
	}
}

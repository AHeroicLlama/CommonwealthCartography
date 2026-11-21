using Library;

namespace Mappalachia
{
	public class SearchSettings
	{
		public bool SearchInAllSpaces { get; set; } = false;

		public bool Advanced { get; set; } = false;

		public string SearchTerm { get; set; } = GetRandomSearchHint();

		bool searchInstancesOnly = false;

		public bool SearchInInstancesOnly
		{
			get
			{
				return searchInstancesOnly;
			}

			set
			{
				searchInstancesOnly = value;

				// Search in instances only implies searching in all spaces
				if (searchInstancesOnly)
				{
					SearchInAllSpaces = true;
				}
			}
		}

		public bool IncludeInitiallyDisabled { get; set; } = false;

		public List<Signature> SelectedSignatures { get; set; } = Enum.GetValues<Signature>().ToList().Where(s => s.IsRecommendedSelection()).ToList();

		public List<LockLevel> SelectedLockLevels { get; set; } = Enum.GetValues<LockLevel>().ToList();

		static string GetRandomSearchHint()
		{
			List<string> searchTermHints = new List<string>
			{
				"Caps Stash",
				"Flora",
				"Fusion Core",
				"Jet",
				"Nuka Cola",
				"PerkMag",
				"PowerArmorFurniture",
				"Pre War Money",
				"RETrigger",
				"Rare",
				"San Francisco Sunlights",
				"Synth",
				"Teddy Bear",
				"Thistle",
				"Trunk Boss",
				"UFO Crash Site",
				"Workbench",
			};

			return searchTermHints[new Random().Next(searchTermHints.Count)];
		}

		public bool ShouldSearchForScrap()
		{
			return SelectedSignatures.Contains(Signature.MISC) && SelectedLockLevels.Contains(LockLevel.None);
		}

		public bool ShouldSearchForRegion()
		{
			return SelectedSignatures.Contains(Signature.REGN) && SelectedLockLevels.Contains(LockLevel.None);
		}

		public bool ShouldSearchForLocation()
		{
			return SelectedSignatures.Contains(Signature.LCTN) && SelectedLockLevels.Contains(LockLevel.None);
		}
	}
}

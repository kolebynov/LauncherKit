using System.Text.Json.Serialization;
using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Feed;

/// <summary>A feed-defined section of mods, as the launcher's <c>ModCategoryFeedData</c>.</summary>
public sealed class ModCategoryModel : FeedEntity
{
	/// <summary>Stable identifier that <see cref="ModModel.CategoryId"/> references.</summary>
	public string Id { get; set; } = string.Empty;

	public Dictionary<string, string> Title { get; init => field = value ?? []; } = [];

	/// <summary>
	/// Markdown shown in the section header beside the title, e.g. links with icons; relative links and images resolve
	/// against the feed server. Null means no description.
	/// </summary>
	public IReadOnlyDictionary<string, string>? Description { get; set; }

	/// <summary>Only one mod of the category can be installed at a time.</summary>
	public bool IsExclusive { get; set; }
}

/// <summary>A mod, shown in its category and offered by installation components.</summary>
public sealed class ModModel : FeedEntity
{
	public string Name { get; set; } = string.Empty;

	public string CategoryId { get; set; } = string.Empty;

	public IReadOnlyDictionary<string, string>? DisplayName { get; set; }

	public string LogoPath { get; set; } = string.Empty;

	public Dictionary<string, string> DescriptionPath { get; init => field = value ?? []; } = [];

	/// <summary>Shown in the mod's option when installing modpack; null means none.</summary>
	public IReadOnlyDictionary<string, string>? OptionDescription { get; set; }

	/// <summary>
	/// No new versions will be released: the launcher hides the mod for modpack versions none of its versions supports
	/// instead of showing it as not yet updated.
	/// </summary>
	public bool IsDiscontinued { get; set; }

	/// <summary>Installing the mod for the first time or uninstalling it requires a new game.</summary>
	public bool RequiresNewGame { get; set; }

	public List<ModItemModel> Items { get; init; } = [];
}

public enum ModPlacementKind
{
	UnderSeparator,
	NextToMod,
}

/// <summary>A side of another mod in Mod Organizer's list, as the launcher's <c>ModListSide</c>.</summary>
public enum ModListSide
{
	Above,
	Below,
}

/// <summary>
/// The launcher's <c>ModPlacement</c> closed hierarchy: <see cref="Separator"/> is used only by
/// <see cref="ModPlacementKind.UnderSeparator"/>, <see cref="ModName"/> and <see cref="Side"/> only by
/// <see cref="ModPlacementKind.NextToMod"/>, so a mod goes either under a separator or next to a mod, never both.
/// </summary>
[JsonConverter(typeof(ModPlacementJsonConverter))]
public sealed class ModPlacementModel
{
	public ModPlacementKind Kind { get; set; } = ModPlacementKind.UnderSeparator;

	/// <summary>Separator name as shown, without the <c>_separator</c> suffix; created when missing.</summary>
	public string Separator { get; set; } = string.Empty;

	/// <summary>The mod placed next to; when it is not in the list, the launcher keeps the default placement.</summary>
	public string ModName { get; set; } = string.Empty;

	public ModListSide Side { get; set; } = ModListSide.Below;
}

public sealed class ModItemModel : FeedEntity
{
	public string Version { get; set; } = string.Empty;

	/// <summary>
	/// The main archive of the mod; the launcher downloads it and installs it as the mod before running
	/// <see cref="InstallSteps"/>: a FOMOD archive is configured by the user, any other archive is extracted whole.
	/// </summary>
	public DownloadInfoModel ArchiveDownloadInfo { get; set; } = new();

	/// <summary>Where the launcher puts the mod in the mod list; <see langword="null"/> for the default placement.</summary>
	public ModPlacementModel? Placement { get; set; }

	/// <summary>Optional extra steps run in order after the main archive is installed.</summary>
	[OmitWhenEmpty]
	public List<ModInstallStepModel> InstallSteps { get; init => field = value ?? []; } = [];

	/// <summary>Patches applied in version order after the version is installed, or to the installed version as an update.</summary>
	[OmitWhenEmpty]
	public List<ModPatchModel> Patches { get; init => field = value ?? []; } = [];

	public string SupportedModpackVersion { get; set; } = string.Empty;

	/// <summary>The launcher offers to uninstall this version, rolling back its rollbackable steps.</summary>
	public bool SupportsUninstall { get; set; }
}

public sealed class ModPatchModel : FeedEntity
{
	public string Version { get; set; } = string.Empty;

	/// <summary>The patch archive, extracted whole into the mod directory before the <see cref="InstallSteps"/>.</summary>
	public DownloadInfoModel DownloadInfo { get; set; } = new();

	/// <summary>
	/// Steps run in order after the patch archive is extracted; their rollbackable steps are rolled back, newest patch
	/// first, before the version's own steps when the mod is uninstalled or updated.
	/// </summary>
	[OmitWhenEmpty]
	public List<ModInstallStepModel> InstallSteps { get; init => field = value ?? []; } = [];
}

public sealed class ModInstallStepModel : FeedEntity
{
	public List<ModInstallActionModel> Actions { get; init; } = [];

	public ModInstallConditionModel? Condition { get; set; }

	public bool CanRollback { get; set; }
}

public sealed class ModInstallConditionModel : FeedEntity
{
	/// <summary>
	/// The key a <c>SelectRequiredMod</c> action publishes: the mod it picked. A <c>SelectChoices</c> action publishes its
	/// option IDs instead.
	/// </summary>
	public const string SelectedModKey = "SelectedMod";

	public int StepIndex { get; set; }

	public string Key { get; set; } = SelectedModKey;

	public CompareOperator Operator { get; set; }

	public string Value { get; set; } = string.Empty;
}

public enum CompareOperator
{
	Equal,
	NotEqual,
}

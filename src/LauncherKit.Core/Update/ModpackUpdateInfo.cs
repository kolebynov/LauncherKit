using NuGet.Versioning;
using LauncherKit.Core.AppTasks;

namespace LauncherKit.Core.Update;

public sealed class ModpackUpdateInfo
{
	public required AppTask UpdateAppTask { get; init; }

	public required UpdateType UpdateType { get; init; }

	public required NuGetVersion Version { get; init; }

	// XAML bindings cannot match union cases, so the view binds these.
	public bool IsInitialInstall => UpdateType is UpdateType.InitialInstall;

	public bool IsMajorUpdate => UpdateType is UpdateType.MajorUpdate;

	public bool IsPatchUpdate => UpdateType is UpdateType.PatchUpdate;
}
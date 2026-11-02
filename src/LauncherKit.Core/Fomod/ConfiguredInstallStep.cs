using FomodInstaller.Scripting.XmlScript;

namespace LauncherKit.Core.Fomod;

public readonly struct ConfiguredInstallStep
{
	public required InstallStep Step { get; init; }

	public required IReadOnlyList<Option> SelectedOptions { get; init; }
}
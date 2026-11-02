using FomodInstaller.Scripting.XmlScript;

namespace LauncherKit.Core.Fomod;

public interface IFomodInstaller
{
	string Title { get; }

	IEnumerable<InstallStep> InstallSteps { get; }

	Task Install(IReadOnlyList<ConfiguredInstallStep> configuredInstallSteps, CancellationToken cancellationToken);
}

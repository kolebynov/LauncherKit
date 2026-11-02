namespace LauncherKit.Core.AppTasks;

/// <summary>Creates tasks installing the tools other app tasks depend on.</summary>
public interface IPrerequisitesAppTaskProvider
{
	IReadOnlyList<AppTask> InstallVcRedistTasks { get; }

	ValueTask<AppTask> CreateInstallSevenZipTask(CancellationToken cancellationToken);
}

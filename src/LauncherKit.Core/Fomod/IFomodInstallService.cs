namespace LauncherKit.Core.Fomod;

public interface IFomodInstallService
{
	Task<bool> IsFomodArchive(string archivePath, CancellationToken cancellationToken);

	Task<IFomodInstaller> CreateInstaller(string archivePath, string modName, CancellationToken cancellationToken);
}

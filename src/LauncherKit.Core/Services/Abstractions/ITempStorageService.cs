namespace LauncherKit.Core.Services.Abstractions;

public interface ITempStorageService
{
	TempStorageService.TempDirectoryHandle CreateTempDirectory();

	void DeleteTempData();
}

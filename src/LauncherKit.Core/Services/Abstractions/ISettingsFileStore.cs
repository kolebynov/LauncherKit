namespace LauncherKit.Core.Services.Abstractions;

public interface ISettingsFileStore
{
	Task<string?> Read(CancellationToken cancellationToken);

	Task Write(string json, CancellationToken cancellationToken);
}

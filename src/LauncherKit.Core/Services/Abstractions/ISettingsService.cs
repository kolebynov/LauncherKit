using LauncherKit.Core.UserSettings;

namespace LauncherKit.Core.Services.Abstractions;

public interface ISettingsService
{
#pragma warning disable CA1024
	AllSettings GetSettings();
#pragma warning restore CA1024

	Task SaveSettings(AllSettings settings);

	/// <summary>
	/// Runs <paramref name="modify"/> on the current settings and saves them, both under the lock that also
	/// serializes <see cref="SaveSettings"/>, so concurrent modifications are applied and saved one at a time.
	/// </summary>
	Task ModifySettings(Func<AllSettings, ValueTask> modify);
}

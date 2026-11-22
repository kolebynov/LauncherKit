namespace LauncherKit.Core.Initialization;

/// <summary>
/// Runs for every application scope, also when no Skyrim directory is configured, before the scope's early and
/// configured initializers.
/// </summary>
public interface IScopeInitializer
{
	void Initialize();
}

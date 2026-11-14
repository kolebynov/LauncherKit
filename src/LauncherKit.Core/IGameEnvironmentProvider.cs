namespace LauncherKit.Core;

/// <summary>
/// Supplies environment variables to the programs the launcher starts through Mod Organizer, e.g. the player's identity
/// for the game's own plugins. Consumers register any number of providers; the kit registers none.
/// </summary>
public interface IGameEnvironmentProvider
{
	/// <summary>
	/// Returns the variables for <paramref name="launch"/>, read right before it starts. Names must be unique across
	/// providers; names and values must not contain quotes, percent signs or line breaks.
	/// </summary>
	ValueTask<IReadOnlyDictionary<string, string>> GetEnvironment(GameLaunch launch, CancellationToken cancellationToken);
}

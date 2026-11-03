namespace LauncherKit.Infrastructure;

/// <summary>Required consumer-supplied local product identity. No product names are inferred.</summary>
public sealed class LauncherProductOptions
{
	public required string SkyrimDataDirectoryName { get; init; }
	public required string LocalDatabaseFileName { get; init; }
	public required string OAuthTokenEntropy { get; init; }
	public required string EnvironmentVariablePrefix { get; init; }
	public required string AppExecutableName { get; init; }
	public required string DisplayName { get; init; }
	public required string UserAgentProduct { get; init; }
	public required string ModOrganizerDirectoryName { get; init; }
	public required string ModpackProfileName { get; init; }
	public required string ModpackVersionFile { get; init; }
	public required string UserModsSeparator { get; init; }
	public required string RequiredSkyrimVersion { get; init; }
	/// <summary>
	/// Registry key under which the consumer's installer writes the launchers root and reads the launcher's data
	/// directories, e.g. <c>HKEY_CURRENT_USER\Software\Company\Product</c>.
	/// </summary>
	public required string RegistryKey { get; init; }

	/// <summary>Rejects an incomplete identity before configuration or installation state is accessed.</summary>
	public void Validate()
	{
		Require(SkyrimDataDirectoryName, nameof(SkyrimDataDirectoryName));
		Require(LocalDatabaseFileName, nameof(LocalDatabaseFileName));
		Require(OAuthTokenEntropy, nameof(OAuthTokenEntropy));
		Require(EnvironmentVariablePrefix, nameof(EnvironmentVariablePrefix));
		Require(AppExecutableName, nameof(AppExecutableName));
		Require(DisplayName, nameof(DisplayName));
		Require(UserAgentProduct, nameof(UserAgentProduct));
		Require(ModOrganizerDirectoryName, nameof(ModOrganizerDirectoryName));
		Require(ModpackProfileName, nameof(ModpackProfileName));
		Require(ModpackVersionFile, nameof(ModpackVersionFile));
		Require(UserModsSeparator, nameof(UserModsSeparator));
		Require(RequiredSkyrimVersion, nameof(RequiredSkyrimVersion));
		Require(RegistryKey, nameof(RegistryKey));

		static void Require(string value, string name)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new ArgumentException($"Consumer product identity requires {name}.", name);
			}
		}
	}
}

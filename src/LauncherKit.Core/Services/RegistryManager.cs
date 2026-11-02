using System.Runtime.Versioning;
using Microsoft.Win32;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

[SupportedOSPlatform("Windows")]
public sealed class RegistryManager : IRegistryManager
{
	public object? GetValue(string keyName, string valueName)
	{
		using var registryKey = GetRegistryKey(keyName);
		return registryKey?.GetValue(valueName, null);
	}

	public void SetValue(string keyName, string valueName, object value)
	{
		using var registryKey = CreateRegistryKey(keyName);
		registryKey.SetValue(valueName, value);
	}

	public IReadOnlyList<string> GetSubKeyNames(string keyName)
	{
		using var registryKey = GetRegistryKey(keyName);
		return registryKey?.GetSubKeyNames() ?? [];
	}

	private static RegistryKey? GetRegistryKey(string keyName)
	{
		var (baseKey, path) = GetBaseKeyAndPath(keyName);
		using (baseKey)
		{
			return baseKey.OpenSubKey(path);
		}
	}

	private static RegistryKey CreateRegistryKey(string keyName)
	{
		var (baseKey, path) = GetBaseKeyAndPath(keyName);
		using (baseKey)
		{
			var newKey = baseKey.CreateSubKey(path);
			if (newKey == null)
			{
				throw new InvalidOperationException("Failed to create new key");
			}

			return newKey;
		}
	}

	private static (RegistryKey, string) GetBaseKeyAndPath(string keyName)
	{
		var sepIndex = keyName.IndexOf('\\', StringComparison.Ordinal);
		if (sepIndex < 1)
		{
			throw new ArgumentException($"Invalid registry key name {keyName}", nameof(keyName));
		}

		var baseKeyName = keyName[..sepIndex];
		var baseKey = baseKeyName.ToUpperInvariant() switch
		{
			"HKEY_CLASSES_ROOT" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64),
			"HKEY_CURRENT_CONFIG" => RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, RegistryView.Registry64),
			"HKEY_CURRENT_USER" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
			"HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
			"HKEY_PERFORMANCE_DATA" => RegistryKey.OpenBaseKey(RegistryHive.PerformanceData, RegistryView.Registry64),
			"HKEY_USERS" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64),
			_ => throw new ArgumentException($"Invalid base key name in registry key name {keyName}", nameof(keyName)),
		};

		return (baseKey, keyName[(sepIndex + 1)..]);
	}
}
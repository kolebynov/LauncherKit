namespace LauncherKit.Core.Services.Abstractions;

public interface IRegistryManager
{
	object? GetValue(string keyName, string valueName);

	void SetValue(string keyName, string valueName, object value);

	IReadOnlyList<string> GetSubKeyNames(string keyName);
}

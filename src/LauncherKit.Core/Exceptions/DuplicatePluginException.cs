namespace LauncherKit.Core.Exceptions;

// The plugin name is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class DuplicatePluginException : InvalidOperationException
{
	public string PluginName { get; }

	public DuplicatePluginException(string pluginName)
		: base($"Plugin {pluginName} is listed more than once in the merged plugins.txt")
	{
		PluginName = pluginName;
	}
}

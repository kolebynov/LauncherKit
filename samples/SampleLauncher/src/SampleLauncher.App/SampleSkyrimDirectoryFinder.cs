using LauncherKit.Core.Services.Abstractions;

namespace SampleLauncher.App;

/// <summary>Suggests no Skyrim directory, so the sample's setup never prefills a real installation.</summary>
internal sealed class SampleSkyrimDirectoryFinder : ISkyrimDirectoryFinder
{
	public string? FindSkyrimDirectory() => null;
}

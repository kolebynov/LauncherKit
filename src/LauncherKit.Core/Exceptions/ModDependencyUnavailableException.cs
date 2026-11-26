using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.ModDependencies;

namespace LauncherKit.Core.Exceptions;

/// <summary>No alternative of a dependency of <see cref="Mod"/> can be installed; nothing was changed.</summary>
#pragma warning disable CA1032
public sealed class ModDependencyUnavailableException : Exception
{
	public ModFeedData Mod { get; }

	/// <summary>The dependency's alternatives, each with its <see cref="ModDependencyOption.UnavailableReason"/>.</summary>
	public IReadOnlyList<ModDependencyOption> Alternatives { get; }

	public override string Message =>
		$"No dependency of mod {Mod.Name} can be installed: {string.Join(", ", Alternatives.Select(x => x.Mod.Name))}";

	public ModDependencyUnavailableException(ModFeedData mod, IReadOnlyList<ModDependencyOption> alternatives)
	{
		Mod = mod;
		Alternatives = alternatives;
	}
}

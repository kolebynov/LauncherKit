using LauncherKit.Core.UserSettings;

namespace LauncherKit.Desktop.ViewModels.Setup;

/// <summary>
/// Supplies first-setup steps. Register providers with <c>TryAddEnumerable</c> (scoped); <see cref="FirstSetupSteps"/>
/// orders them by <see cref="Order"/> and concatenates their steps.
/// </summary>
public interface ISetupStepProvider
{
	/// <summary>Position among providers, ascending; see <see cref="SetupStepOrder"/> for the kit's steps.</summary>
	int Order { get; }

	/// <summary>The steps the current settings still need; none when nothing is left to set up.</summary>
	IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context);
}

/// <summary><see cref="ISetupStepProvider.Order"/> values of the kit's providers.</summary>
public static class SetupStepOrder
{
	public const int SkyrimDirectory = 100;
	public const int AppDataDirectory = 200;
	public const int Availability = 1000;
}

/// <summary>The settings the first setup edits; saved after its last step.</summary>
public sealed class FirstSetupContext(AllSettings settings)
{
	public AllSettings Settings { get; } = settings;
}

public static class FirstSetupSteps
{
	/// <summary>The steps of all providers, by ascending <see cref="ISetupStepProvider.Order"/> (stable).</summary>
	public static IReadOnlyList<SetupStepViewModel> Create(IEnumerable<ISetupStepProvider> providers,
		FirstSetupContext context) =>
		providers.OrderBy(x => x.Order).SelectMany(x => x.CreateSteps(context)).ToArray();
}

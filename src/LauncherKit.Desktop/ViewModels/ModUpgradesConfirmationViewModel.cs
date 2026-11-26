using CommunityToolkit.Mvvm.Input;
using NuGet.Versioning;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.ModDependencies;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>
/// Asks whether installing <see cref="RequestedModName"/> may update the installed dependencies listed in
/// <see cref="Upgrades"/> and install the dependencies requiring a new game listed in <see cref="NewGameModNames"/>.
/// The view shows the facts and runs <see cref="ContinueCommand"/> or <see cref="CancelCommand"/>; closing it any other
/// way must cancel.
/// </summary>
public partial class ModUpgradesConfirmationViewModel : ViewModelBase
{
	private readonly ModUpgradesConfirmation _confirmation;

	/// <summary>The display name of the mod being installed.</summary>
	public string RequestedModName { get; }

	public IReadOnlyList<ModUpgradeFact> Upgrades { get; }

	/// <summary>Display names of the dependencies installed for the first time that require a new game.</summary>
	public IReadOnlyList<string> NewGameModNames { get; }

	public ModUpgradesConfirmationViewModel(ModUpgradesConfirmation confirmation)
	{
		_confirmation = confirmation;
		RequestedModName = confirmation.RequestedMod.ResolveDisplayName();
		Upgrades = confirmation.Upgrades
			.Select(x => new ModUpgradeFact(x.Mod.ResolveDisplayName(), x.InstalledVersion, x.TargetVersion))
			.ToArray();
		NewGameModNames = confirmation.NewGameInstalls.Select(x => x.ResolveDisplayName()).ToArray();
	}

	[RelayCommand]
	private void Continue() => _confirmation.SetResult(true);

	[RelayCommand]
	private void Cancel() => _confirmation.SetResult(false);
}

/// <summary>The installed dependency <paramref name="ModName"/> (display name) updated to <paramref name="TargetVersion"/>.</summary>
public sealed record ModUpgradeFact(string ModName, NuGetVersion InstalledVersion, NuGetVersion TargetVersion);

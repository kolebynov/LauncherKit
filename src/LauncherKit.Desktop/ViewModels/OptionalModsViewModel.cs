using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class OptionalModsViewModel : BaseActivatableViewModel
{
	[ObservableProperty]
	public partial IReadOnlyList<ModCardViewModel> OptionalMods { get; private set; } = [];

	public Loader Loader { get; } = new();

	public OptionalModsViewModel(IFeedProvider feedProvider, IInstallAppTaskProvider installAppTaskProvider,
		IAppTaskQueue appTaskQueue, IModpackVersionProvider modpackVersionProvider, IFeedUtils feedUtils,
		IUiDispatcher dispatcher)
	{
		this.WhenActivated(d =>
		{
			modpackVersionProvider.ObservePropertyChanged(x => x.InstalledModpackVersion)
				.ObserveOnUi(dispatcher)
				.SubscribeAwait(async (_, ct) =>
				{
					using var loading = Loader.BeginLoading();
					OptionalMods = [];
					var modpackVersion = modpackVersionProvider.InstalledModpackVersion;
					if (modpackVersion == null)
					{
						return;
					}

					var mods = await feedProvider.GetModFeedDatas(ct);
					OptionalMods = mods
						?.Where(x => x.Items.Any(y => y.SupportedModpackVersion.Satisfies(modpackVersion)))
						.Select(x => new ModCardViewModel(
							x.DisplayName?.Resolve() ?? x.Name, feedUtils.GetUri(x.LogoPath),
							feedUtils.GetUri(x.DescriptionPath.Resolve()),
							() => installAppTaskProvider.GetModInstallState(x, modpackVersion),
							new AsyncRelayCommand(async commandCt =>
							{
								var appTask = await installAppTaskProvider.CreateModInstallAppTask(x, null, commandCt);
								await appTaskQueue.EnqueueAndWaitForCompletion(appTask, commandCt);
							}),
							new AsyncRelayCommand(async commandCt =>
							{
								await appTaskQueue.EnqueueAndWaitForCompletion(
									installAppTaskProvider.CreateModUninstallAppTask(x), commandCt);
							})))
						.ToArray() ?? [];
				})
				.AddTo(d);
		});
	}

}
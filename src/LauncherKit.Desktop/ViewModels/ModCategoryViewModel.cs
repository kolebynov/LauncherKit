using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using LauncherKit.Infrastructure;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>
/// A section of mod cards for one feed category, loaded while activated. A mod without a version supporting the installed
/// modpack is shown unavailable, unless it is discontinued, in which case it is hidden.
/// </summary>
public partial class ModCategoryViewModel : BaseActivatableViewModel
{
	private readonly ModCategoryFeedData _category;
	private readonly IInstallationService _installations;
	private readonly IFeedProvider _feedProvider;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IFeedUtils _feedUtils;
	private readonly IUiDispatcher _dispatcher;

	public string Id => _category.Id;

	public string Title { get; }

	/// <summary>The category's Markdown description, shown in the section header, if any.</summary>
	public string? Description { get; }

	/// <summary>The feed server root, against which the description's relative links and images resolve.</summary>
	public Uri DescriptionAssetRoot { get; }

	/// <summary>The cards, each kept current by its view while activated.</summary>
	[ObservableProperty]
	public partial IReadOnlyList<ModCardViewModel> Mods { get; private set; } = [];

	public Loader Loader { get; } = new();

	public ModCategoryViewModel(ModCategoryFeedData category, IInstallationService installations,
		IFeedProvider feedProvider, IModpackVersionProvider modpackVersionProvider, IFeedUtils feedUtils,
		IUiDispatcher dispatcher)
	{
		_category = category;
		_installations = installations;
		_feedProvider = feedProvider;
		_modpackVersionProvider = modpackVersionProvider;
		_feedUtils = feedUtils;
		_dispatcher = dispatcher;
		Title = category.Title.Resolve();
		Description = category.Description?.Resolve();
		DescriptionAssetRoot = feedUtils.GetUri(string.Empty);

		this.WhenActivated(d =>
		{
			_modpackVersionProvider.ObservePropertyChanged(x => x.InstalledModpackVersion)
				.ObserveOnUi(dispatcher)
				.SubscribeAwait((_, ct) => UpdateMods(ct))
				.AddTo(d);
		});
	}

	private async ValueTask UpdateMods(CancellationToken cancellationToken)
	{
		using var loading = Loader.BeginLoading();
		var modpackVersion = _modpackVersionProvider.InstalledModpackVersion;
		var categoryMods = (await _feedProvider.GetModFeedDatas(cancellationToken))
			.Where(x => x.CategoryId == _category.Id)
			.ToArray();
		Mods = categoryMods
			.Where(x => x.IsOfferedFor(modpackVersion))
			.Select(x => new ModCardViewModel(x, _category, categoryMods, _installations, _modpackVersionProvider,
				_feedUtils, _dispatcher))
			.ToArray();
	}
}

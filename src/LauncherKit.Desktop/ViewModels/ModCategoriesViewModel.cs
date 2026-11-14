using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>The feed's mod categories, in feed order, each a section of mod cards, recreated on each activation.</summary>
public partial class ModCategoriesViewModel : BaseActivatableViewModel
{
	private readonly IInstallationService _installations;
	private readonly IFeedProvider _feedProvider;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IFeedUtils _feedUtils;
	private readonly IUiDispatcher _dispatcher;

	[ObservableProperty]
	public partial IReadOnlyList<ModCategoryViewModel> Categories { get; private set; } = [];

	public Loader Loader { get; } = new();

	public ModCategoriesViewModel(IInstallationService installations, IFeedProvider feedProvider,
		IModpackVersionProvider modpackVersionProvider, IFeedUtils feedUtils, IUiDispatcher dispatcher)
	{
		_installations = installations;
		_feedProvider = feedProvider;
		_modpackVersionProvider = modpackVersionProvider;
		_feedUtils = feedUtils;
		_dispatcher = dispatcher;

		this.WhenActivated(async (_, ct) => await LoadCategories(ct));
	}

	private async ValueTask LoadCategories(CancellationToken cancellationToken)
	{
		using var loading = Loader.BeginLoading();
		var categories = await _feedProvider.GetModCategories(cancellationToken);
		Categories = categories
			.Select(x => new ModCategoryViewModel(x, _installations, _feedProvider, _modpackVersionProvider, _feedUtils,
				_dispatcher))
			.ToArray();
	}
}

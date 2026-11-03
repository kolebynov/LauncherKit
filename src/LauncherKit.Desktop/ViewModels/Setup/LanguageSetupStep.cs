using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.ViewModels.Setup;

/// <summary>
/// Chooses the UI language among <see cref="LanguageCulture.Languages"/>. The choice applies at once, so the rest of
/// the setup is shown in it; it defaults to the operating system's language when supported, otherwise English.
/// </summary>
public partial class LanguageSetupStepViewModel : SetupStepViewModel
{
	private readonly FirstSetupContext _context;
	private readonly IAppReloader _appReloader;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(NextCommand))]
	public partial string? SelectedLanguage { get; set; }

	public AsyncRelayCommand NextCommand { get; }

	public LanguageSetupStepViewModel(FirstSetupContext context, IAppReloader appReloader)
	{
		_context = context;
		_appReloader = appReloader;
		NextCommand = new AsyncRelayCommand(Next, () => IsShown && LanguageCulture.IsSupported(SelectedLanguage));
		var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
		SelectedLanguage = LanguageCulture.IsSupported(systemLanguage) ? systemLanguage : LanguageCulture.Languages[0];
	}

	protected override void OnShown() => NextCommand.NotifyCanExecuteChanged();

	protected override void OnHidden() => NextCommand.NotifyCanExecuteChanged();

	private Task Next()
	{
		var language = SelectedLanguage!;
		_context.Settings.App.Language = language;
		LanguageCulture.Apply(language);
		_appReloader.ReloadApp(AppReloadMode.Language);
		return GoNext();
	}
}

/// <summary>Offers the language step until a supported language is chosen (<see cref="LanguageCulture.IsSupported"/>).</summary>
public sealed class LanguageSetupStepProvider(IAppReloader appReloader) : ISetupStepProvider
{
	public int Order => SetupStepOrder.Language;

	public IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context) =>
		LanguageCulture.IsSupported(context.Settings.App.Language) ? [] : [new LanguageSetupStepViewModel(context, appReloader)];
}

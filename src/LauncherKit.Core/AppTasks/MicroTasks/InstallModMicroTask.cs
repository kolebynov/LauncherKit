using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using R3;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Fomod;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class InstallModMicroTask : IAppMicroTask
{
	private readonly IFileStorageService _fileStorageService;
	private readonly IFomodInstallService _fomodInstallService;
	private readonly ILogger<InstallModMicroTask> _logger;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly string _modName;
	private readonly string _archiveName;
	private readonly ModPlacement? _placement;

	public MicroTaskDescription Description { get; }

	public InstallModMicroTask(IFileStorageService fileStorageService, IFomodInstallService fomodInstallService,
		ILogger<InstallModMicroTask> logger, IModOrganizerService modOrganizerService, string modName,
		string archiveName, ModPlacement? placement)
	{
		_fileStorageService = fileStorageService;
		_fomodInstallService = fomodInstallService;
		_logger = logger;
		_modOrganizerService = modOrganizerService;
		_modName = modName;
		Description = new MicroTaskDescription.InstallMod(_modName);
		_archiveName = archiveName;
		_placement = placement;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var archivePath = _fileStorageService.GetFilePath(_archiveName)
			?? throw new InvalidOperationException($"Archive {_archiveName} for mod {_modName} was not downloaded");

		if (!await _fomodInstallService.IsFomodArchive(archivePath, cancellationToken))
		{
			await _modOrganizerService.InstallMod(_modName, archivePath, _placement, cancellationToken);
			return;
		}

		_logger.LogInformation("Installing {ModName} from FOMOD archive {ArchivePath}", _modName, archivePath);
		stepSubject.OnNext(new MicroTaskStep.MicroOperation(MicroTaskDescription.ConfigureMod.Instance));
		var fomodInstaller = await _fomodInstallService.CreateInstaller(archivePath, _modName, cancellationToken);
		var fomodChoices = new FomodChoices(fomodInstaller.InstallSteps);
		using var choiceSelectionContext = new ChoiceSelectionContext(new ChoiceText.Content(fomodInstaller.Title), fomodChoices.Steps,
			cancellationToken);
		stepSubject.OnNext(new MicroTaskStep.SelectChoices(choiceSelectionContext));
		_logger.LogInformation("Waiting for FOMOD configuration...");
		var selections = await choiceSelectionContext.ResultTask;

		stepSubject.OnNext(new MicroTaskStep.MicroOperation(MicroTaskDescription.Installing.Instance));
		await fomodInstaller.Install(fomodChoices.ToConfiguredInstallSteps(selections), cancellationToken);
		if (_placement != null)
		{
			await _modOrganizerService.PlaceMod(_modName, _placement, cancellationToken);
		}

		await _modOrganizerService.ActivateMod(_modName, true, true, cancellationToken);
	}
}

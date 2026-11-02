namespace LauncherKit.Desktop.Services;

public sealed class SaveWindowStateCloseHandler(WindowStateService windowStateService) : IAppCloseHandler
{
	public async ValueTask<IAppCloseHandler.HandleResult> Handle(AppCloseContext context)
	{
		await windowStateService.Save(context.Window);
		return new IAppCloseHandler.HandleResult.CanClose();
	}
}
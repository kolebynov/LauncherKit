namespace LauncherKit.Desktop.Services;

public interface IAppCloseHandler
{
	ValueTask<HandleResult> Handle(AppCloseContext context);

	public union HandleResult(HandleResult.CanClose, HandleResult.CannotClose)
	{
		public sealed record CanClose;

		public sealed record CannotClose;
	}
}
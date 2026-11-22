namespace LauncherKit.Core.Services.Abstractions;

public interface IApplicationLifetime
{
	IServiceProvider Services { get; }

	void CreateNewScope();

	void Exit();
}
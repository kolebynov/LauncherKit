namespace LauncherKit.Core.Services.Abstractions;

public interface IGlobalScope : IDisposable
{
	GlobalScope.EnteredScope EnterScope();
}

namespace LauncherKit.Bootstrapper;

public interface IInterprocessLockFactory
{
	Task<IDisposable?> TryAcquire(string name, CancellationToken cancellationToken);
}

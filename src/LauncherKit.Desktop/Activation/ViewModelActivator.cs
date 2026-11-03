using R3;

namespace LauncherKit.Desktop.Activation;

public sealed class ViewModelActivator : IDisposable
{
	private readonly List<Func<CompositeDisposable, CancellationToken, ValueTask>> _activationBlocks = new();
	private IDisposable _activationHandle = Disposable.Empty;
	private int _activateCount;

	public async ValueTask<IDisposable> Activate(CancellationToken cancellationToken)
	{
		if (Interlocked.Increment(ref _activateCount) == 1)
		{
			var compositeDisposable = new CompositeDisposable();

			try
			{
				foreach (var activationBlock in _activationBlocks)
				{
					await activationBlock(compositeDisposable, cancellationToken);
				}
			}
			catch (Exception)
			{
				compositeDisposable.Dispose();
				throw;
			}

			Interlocked.Exchange(ref _activationHandle, compositeDisposable).Dispose();
		}

		return Disposable.Create(this, static self => self.Deactivate());
	}

	public void Dispose()
	{
		_activationHandle.Dispose();
	}

	public void AddActivationBlock(Func<CompositeDisposable, CancellationToken, ValueTask> block) =>
		_activationBlocks.Add(block ?? throw new ArgumentNullException(nameof(block)));

	private void Deactivate()
	{
		if (Interlocked.Decrement(ref _activateCount) == 0)
		{
			Interlocked.Exchange(ref _activationHandle, Disposable.Empty).Dispose();
		}
	}
}
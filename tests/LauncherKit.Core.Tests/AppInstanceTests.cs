using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests;

[TestFixture]
public sealed class AppInstanceTests
{
	[Test]
	public void CreateNewScope_WithCallerSynchronizationContext_StartsAsyncDisposalOutsideThatContext()
	{
		var services = new ServiceCollection().AddLogging();
		services.AddOptions<AppSettings>();
		services.AddScoped<DisposalProbe>();
		using var sut = new AppInstance(services.BuildServiceProvider());
		var previousScopeProbe = sut.ServiceProvider.GetRequiredService<DisposalProbe>();
		var previousContext = SynchronizationContext.Current;
		try
		{
			// The base context posts continuations to the pool, avoiding a hanging test on the old implementation;
			// observing it at disposal entry still detects the UI-deadlocking capture boundary.
			SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

			sut.CreateNewScope();
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previousContext);
		}

		Assert.Multiple(() =>
		{
			Assert.That(previousScopeProbe.EntryContext, Is.Null);
			Assert.That(previousScopeProbe.IsDisposed, Is.True);
			Assert.That(sut.ServiceProvider.GetRequiredService<DisposalProbe>(), Is.Not.SameAs(previousScopeProbe));
		});
	}

	private sealed class DisposalProbe : IAsyncDisposable
	{
		public DisposalProbe() { }

		public SynchronizationContext? EntryContext { get; private set; }

		public bool IsDisposed { get; private set; }

		public async ValueTask DisposeAsync()
		{
			EntryContext = SynchronizationContext.Current;
			await Task.Yield();
			IsDisposed = true;
		}
	}
}

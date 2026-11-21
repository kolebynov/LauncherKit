using LauncherKit.Desktop.SingleInstance;
using Microsoft.Extensions.DependencyInjection;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class LaunchHandlerRegistrationTests
{
	[Test]
	public void AddLaunchHandler_SameTypeAddedTwice_ResolvesEachTypeOnceInRegistrationOrderPerScope()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddLaunchHandler<RecordingHandler>();
		services.AddLaunchHandler<OtherHandler>();
		services.AddLaunchHandler<RecordingHandler>();
		using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		using var firstScope = provider.CreateScope();
		using var secondScope = provider.CreateScope();

		// Act
		var first = firstScope.ServiceProvider.GetServices<ILaunchHandler>().ToArray();
		var again = firstScope.ServiceProvider.GetServices<ILaunchHandler>().ToArray();
		var second = secondScope.ServiceProvider.GetServices<ILaunchHandler>().ToArray();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(first.Select(h => h.GetType()), Is.EqualTo(new[] { typeof(RecordingHandler), typeof(OtherHandler) }));
			Assert.That(again, Is.EqualTo(first));
			Assert.That(second, Has.None.AnyOf(first[0], first[1]));
		});
	}

	[Test]
	public void AddLauncherKitDesktop_ConsumerHandlersAddedAfterwards_RegistersKitActivationHandlerFirst()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act
		services.AddLauncherKitDesktop();
		services.AddLaunchHandler<RecordingHandler>();
		services.AddLaunchHandler<OtherHandler>();

		// Assert
		var handlers = services.Where(d => d.ServiceType == typeof(ILaunchHandler)).ToArray();
		Assert.Multiple(() =>
		{
			Assert.That(handlers.Select(d => d.ImplementationType), Is.EqualTo(new[]
			{
				typeof(MainWindowActivationLaunchHandler), typeof(RecordingHandler), typeof(OtherHandler),
			}));
			Assert.That(handlers.Select(d => d.Lifetime), Is.All.EqualTo(ServiceLifetime.Scoped));
		});
	}

	private sealed class RecordingHandler : ILaunchHandler
	{
		public ValueTask Handle(LaunchContext context, CancellationToken cancellationToken) => throw new AssertionException("Unexpected launch.");
	}

	private sealed class OtherHandler : ILaunchHandler
	{
		public ValueTask Handle(LaunchContext context, CancellationToken cancellationToken) => throw new AssertionException("Unexpected launch.");
	}
}

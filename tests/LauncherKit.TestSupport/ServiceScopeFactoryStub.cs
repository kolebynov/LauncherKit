using Microsoft.Extensions.DependencyInjection;

namespace LauncherKit.TestSupport;

/// <summary>
/// Creates a strict per-task service scope backed by services supplied by the test.
/// </summary>
public static class ServiceScopeFactoryStub
{
	public static IServiceScopeFactory ScopeFactoryFor(AutoMocker mocker, params object[] services)
	{
		ArgumentNullException.ThrowIfNull(mocker);
		ArgumentNullException.ThrowIfNull(services);

		var provider = mocker.GetMock<IServiceProvider>();
		foreach (var service in services)
		{
			ArgumentNullException.ThrowIfNull(service);
			provider.Setup(x => x.GetService(service.GetType().GetInterfaces()[0])).Returns(service);
		}

		var scope = mocker.GetMock<IServiceScope>();
		scope.Setup(x => x.ServiceProvider).Returns(provider.Object);
		scope.Setup(x => x.Dispose());

		var factory = mocker.GetMock<IServiceScopeFactory>();
		factory.Setup(x => x.CreateScope()).Returns(scope.Object);
		return factory.Object;
	}
}

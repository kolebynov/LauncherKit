using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Core.Extensions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core;

/// <summary>Collects the consumer's identity, configuration and services; the kit's application host builds it.</summary>
public sealed class AppBuilder
{
	public IServiceCollection Services { get; } = new ServiceCollection();

	public ConfigurationManager Configuration { get; } = new();

	public LauncherProductOptions Product { get; }

	public AppBuilder(LauncherProductOptions product)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		Product = product;
	}

	internal AppInstance Build()
	{
		Services.AddLauncherKitCore(Configuration, Product);

		const bool validateScopes =
#if DEBUG
			true;
#else
			false;
#endif

		return new AppInstance(Services.BuildServiceProvider(validateScopes));
	}
}
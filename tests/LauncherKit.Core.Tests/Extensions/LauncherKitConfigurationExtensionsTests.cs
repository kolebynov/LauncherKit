using Microsoft.Extensions.Configuration;
using LauncherKit.Core.Extensions;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Extensions;

[TestFixture]
public sealed class LauncherKitConfigurationExtensionsTests
{
	[Test]
	public void Configuration_LoadsJsonThenOverrideThenProductPrefixedEnvironment()
	{
		using var temp = new TempDirectory();
		var prefix = $"LauncherKit_{Guid.NewGuid():N}_";
		var product = ProductTestSupport.CreateProduct();
		product.EnvironmentVariablePrefix = prefix;
		var variable = prefix + "App__Language";
		File.WriteAllText(Path.Combine(temp.Path, "appsettings.json"),
			"{\"Product\":{\"EnvironmentVariablePrefix\":\"IGNORED_\"},\"App\":{\"Language\":\"ru\",\"SkyrimDirectory\":\"base\"}}");
		File.WriteAllText(Path.Combine(temp.Path, LauncherConstants.SettingsOverrideFileName),
			"""{"App":{"Language":"en","SkyrimDirectory":"override"}}""");
		Environment.SetEnvironmentVariable(variable, "de");
		try
		{
			using var configuration = new ConfigurationManager();
			configuration.AddLauncherKitConfiguration(temp.Path, product);

			Assert.Multiple(() =>
			{
				Assert.That(configuration["App:Language"], Is.EqualTo("de"));
				Assert.That(configuration["App:SkyrimDirectory"], Is.EqualTo("override"));
				Assert.That(configuration["Product:EnvironmentVariablePrefix"], Is.EqualTo("IGNORED_"));
			});
		}
		finally
		{
			Environment.SetEnvironmentVariable(variable, null);
		}
	}

	[Test]
	public void Configuration_IndependentProducts_UseOnlyTheirOwnEnvironmentPrefix()
	{
		using var temp = new TempDirectory();
		File.WriteAllText(Path.Combine(temp.Path, "appsettings.json"), """{"App":{"Language":"base"}}""");
		var first = ProductTestSupport.CreateProduct();
		var second = ProductTestSupport.CreateProduct();
		first.EnvironmentVariablePrefix = $"First_{Guid.NewGuid():N}_";
		second.EnvironmentVariablePrefix = $"Second_{Guid.NewGuid():N}_";
		var firstVariable = first.EnvironmentVariablePrefix + "App__Language";
		var secondVariable = second.EnvironmentVariablePrefix + "App__Language";
		Environment.SetEnvironmentVariable(firstVariable, "en");
		Environment.SetEnvironmentVariable(secondVariable, "de");
		try
		{
			using var firstConfiguration = new ConfigurationManager();
			using var secondConfiguration = new ConfigurationManager();
			firstConfiguration.AddLauncherKitConfiguration(temp.Path, first);
			secondConfiguration.AddLauncherKitConfiguration(temp.Path, second);
			Assert.Multiple(() =>
			{
				Assert.That(firstConfiguration["App:Language"], Is.EqualTo("en"));
				Assert.That(secondConfiguration["App:Language"], Is.EqualTo("de"));
			});
		}
		finally
		{
			Environment.SetEnvironmentVariable(firstVariable, null);
			Environment.SetEnvironmentVariable(secondVariable, null);
		}
	}

	[Test]
	public void Configuration_InvalidIdentity_ThrowsBeforeLoadingFiles()
	{
		using var temp = new TempDirectory();
		using var configuration = new ConfigurationManager();
		var originalSources = configuration.Sources.ToArray();
		Assert.That(() => configuration.AddLauncherKitConfiguration(temp.Path, new LauncherProductOptions()),
			Throws.ArgumentException);
		Assert.That(configuration.Sources, Is.EqualTo(originalSources));
	}
}

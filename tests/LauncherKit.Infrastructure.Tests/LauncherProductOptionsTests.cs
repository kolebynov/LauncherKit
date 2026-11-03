using System.Reflection;
using LauncherKit.TestSupport;

namespace LauncherKit.Infrastructure.Tests;

[TestFixture]
public sealed class LauncherProductOptionsTests
{
	private static IEnumerable<PropertyInfo> RequiredIdentityStrings() => typeof(LauncherProductOptions)
		.GetProperties().Where(property => property.PropertyType == typeof(string));

	[TestCaseSource(nameof(RequiredIdentityStrings))]
	public void Validate_BlankRequiredIdentity_ThrowsArgumentException(PropertyInfo property)
	{
		foreach (var value in new string?[] { null, string.Empty, " \t" })
		{
			var product = ProductTestSupport.CreateProduct();
			property.SetValue(product, value);
			Assert.That(product.Validate, Throws.ArgumentException, property.Name);
		}
	}

	[Test]
	public void Validate_MissingOrInvalidLaunchVariants_ThrowsArgumentException()
	{
		var property = typeof(LauncherProductOptions).GetProperty(nameof(LauncherProductOptions.GameLaunchVariants))!;
		foreach (var variants in new IReadOnlyList<GameLaunchVariant>?[]
		{
			null, [], [null!], [new() { Id = "" }], [new() { Id = " \t" }],
		})
		{
			var product = ProductTestSupport.CreateProduct();
			property.SetValue(product, variants);
			Assert.That(product.Validate, Throws.ArgumentException);
		}
	}

	[Test]
	public void InstallationPaths_UseIndependentIdentities_WithoutChangingGenericStoragePaths()
	{
		var settings = new AppSettings { AppDataDirectory = @"C:\DownloadsRoot", SkyrimDirectory = @"C:\Game" };
		var first = ProductTestSupport.CreateProduct();
		var second = ProductTestSupport.CreateProduct(
			skyrimDataDirectoryName: ".OtherLauncher", modOrganizerDirectoryName: "Other MO");
		Assert.Multiple(() =>
		{
			Assert.That(settings.GetSkyrimAppDataDirectory(first), Is.EqualTo(Path.Combine(settings.SkyrimDirectory, ".ExampleLauncher")));
			Assert.That(settings.GetSkyrimAppDataDirectory(second), Is.EqualTo(Path.Combine(settings.SkyrimDirectory, ".OtherLauncher")));
			Assert.That(settings.GetModOrganizerDirectory(first), Is.EqualTo(Path.Combine(settings.SkyrimDirectory, "Example MO")));
			Assert.That(settings.GetModOrganizerDirectory(second), Is.EqualTo(Path.Combine(settings.SkyrimDirectory, "Other MO")));
			Assert.That(settings.GetDownloadsDirectory(), Is.EqualTo(Path.Combine(settings.AppDataDirectory, "Downloads")));
			Assert.That(settings.GetTempDataDirectory(), Is.EqualTo(Path.Combine(settings.AppDataDirectory, "Temp")));
		});
	}

	[Test]
	public void InstallationPaths_InvalidIdentity_ThrowsBeforeReturningStatePath()
	{
		var settings = new AppSettings { SkyrimDirectory = @"C:\Game" };
		Assert.Multiple(() =>
		{
			Assert.That(() => settings.GetSkyrimAppDataDirectory(ProductTestSupport.CreateProduct(globalDatabaseFileName: "")), Throws.ArgumentException);
			Assert.That(() => settings.GetModOrganizerDirectory(ProductTestSupport.CreateProduct(globalDatabaseFileName: "")), Throws.ArgumentException);
		});
	}
}

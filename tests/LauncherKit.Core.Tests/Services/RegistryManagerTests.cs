using Microsoft.Win32;
using LauncherKit.Core.Services;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class RegistryManagerTests
{
	[Test]
	public void SetValue_ThenGetValue_RoundTripsUnderCurrentUser()
	{
		// Arrange
		var relativeKey = $@"Software\LauncherKitTests\{Guid.NewGuid():N}";
		var key = $@"HKEY_CURRENT_USER\{relativeKey}";
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<RegistryManager>();

		try
		{
			// Act
			sut.SetValue(key, "Value", 42);

			// Assert
			Assert.That(sut.GetValue(key, "Value"), Is.EqualTo(42));
		}
		finally
		{
			Registry.CurrentUser.DeleteSubKeyTree(relativeKey, false);
		}
	}

	[Test]
	public void GetSubKeyNames_ParentWithChildren_ReturnsChildNames()
	{
		// Arrange
		var relativeKey = $@"Software\LauncherKitTests\{Guid.NewGuid():N}";
		var key = $@"HKEY_CURRENT_USER\{relativeKey}";
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<RegistryManager>();

		try
		{
			sut.SetValue($@"{key}\first", "Value", 1);
			sut.SetValue($@"{key}\second", "Value", 2);

			// Act
			var result = sut.GetSubKeyNames(key);

			// Assert
			Assert.That(result, Is.EquivalentTo(new[] { "first", "second" }));
		}
		finally
		{
			Registry.CurrentUser.DeleteSubKeyTree(relativeKey, false);
		}
	}

	[Test]
	public void GetSubKeyNames_MissingKey_ReturnsEmptyCollection()
	{
		// Arrange
		var key = $@"HKEY_CURRENT_USER\Software\LauncherKitTests\missing-{Guid.NewGuid():N}";
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<RegistryManager>();

		// Act
		var result = sut.GetSubKeyNames(key);

		// Assert
		Assert.That(result, Is.Empty);
	}
}

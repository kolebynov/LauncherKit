using LauncherKit.Desktop.SingleInstance;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class SingleInstanceNamesTests
{
	private const string Sid = "S-1-5-21-1-2-3-1001";

	[Test]
	public void Create_SameKeyInAnotherCase_ReturnsSameNames()
	{
		// Act
		var lower = SingleInstanceNames.Create(@"HKEY_CURRENT_USER\Software\Company\Product", Sid, 1);
		var upper = SingleInstanceNames.Create(@"hkey_current_user\SOFTWARE\company\product", Sid, 1);

		// Assert
		Assert.That(upper, Is.EqualTo(lower));
	}

	[TestCase(@"HKEY_CURRENT_USER\Software\Company\Product\")]
	[TestCase(@"HKEY_CURRENT_USER\Software\Company\Product\\")]
	[TestCase(@"HKEY_CURRENT_USER\Software\\Company\Product")]
	public void Create_KeyWithExtraSeparators_ReturnsSameNamesAsRegistryOpens(string key)
	{
		// Act
		var names = SingleInstanceNames.Create(key, Sid, 1);

		// Assert
		Assert.That(names, Is.EqualTo(SingleInstanceNames.Create(@"HKEY_CURRENT_USER\Software\Company\Product", Sid, 1)));
	}

	[TestCase(@"HKEY_CURRENT_USER\Software\Company\Product ")]
	[TestCase(@" HKEY_CURRENT_USER\Software\Company\Product")]
	public void Create_KeyWithSpaces_ReturnsDistinctNames(string key)
	{
		// Act
		var names = SingleInstanceNames.Create(key, Sid, 1);

		// Assert
		Assert.That(names, Is.Not.EqualTo(SingleInstanceNames.Create(@"HKEY_CURRENT_USER\Software\Company\Product", Sid, 1)));
	}

	[Test]
	public void Create_DifferentProductUserOrSession_ReturnsDistinctNames()
	{
		// Act
		var names = new[]
		{
			SingleInstanceNames.Create(@"HKEY_CURRENT_USER\Software\Company\Product", Sid, 1),
			SingleInstanceNames.Create(@"HKEY_CURRENT_USER\Software\Company\Other", Sid, 1),
			SingleInstanceNames.Create(@"HKEY_CURRENT_USER\Software\Company\Product", "S-1-5-21-1-2-3-1002", 1),
			SingleInstanceNames.Create(@"HKEY_CURRENT_USER\Software\Company\Product", Sid, 2),
		};

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(names.Select(n => n.Mutex), Is.Unique);
			Assert.That(names.Select(n => n.Pipe), Is.Unique);
		});
	}

	[Test]
	public void Create_BlankKey_Throws()
	{
		// Act & Assert
		Assert.That(() => SingleInstanceNames.Create(" ", Sid, 1), Throws.ArgumentException);
	}
}

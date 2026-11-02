using LauncherKit.Core.Choices;

namespace LauncherKit.Core.Tests.Choices;

[TestFixture]
public sealed class ChoiceOptionTests
{
	[Test]
	public void Init_RequiredThenUnavailable_Throws()
	{
		// Arrange
		static ChoiceOption Create() => new() { Name = new ChoiceText.Content("A"), Description = new ChoiceText.Content(""), IsRequired = true, UnavailableReason = new ChoiceText.Content("Reason") };

		// Act & Assert
		Assert.That(Create, Throws.ArgumentException);
	}

	[Test]
	public void Init_UnavailableThenRequired_Throws()
	{
		// Arrange
		static ChoiceOption Create() => new() { Name = new ChoiceText.Content("A"), Description = new ChoiceText.Content(""), UnavailableReason = new ChoiceText.Content("Reason"), IsRequired = true };

		// Act & Assert
		Assert.That(Create, Throws.ArgumentException);
	}

	[TestCase("")]
	[TestCase(" ")]
	public void Init_BlankUnavailableReason_Throws(string reason)
	{
		// Arrange
		ChoiceOption Create() => new() { Name = new ChoiceText.Content("A"), Description = new ChoiceText.Content(""), UnavailableReason = new ChoiceText.Content(reason) };

		// Act & Assert
		Assert.That(Create, Throws.ArgumentException);
	}
}

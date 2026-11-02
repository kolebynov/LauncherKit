using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Choices;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class ChoiceStepViewModelTests
{
	[Test]
	public void IsSelected_RequiredOptionDeselected_RemainsSelected()
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.Any, CreateOption("Required", isRequired: true)));
		var required = sut.Options.Single();

		// Act
		required.IsSelected = false;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(required.IsSelected, Is.True);
			Assert.That(sut.AllRequiredOptionsSelected, Is.True);
		});
	}

	[Test]
	public void IsSelected_ExactlyOneAndSecondOptionSelected_DeselectsFirst()
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.ExactlyOne, CreateOption("A"), CreateOption("B")));

		// Act
		sut.Options[0].IsSelected = true;
		sut.Options[1].IsSelected = true;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Options[0].IsSelected, Is.False);
			Assert.That(sut.Options[1].IsSelected, Is.True);
			Assert.That(sut.AllRequiredOptionsSelected, Is.True);
		});
	}

	[Test]
	public void IsSelected_OptionSelectedByDefault_StartsSelectedAndCanBeDeselected()
	{
		// Arrange
		var option = new ChoiceOption { Name = new ChoiceText.Content("Default"), Description = new ChoiceText.Content(string.Empty), IsSelectedByDefault = true };
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.Any, option));
		var initiallySelected = sut.Options[0].IsSelected;

		// Act
		sut.Options[0].IsSelected = false;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(initiallySelected, Is.True);
			Assert.That(sut.Options[0].IsSelected, Is.False);
		});
	}

	[TestCase(ChoiceSelectionMode.Any, 0, true)]
	[TestCase(ChoiceSelectionMode.AtLeastOne, 0, false)]
	[TestCase(ChoiceSelectionMode.AtLeastOne, 2, true)]
	[TestCase(ChoiceSelectionMode.AtMostOne, 2, false)]
	[TestCase(ChoiceSelectionMode.AtMostOne, 1, true)]
	[TestCase(ChoiceSelectionMode.ExactlyOne, 0, false)]
	[TestCase(ChoiceSelectionMode.All, 1, false)]
	[TestCase(ChoiceSelectionMode.All, 2, true)]
	public void AllRequiredOptionsSelected_SelectedCount_MatchesSelectionMode(ChoiceSelectionMode mode, int selectedCount, bool expected)
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(mode, CreateOption("A"), CreateOption("B")));

		// Act
		foreach (var option in sut.Options.Take(selectedCount))
		{
			option.IsSelected = true;
		}

		// Assert
		Assert.That(sut.AllRequiredOptionsSelected, Is.EqualTo(expected));
	}

	[Test]
	public void IsSelected_UnavailableOptionSelected_RemainsDeselected()
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.Any, CreateUnavailableOption("A")));

		// Act
		sut.Options[0].IsSelected = true;

		// Assert
		Assert.That(sut.Options[0].IsSelected, Is.False);
	}

	[Test]
	public void IsSelected_UnavailableOptionSelectedByDefault_StartsDeselected()
	{
		// Arrange
		var option = new ChoiceOption { Name = new ChoiceText.Content("A"), Description = new ChoiceText.Content(string.Empty), IsSelectedByDefault = true, UnavailableReason = new ChoiceText.Content("Reason") };

		// Act
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.Any, option));

		// Assert
		Assert.That(sut.Options[0].IsSelected, Is.False);
	}

	[Test]
	public void IsSelected_ExactlyOneAndUnavailableOptionSelected_KeepsCurrentSelection()
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.ExactlyOne, CreateOption("A"), CreateUnavailableOption("B")));
		sut.Options[0].IsSelected = true;

		// Act
		sut.Options[1].IsSelected = true;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Options[0].IsSelected, Is.True);
			Assert.That(sut.Options[1].IsSelected, Is.False);
			Assert.That(sut.AllRequiredOptionsSelected, Is.True);
		});
	}

	[Test]
	public void AllRequiredOptionsSelected_AllModeAndEveryAvailableOptionSelected_IsTrue()
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.All, CreateOption("A"), CreateUnavailableOption("B")));

		// Act
		sut.Options[0].IsSelected = true;

		// Assert
		Assert.That(sut.AllRequiredOptionsSelected, Is.True);
	}

	[TestCase(ChoiceSelectionMode.AtLeastOne)]
	[TestCase(ChoiceSelectionMode.ExactlyOne)]
	[TestCase(ChoiceSelectionMode.All)]
	public void IsSelected_LastSelectedOptionDeselected_RemainsSelected(ChoiceSelectionMode mode)
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(mode, CreateOption("A"), CreateOption("B")));
		sut.Options[0].IsSelected = true;

		// Act
		sut.Options[0].IsSelected = false;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Options[0].IsSelected, Is.True);
			Assert.That(sut.AllRequiredOptionsSelected, Is.EqualTo(mode != ChoiceSelectionMode.All));
		});
	}

	[TestCase(ChoiceSelectionMode.AtLeastOne, false)]
	[TestCase(ChoiceSelectionMode.ExactlyOne, false)]
	[TestCase(ChoiceSelectionMode.All, false)]
	[TestCase(ChoiceSelectionMode.Any, true)]
	[TestCase(ChoiceSelectionMode.AtMostOne, true)]
	public void CanToggle_OnlySelectedOption_DependsOnSelectionMode(ChoiceSelectionMode mode, bool expected)
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(mode, CreateOption("A"), CreateOption("B")));

		// Act
		sut.Options[0].IsSelected = true;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Options[0].CanToggle, Is.EqualTo(expected));
			Assert.That(sut.Options[1].CanToggle, Is.True);
		});
	}

	[Test]
	public void CanToggle_ExactlyOneAndAnotherOptionSelected_UnlocksPreviousOption()
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.ExactlyOne, CreateOption("A"), CreateOption("B")));
		sut.Options[0].IsSelected = true;

		// Act
		sut.Options[1].IsSelected = true;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Options[0].IsSelected, Is.False);
			Assert.That(sut.Options[0].CanToggle, Is.True);
			Assert.That(sut.Options[1].CanToggle, Is.False);
		});
	}

	[Test]
	public void CanToggle_RequiredOrUnavailableOption_IsFalse()
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(ChoiceSelectionMode.Any, CreateOption("A", isRequired: true), CreateUnavailableOption("B")));

		// Act
		var canToggle = sut.Options.Select(x => x.CanToggle).ToList();

		// Assert
		Assert.That(canToggle, Is.EqualTo([false, false]));
	}

	[TestCase(ChoiceSelectionMode.AtLeastOne)]
	[TestCase(ChoiceSelectionMode.All)]
	public void IsSelected_OneOfSeveralSelectedOptionsDeselected_IsDeselected(ChoiceSelectionMode mode)
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(mode, CreateOption("A"), CreateOption("B")));
		sut.Options[0].IsSelected = true;
		sut.Options[1].IsSelected = true;

		// Act
		sut.Options[0].IsSelected = false;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Options[0].IsSelected, Is.False);
			Assert.That(sut.Options[1].IsSelected, Is.True);
		});
	}

	[TestCase(ChoiceSelectionMode.Any)]
	[TestCase(ChoiceSelectionMode.AtMostOne)]
	public void IsSelected_LastSelectedOptionDeselectedInOptionalMode_IsDeselected(ChoiceSelectionMode mode)
	{
		// Arrange
		var sut = new ChoiceStepViewModel(CreateStep(mode, CreateOption("A"), CreateOption("B")));
		sut.Options[0].IsSelected = true;

		// Act
		sut.Options[0].IsSelected = false;

		// Assert
		Assert.That(sut.Options[0].IsSelected, Is.False);
	}

	private static ChoiceStep CreateStep(ChoiceSelectionMode mode, params ChoiceOption[] options) => new()
	{
		Title = new ChoiceText.Content("Step"),
		Subtitle = new ChoiceText.Content("Group"),
		SelectionMode = mode,
		Options = options,
	};

	private static ChoiceOption CreateOption(string name, bool isRequired = false) =>
		new() { Name = new ChoiceText.Content(name), Description = new ChoiceText.Content(string.Empty), IsRequired = isRequired };

	private static ChoiceOption CreateUnavailableOption(string name) =>
		new() { Name = new ChoiceText.Content(name), Description = new ChoiceText.Content(string.Empty), UnavailableReason = new ChoiceText.Content("Reason") };
}

using Avalonia.Controls;
using LauncherKit.Desktop.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;

namespace LauncherKit.Desktop.Tests.Controls;

[NonParallelizable]
[TestFixture]
public sealed class LockableCheckBoxTests
{
	[AvaloniaTest]
	public void Toggle_CannotToggle_StaysCheckedAndEnabled()
	{
		// Arrange
		var checkBox = new LockableCheckBox { IsChecked = true, CanToggle = false };

		// Act
		PressSpace(checkBox);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(checkBox.IsChecked, Is.True);
			Assert.That(checkBox.IsEnabled, Is.True);
		});
	}

	[AvaloniaTest]
	public void Toggle_CanToggle_Unchecks()
	{
		// Arrange
		var checkBox = new LockableCheckBox { IsChecked = true, CanToggle = true };

		// Act
		PressSpace(checkBox);

		// Assert
		Assert.That(checkBox.IsChecked, Is.False);
	}

	private static void PressSpace(LockableCheckBox checkBox)
	{
		var window = new Window { Content = checkBox };
		window.Show();
		checkBox.Focus();
		window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
		window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
	}
}

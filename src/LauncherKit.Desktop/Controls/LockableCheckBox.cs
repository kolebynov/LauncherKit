using Avalonia;
using Avalonia.Controls;

namespace LauncherKit.Desktop.Controls;

/// <summary>
/// A <see cref="CheckBox"/> that ignores user toggles while <see cref="CanToggle"/> is <see langword="false"/>
/// but keeps its enabled look, unlike <see cref="InputElement.IsEnabled"/>.
/// </summary>
public sealed class LockableCheckBox : CheckBox
{
	public static readonly StyledProperty<bool> CanToggleProperty =
		AvaloniaProperty.Register<LockableCheckBox, bool>(nameof(CanToggle), defaultValue: true);

	public bool CanToggle
	{
		get => GetValue(CanToggleProperty);
		set => SetValue(CanToggleProperty, value);
	}

	protected override Type StyleKeyOverride => typeof(CheckBox);

	protected override void Toggle()
	{
		if (CanToggle)
		{
			base.Toggle();
		}
	}
}

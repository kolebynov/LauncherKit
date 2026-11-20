using LauncherKit.Desktop.Extensions;
using Avalonia.Controls;

namespace SampleLauncher.App.Views;

public partial class CheckAvailabilityView : UserControl
{
	public CheckAvailabilityView()
	{
		InitializeComponent();
		// Activation starts the check.
		this.UseActivation();
	}
}

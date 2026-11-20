using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.ViewModels.Setup;
using Avalonia.Controls;

namespace SampleLauncher.App.Views;

public partial class FirstSetupView : UserControl
{
	public FirstSetupView()
	{
		InitializeComponent();
		// The sample has no welcome page: the setup starts with its first step.
		this.WhenActivated<FirstSetupViewModel>((vm, _) => vm.Start());
	}
}

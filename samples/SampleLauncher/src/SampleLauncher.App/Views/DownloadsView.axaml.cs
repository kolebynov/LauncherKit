using LauncherKit.Desktop.Extensions;
using Avalonia.Controls;

namespace SampleLauncher.App.Views;

public partial class DownloadsView : UserControl
{
	public DownloadsView()
	{
		InitializeComponent();
		// Activation reads the stored files.
		this.UseActivation();
	}
}

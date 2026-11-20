using LauncherKit.Desktop.Extensions;
using Avalonia.Controls;

namespace SampleLauncher.App.Views;

public partial class ModCategoriesView : UserControl
{
	public ModCategoriesView()
	{
		InitializeComponent();
		// Activation loads the categories from the feed.
		this.UseActivation();
	}
}

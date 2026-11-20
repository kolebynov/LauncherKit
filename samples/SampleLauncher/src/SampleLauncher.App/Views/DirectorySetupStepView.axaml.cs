using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.ViewModels.Setup;
using R3;
using Avalonia.Controls;

namespace SampleLauncher.App.Views;

public partial class DirectorySetupStepView : UserControl
{
	public DirectorySetupStepView()
	{
		InitializeComponent();
		this.WhenActivated<DirectorySetupStepViewModel>((vm, d) =>
		{
			Heading.Text = vm is SkyrimDirectorySetupStepViewModel
				? "Skyrim directory (an empty directory is fine)"
				: "Directory for downloads and temporary files";
			if (vm is SkyrimDirectorySetupStepViewModel skyrim)
			{
				skyrim.ObservePropertyChanged(x => x.Error)
					.Subscribe(error => ErrorText.Text = error is null ? null
						: $"Skyrim {error.DetectedSkyrimVersion} found; {error.RequiredSkyrimVersion} is required.")
					.AddTo(d);
			}
		});
	}
}

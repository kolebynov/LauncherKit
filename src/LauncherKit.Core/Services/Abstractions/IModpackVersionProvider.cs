using System.ComponentModel;
using NuGet.Versioning;

namespace LauncherKit.Core.Services.Abstractions;

public interface IModpackVersionProvider : INotifyPropertyChanged
{
	NuGetVersion? InstalledModpackVersion { get; }

	void RefreshInstalledModpackVersion();
}
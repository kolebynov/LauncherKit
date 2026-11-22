using System.ComponentModel;

namespace LauncherKit.Core.Services.Abstractions;

public interface IApplicationLifetime : INotifyPropertyChanged
{
	IServiceProvider Services { get; }

	void CreateNewScope();

	void Exit();
}
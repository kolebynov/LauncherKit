namespace LauncherKit.Core.Services.Abstractions;

public interface IInstallDetector
{
	bool IsVcRedistInstalled(InstallDetector.VcRedistSupportedArchitecture architecture);
}

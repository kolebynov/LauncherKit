using LauncherKit.Core.Models;

namespace LauncherKit.Core.FileSources;

public interface IHttpFileSourceFactory
{
	IFileSource Create(Uri fileUri, Sha256Hash? hash);
}

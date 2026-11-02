using LauncherKit.Core.Models;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Models;

[TestFixture]
public sealed class DownloadProgressTests
{
	[Test]
	public async Task ReportPauseResume_StateChanges_EmitOnlyActualTransitions()
	{
		// Arrange
		using var sut = new DownloadProgress("file.7z");
		using var recorder = new ObservableRecorder<R3.Unit>(sut.ProgressObservable);

		// Act
		sut.ReportProgress(25, 100, 12.5);
		await sut.Pause();
		await sut.Pause();
		await sut.Resume();
		await sut.Resume();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Operation.Value, Is.EqualTo(new MicroTaskDescription.DownloadFile("file.7z")));
			Assert.That(sut.Progress, Is.EqualTo(0.25));
			Assert.That(sut.Speed, Is.EqualTo(12.5));
			Assert.That(sut.IsPaused, Is.False);
			Assert.That(recorder.Values, Has.Count.EqualTo(3));
		});
	}
}

[TestFixture]
public sealed class OperationProgressTests
{
	[Test]
	public void ReportProgress_NewValue_UpdatesValueAndEmits()
	{
		// Arrange
		using var sut = new OperationProgress(new MicroTaskDescription.ExtractArchive("file.7z"));
		using var recorder = new ObservableRecorder<R3.Unit>(sut.ProgressObservable);

		// Act
		sut.ReportProgress(0.75);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Operation.Value, Is.EqualTo(new MicroTaskDescription.ExtractArchive("file.7z")));
			Assert.That(sut.Progress, Is.EqualTo(0.75));
			Assert.That(recorder.Values, Has.Count.EqualTo(1));
		});
	}
}

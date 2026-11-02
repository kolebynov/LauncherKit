using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Core.Tests.AppTasks;

[TestFixture]
public sealed class AppTaskTests
{
	[Test]
	public void Equality_SameIdDifferentDescriptions_AreEqualAndShareHashCode()
	{
		// Arrange
		var first = CreateTask("same", "First", []);
		var second = CreateTask("same", "Second", []);

		// Act
		var equal = first == second;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(equal, Is.True);
			Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
		});
	}

	[Test]
	public void Dependencies_ConstructedList_IsExposedInOrder()
	{
		// Arrange
		var first = CreateTask("first", "First", []);
		var second = CreateTask("second", "Second", []);

		// Act
		var sut = CreateTask("root", "Root", [first, second]);

		// Assert
		Assert.That(sut.Dependencies, Is.EqualTo(new[] { first, second }));
	}

	[Test]
	public async Task OnComplete_AssignedCallback_CanBeInvokedWithToken()
	{
		// Arrange
		using var cancellation = new CancellationTokenSource();
		var received = CancellationToken.None;
		var sut = new AppTask("root", new AppTaskDescription.Custom("Root"), _ => Array.Empty<IAppMicroTask>(), [], null)
		{
			OnComplete = token =>
			{
				received = token;
				return ValueTask.CompletedTask;
			},
		};

		// Act
		await sut.OnComplete!(cancellation.Token);

		// Assert
		Assert.That(received, Is.EqualTo(cancellation.Token));
	}

	[Test]
	public void Description_CustomText_IsPreservedWithoutFormatting()
	{
		var description = new AppTaskDescription.Custom("Consumer task");
		var task = new AppTask("custom", description, _ => [], [], null);

		Assert.That(task.Description.Value, Is.SameAs(description));
	}

	private static AppTask CreateTask(string id, string name, IReadOnlyList<AppTask> dependencies) =>
		new(id, new AppTaskDescription.Custom(name), _ => Array.Empty<IAppMicroTask>(), dependencies, null);
}

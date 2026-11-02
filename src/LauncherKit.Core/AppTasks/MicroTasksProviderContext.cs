using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Core.AppTasks;

/// <summary>
/// What <see cref="AppTask.MicroTasksProvider"/> builds the task's microtasks with: the services of the scope the task
/// runs in, created when the task starts.
/// </summary>
public sealed class MicroTasksProviderContext
{
	public IMicroTasksFactory MicroTasksFactory { get; }

	/// <summary>Resolves other microtask builders, e.g. <c>IInstallStepsMicroTasksFactory</c>, from the task's scope.</summary>
	public IServiceProvider ServiceProvider { get; }

	public MicroTasksProviderContext(IMicroTasksFactory microTasksFactory, IServiceProvider serviceProvider)
	{
		MicroTasksFactory = microTasksFactory;
		ServiceProvider = serviceProvider;
	}
}

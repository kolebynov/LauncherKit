﻿using LauncherKit.Core.AppTasks.CompleteVerifiers;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Core.AppTasks;

public sealed class AppTask : IEquatable<AppTask>
{
	public string Id { get; }

	public AppTaskDescription Description { get; }

	public Func<MicroTasksProviderContext, IReadOnlyList<IAppMicroTask>> MicroTasksProvider { get; }

	public IReadOnlyList<AppTask> Dependencies { get; }

	public IAppTaskCompleteVerifier? CompleteVerifier { get; }

	public Func<CancellationToken, ValueTask>? OnComplete { get; init; }

	/// <summary>
	/// Reports that the queued task turned out not to be needed, e.g. because of an answer given by an earlier task.
	/// Checked for every queued task each time a task completes successfully; an unneeded task leaves the visible queue
	/// at once, but completes, and lets its dependents run, only after its own dependencies.
	/// </summary>
	public Func<bool>? IsUnneeded { get; init; }

	public AppTask(string id, AppTaskDescription description, Func<MicroTasksProviderContext, IReadOnlyList<IAppMicroTask>> microTasksProvider,
		IReadOnlyList<AppTask> dependencies, IAppTaskCompleteVerifier? completeVerifier)
	{
		Id = id;
		Description = description;
		MicroTasksProvider = microTasksProvider;
		Dependencies = dependencies;
		CompleteVerifier = completeVerifier;
	}

	public bool Equals(AppTask? other)
	{
		if (other is null)
		{
			return false;
		}

		if (ReferenceEquals(this, other))
		{
			return true;
		}

		return Id == other.Id;
	}

	public override bool Equals(object? obj) => ReferenceEquals(this, obj) || obj is AppTask other && Equals(other);

	public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);

	public override string ToString() => $"Id = {Id}, Description = {Description.Value}";

	public static bool operator ==(AppTask? left, AppTask? right) => Equals(left, right);

	public static bool operator !=(AppTask? left, AppTask? right) => !Equals(left, right);
}
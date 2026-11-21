# Single instance and launch handling

`LauncherKit.Desktop` runs one launcher per product, Windows user and interactive session. The code lives in `src/LauncherKit.Desktop/SingleInstance/` and is driven by `LauncherKitProgram.Run`. Every admitted launch, the one that starts the launcher and each later one, is offered as a `LaunchContext` to every registered handler in the running launcher, in registration order.

## Owner election

`LauncherKitProgram.Run<TApp>(args, LauncherProductOptions)` validates the product options, captures the working directory and decides ownership before anything else: no `console.log`, logging or services exist yet.

- The product is identified by `LauncherProductOptions.RegistryKey`, never by path, version or display name. The key is normalized like the .NET registry APIs open it (repeated `\` after the first character collapse, a trailing `\` goes, spaces stay significant), upper-cased (case-insensitive) and hashed with SHA-256. The mutex and pipe name is `LauncherKit-{digest}-{user SID}-{session id}`, so installed, copied, debugged or sandboxed copies of the same product share it.
- The launch that acquires the named `Mutex` (created with `CurrentUserOnly` and `CurrentSessionOnly`) becomes the owner and runs the application. An abandoned mutex (previous owner crashed) counts as acquired. The owner releases it on the same thread after the application exits.
- Every other launch is a second launch and never becomes the owner.

## Launch context

`LaunchContext(IReadOnlyList<string> Args, string WorkingDirectory, bool IsFirstLaunch)` is one invocation: its raw arguments, the working directory of the process that was started, and whether it started the running launcher. The kit neither parses arguments nor normalizes paths: resolve relative file paths against `WorkingDirectory` for both kinds of launch (a relaunch's working directory differs from the owner's), e.g. `Path.GetFullPath(arg, context.WorkingDirectory)`.

## Second launch

1. The launch serializes exactly one `LaunchContext` with `IsFirstLaunch = false`, also when it has no arguments. No consumer code runs, no services are composed and Avalonia never starts.
2. A context whose UTF-8 JSON exceeds 64 KiB (the 4-byte length prefix comes on top) is dropped and the launch exits.
3. Otherwise it connects to the owner's `CurrentUserOnly` named pipe (2 s connect timeout), grants the connected owner process the right to take the foreground (`GetNamedPipeServerProcessId` + `AllowSetForegroundWindow`; a failure only loses the permission) and writes one frame: a little-endian 32-bit length followed by that many bytes of UTF-8 JSON. Writing, flushing and waiting for the pipe to drain (so the owner reads the frame before the client closes) are bounded by a 5 s exchange timeout.
4. The launch exits with `0` whether or not the owner was reached. Nothing is ever answered.

## Owner side

- Before the application starts, the owner creates a bounded `LaunchRequestQueue` (32 contexts), admits its own `LaunchContext` (`IsFirstLaunch = true`) and only then creates a `SingleInstanceListener` on the pipe, so its own launch is always handled first. Its own context counts toward the capacity.
- The listener serves one connection at a time, each bounded by the 5 s exchange timeout, and keeps listening after every connection. A well-framed but invalid context (malformed JSON, a missing or null field or argument, or a forged `IsFirstLaunch: true`), or one the queue does not admit (full, or completed because the launcher is exiting), is skipped and the listener keeps reading that connection. An invalid length or a truncated frame ends that connection (its unread remainder is discarded) and the listener accepts the next one. Drops are logged as warnings that never include frame content; the listener starts before `LauncherKitApp` configures Serilog, so drops in that startup window may go unlogged.
- `LauncherKitApp` registers the queue and the `LaunchRequestPump` as singletons. The pump starts when the main window first opens, so contexts admitted during startup wait and handlers can rely on the window. It dispatches each context on the UI thread, in admission order and one at a time, to every `ILaunchHandler` resolved from the scope current when that context's handling starts, in registration order, with the same context. Each handler's `ValueTask` is awaited before the next handler runs, and the next context waits until every handler of the current one has completed, so a handler may await work (e.g. a dialog) without a later launch's handling overlapping it. A scope reload made while a handler awaits does not change the handlers already resolved for that context; such a handler must not use its scope's services after the reload. The first of them is the kit's own activation handler (see below).
- Without registered handlers contexts are dropped silently. A handler whose `Handle` throws or whose task faults is logged (without the context) with its exception, and the remaining handlers and following contexts still run. Resolving the handlers is not guarded: if a constructor or factory throws, the exception propagates out of the pump's Avalonia dispatcher callback unhandled (the kit installs no `Dispatcher.UnhandledException` handler), so under default unhandled-exception handling it reaches `LauncherKitApp`'s `AppDomain` unhandled-exception log and ends the launcher, like any other unhandled UI-thread exception. A consumer that installs its own dispatcher exception handler may mark it handled, in which case the launcher keeps running.
- On exit the queue stops admitting contexts and disposing the services stops the pump: the token passed to the running handler is cancelled (an `OperationCanceledException` it then throws is not logged), and no further handler or context is processed.

## Consumer API

Implement `ILaunchHandler.Handle(LaunchContext, CancellationToken)` and register it, scoped, with `services.AddLaunchHandler<THandler>()` in `ConfigureServices`. Several handlers may be registered; each gets every launch, in registration order. `AddLaunchHandler` uses `TryAddEnumerable`, so adding the same type again does nothing; other non-keyed `ILaunchHandler` registrations also take part, in their registration order, without that deduplication.

```csharp
[STAThread]
public static int Main(string[] args) => LauncherKitProgram.Run<App>(args, ProductIdentity.CreateOptions());

// In ConfigureServices.
services.AddLaunchHandler<OpenFileLaunchHandler>();

public sealed class OpenFileLaunchHandler(IFileOpener opener) : ILaunchHandler
{
	public async ValueTask Handle(LaunchContext context, CancellationToken cancellationToken)
	{
		foreach (var path in context.Args)
			await opener.Open(Path.GetFullPath(path, context.WorkingDirectory), cancellationToken);
	}
}
```

The handler runs on the UI thread. Later launches wait while it is awaited, so await only work that belongs to handling the launch (e.g. a question to the user) and start long background work (e.g. enqueue a task) instead. The same handler sees the first launch and relaunches, so startup argument handling needs no separate code path; use `IsFirstLaunch` where the two differ. Handlers need not activate the window: the kit's activation handler already ran for relaunches. The kit registers no file association.

## Foreground and the activation handler

`AddLauncherKitDesktop`, which `LauncherKitApp` calls before the consumer's `ConfigureServices`, registers the internal scoped `MainWindowActivationLaunchHandler` through the same `TryAddEnumerable` path, so it is the first `ILaunchHandler` and runs before the consumer's. For every relaunch (`IsFirstLaunch = false`) it restores the main window (`IMainView`) when it is minimized (`WindowState.Normal`; other states are left as they are) and calls `Window.Activate()`; the first launch gets no extra activation. A second launch allows the connected owner to take the foreground, but Windows still decides: the permission exists only when the second launch itself was allowed to take the foreground (e.g. started by the user from Explorer), so focus is not guaranteed.

## Limits

Delivery is best effort. Contexts are lost when the owner cannot be reached: it is exiting, runs at another elevation level (the pipe is current-user-only and the client cannot connect), or stays busy past the timeouts.

## Designer

`LauncherKitProgram.BuildAvaloniaApp<TApp>(LauncherProductOptions)`, returned by the consumer's `Program.BuildAvaloniaApp` for the Avalonia designer, neither takes ownership nor listens for second launches and admits no launch context; the services create an empty queue of their own.

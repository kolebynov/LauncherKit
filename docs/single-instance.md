# Single instance and launch messages

`LauncherKit.Desktop` runs one launcher per product, Windows user and interactive session. The code lives in `src/LauncherKit.Desktop/SingleInstance/` and is driven by `LauncherKitProgram.Run`.

## Owner election

`LauncherKitProgram.Run<TApp>(args, LauncherProductOptions, onSecondLaunch?)` validates the product options and decides ownership before anything else: no `console.log`, logging or services exist yet.

- The product is identified by `LauncherProductOptions.RegistryKey`, never by path, version or display name. The key is normalized like the .NET registry APIs open it (repeated `\` after the first character collapse, a trailing `\` goes, spaces stay significant), upper-cased (case-insensitive) and hashed with SHA-256. The mutex and pipe name is `LauncherKit-{digest}-{user SID}-{session id}`, so installed, copied, debugged or sandboxed copies of the same product share it.
- The launch that acquires the named `Mutex` (created with `CurrentUserOnly` and `CurrentSessionOnly`) becomes the owner and runs the application. An abandoned mutex (previous owner crashed) counts as acquired. The owner releases it on the same thread after the application exits.
- Every other launch is a second launch and never becomes the owner.

## Second launch

1. Without `onSecondLaunch`, the launch exits with `0` at once.
2. Otherwise the callback runs with a `LaunchContext` (this launch's arguments and working directory) and an `ILauncherMessageSender`. `Send<TMessage>(value, JsonTypeInfo<TMessage>)` serializes the message with the consumer's source-generated metadata into a `LaunchRequest` `{Kind, Payload}`, where `Kind` is `TMessage.Kind` and `Payload` is the message's JSON text. A request whose serialized JSON exceeds 64 KiB makes `Send` throw `ArgumentException`; the 4-byte length prefix comes on top of that limit.
3. Any exception from the callback is contained: the messages sent before it still go.
4. When nothing was sent, no connection is made. Otherwise the launch connects to the owner's `CurrentUserOnly` named pipe (2 s connect timeout) and writes each request as a one-way frame: a little-endian 32-bit length followed by that many bytes of UTF-8 JSON. Writing, flushing and waiting for the pipe to drain (so the owner reads the frames before the client closes) are bounded by a 5 s exchange timeout.
5. The launch exits with `0` whether or not the owner was reached. Nothing is ever answered.

## Owner side

- Before the application starts, the owner creates a bounded `LaunchRequestQueue` (32 requests) and a `SingleInstanceListener` on the pipe. The listener serves one connection at a time, each bounded by the 5 s exchange timeout, and keeps listening after every connection. A well-framed but malformed request, or one the queue does not admit (full, or completed because the launcher is exiting), is skipped and the listener keeps reading that connection. An invalid length or a truncated frame ends that connection (its unread remainder is discarded) and the listener accepts the next one. Drops are logged as warnings that never include frame content; the listener starts before `LauncherKitApp` configures Serilog, so drops in that startup window may go unlogged.
- `LauncherKitApp` registers the queue and the `LaunchRequestPump` as singletons. The pump starts when the main window first opens, so requests admitted during startup wait and handlers can rely on the window. It dispatches each request on the UI thread to the `LauncherMessageProcessor` of the scope current at that moment, so scope reloads never see a disposed scope.
- The processor runs the handler registered for the kind. An unknown kind is logged without the kind text; an invalid payload or a failing handler is logged with the full exception and the following messages still run.
- On exit the queue stops admitting requests and disposing the services stops the pump.

## Consumer API

A message type implements `ILauncherMessage`, whose `static abstract string Kind` names it on the wire and selects its handler; keep it stable and unique. Register one scoped `ILauncherMessageHandler<TMessage>` per kind with `AddLauncherMessageHandler<TMessage, THandler>(JsonTypeInfo<TMessage>)`; a blank kind or registering the same kind twice throws `ArgumentException`.

```csharp
public sealed record OpenFile(string Path) : ILauncherMessage
{
	public static string Kind => "MyLauncher.OpenFile";
}

[STAThread]
public static int Main(string[] args) => LauncherKitProgram.Run<App>(args, ProductIdentity.CreateOptions(),
	(launch, sender) =>
	{
		foreach (var path in launch.Args)
			sender.Send(new OpenFile(Path.GetFullPath(path, launch.WorkingDirectory)), MyJsonContext.Default.OpenFile);
	});

// In ConfigureServices; OpenFileHandler implements ILauncherMessageHandler<OpenFile>.
services.AddLauncherMessageHandler<OpenFile, OpenFileHandler>(MyJsonContext.Default.OpenFile);
```

Handlers run on the UI thread and should only start the work (e.g. enqueue a task). The first launch ignores `onSecondLaunch`: handle its own arguments in your startup code. The kit registers no file association.

## No built-in activation

The kit sends no message of its own and has no bring-to-front logic: a second launch without messages just exits. A consumer that wants the running launcher to come to the front sends its own message whose handler activates the main window. Windows may refuse the foreground unless the second launch first allows it, e.g. with `AllowSetForegroundWindow`.

## Limits

Delivery is best effort. Messages are lost when the owner cannot be reached: it is exiting, runs at another elevation level (the pipe is current-user-only and the client cannot connect), or stays busy past the timeouts.

## Designer

`LauncherKitProgram.BuildAvaloniaApp<TApp>(LauncherProductOptions)`, returned by the consumer's `Program.BuildAvaloniaApp` for the Avalonia designer, neither takes ownership nor listens for second launches; the services create an empty queue of their own.

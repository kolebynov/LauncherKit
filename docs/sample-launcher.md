# Sample launcher

`samples/SampleLauncher` is a small launcher for trying kit changes without a consumer checkout. It covers the first
setup, a feed-backed Home and one real download through the kit task queue and Downloads storage, driven by Python
development automation. It does not install or update a modpack, start the game, collect logs or build an installer.
Real consumers still own those flows (see [dev automation](dev-automation.md)).

The sample is not part of `LauncherKit.sln`: the kit solution builds and tests without it and without its generated
files. It references kit projects and `msbuild/LauncherKit.Product.props` by relative path and takes the root
`Directory.Build.props` and `Directory.Packages.props` settings.

## Layout

| Path | Contents |
|---|---|
| `SampleLauncher.Product.props` | The sample's own identity: `SAMPLE_LAUNCHER_` prefix, `SampleLauncher.App.exe`, the `HKEY_CURRENT_USER\Software\LauncherKit Sample\Sample Launcher` registry key. |
| `src/SampleLauncher.App` | The app. It has one plain Avalonia window with Home, Tasks and Downloads buttons, a view registry, a yes/no `IDialogService` and a root directory manager whose root is the parent of the working directory. The window is the `INotificationManagerProvider`, and `App.axaml` holds the `AppNotification` toast template (`NotificationTitle`, `NotificationMessage`). |
| `src/SampleLauncher.App/SampleLaunchHandler.cs` | The single-instance `ILaunchHandler`: for every later launch (not its own first launch) the running launcher shows a `Launched again` toast listing that launch's working directory and arguments. Bringing the window to the front is done by the kit's activation handler, registered before the sample's, not by this handler. |
| `scripts/make_mocks.py` | Writes the ignored `scripts/Mock`: `feedV12.json` (one category, `Sample Tools`), `Test.zip` for the setup's server check and `Sample/sample-download.txt`. |
| `scripts/launcher_flows.py` | Runs the kit's `scripts/launcher_dev.py` CLI with the sample's defaults, plus the `setup`, `home`, `download`, `smoke` and `second-launch` flows. |

The download task (`SampleDownload.cs`) downloads `Sample/sample-download.txt` with a SHA-256 check. `make_mocks.py`
fails when the hash compiled into the app does not match the file it generates.

## Running

Requirements: Windows x64, the .NET 11 SDK and Python 3.12 or later. The NativeAOT run also needs the C++ workload.
Run these commands from `samples/SampleLauncher`:

```powershell
python scripts\make_mocks.py
python scripts\launcher_flows.py start --fresh          # build with dev automation, serve scripts\Mock on 127.0.0.1:8190
python scripts\launcher_flows.py smoke                  # first setup, Home category, new sample download
python scripts\launcher_flows.py start --no-build       # warm restart: the setup and the download are kept
python scripts\launcher_flows.py smoke --configured     # Home without setup, then the download (cache hit or transfer)
python scripts\launcher_flows.py second-launch          # a later launch exits 0; the running launcher shows a toast
python scripts\launcher_flows.py stop --mock
```

`second-launch` runs `scripts\Sandbox\current\Launcher\SampleLauncher.App.exe` again from
`scripts\Sandbox\current\SecondLaunch` with a unique `--second-launch-test=...` argument, then clicks the toast to close
it. It needs a running sandbox launcher (without one it refuses before launching anything) and fails, with a nonzero
exit code, when:

- the second launch does not exit within `--timeout` seconds (15) or exits with a code other than `0`;
- within `--timeout`, the running launcher shows no toast containing the argument, or the toast does not name the
  working directory, or that same toast's own title (the `NotificationTitle` right before its `NotificationMessage`;
  other toasts' titles do not count) is not `Launched again`;
- the automation API then reports another process ID than before the launch, or that process is gone;
- the clicked toast does not close within 10 s.

It is not part of `smoke`. The sample's toasts also show the kit's feed loading progress and errors.

`smoke` fails, with a nonzero exit code, in these cases:

- the first setup is not shown (or, with `--configured`, is shown);
- a fresh `smoke` (first setup shown) finds `sample-download.txt` already in the data directory, or the mock server
  selected by the current `start` was not started by the sandbox (no sandbox log) or logs no request for it;
- the download server check fails;
- Home does not show the feed's `Sample Tools` category;
- the download task does not complete;
- Downloads does not list `sample-download.txt` as `Complete`;
- the stored file under the data directory's `Downloads` has the wrong SHA-256.

The data directory is the one the launcher saved in `appsettings.override.json` when it has one (a resumed setup then
skips the directory step), otherwise `--data` or `scripts\Sandbox\current\Data`; a `--data` differing from the saved
directory is rejected.

The fresh `smoke` is the download test. Other runs report what happened from the destination before the run and the
log of the mock server selected by the current `start`: `Downloaded`, `Downloaded again over the pre-existing file`, or
`Cache hit: kept the pre-existing file without a request` (the kit's usual result after a warm restart). When that
server is not one the sandbox started (another running server was reused), the output says the transfer is unknown.

The sample uses its own ports, API `8195` and mock feed `8190`, so it does not collide with other launchers' defaults.
`start` uses the server already answering the feed on the mock port, if any, and checks the selected server after
selecting it and before launching the app: it must serve `feedV12.json`, `Test.zip` and `Sample/sample-download.txt`
byte for byte as in `scripts\Mock`; otherwise, or if it cannot fetch them, `start` refuses.
The kit's `start` accepts only an automation API reporting the started process ID, and every command checks that the
API reports the process ID of the sample sandbox's launcher before sending commands.

The setup always gets explicit directories, `current\Skyrim` (an empty directory) and `current\Data` in the sandbox, so
it never uses an installed Skyrim; the sample app also registers a Skyrim directory finder that suggests nothing, so a
manual setup never prefills a real installation. The setup flow starts from the step shown: either directory step, or
only the download server check (after **Continue without downloads**, the kit keeps the directories), or Home when an
interrupted setup already passed the check. `setup`, `home` and
`download` run single steps. The generic commands also work, e.g. `tree`, `click --id DownloadSampleButton`,
`snapshot save NAME`, `log` and `start --aot --fresh`.

Like any launcher, the sample writes its data directories under its registry key, which the sandbox does not
virtualize. Delete the key with `reg delete "HKCU\Software\LauncherKit Sample" /f` when it is no longer needed.

To run the built app by hand, serve the mocks with
`python -m http.server 8190 --bind 127.0.0.1 --directory scripts\Mock`. The app's `appsettings.json` points at that
address. Analytics also points at the mock server, and its `/v1/installations` POST (Release builds only) is not
implemented, so it is not tested.

Release publish without the automation server:

```powershell
dotnet publish src\SampleLauncher.App\SampleLauncher.App.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true -p:EnableDevAutomation=false
```

# Dev automation

A local HTTP API inside an automation-enabled launcher reads and drives its UI. The reusable tools are
`kit/scripts/launcher_dev.py` (sandbox/CLI) and `kit/scripts/launcher_client.py` (Python API client).
Consumer-specific user flows, their configured CLI, mock generation and serving are consumer-owned. Prefer these
tools over OS-level mouse scripts or screen captures. All commands below run from the consumer repository root. See
[kit contracts](../AGENTS.md).

## Build gating

The API lives in `kit/src/LauncherKit.DevAutomation`. The launcher references it, and defines `DEV_AUTOMATION` for its
`LauncherKitApp.StartDesktopServices` override that starts the server, only when it is built with `-p:EnableDevAutomation=true`.
`PublishLauncher` inherited from `LauncherKit.Build.LauncherBuild` passes `EnableDevAutomation=false` as a global property, so release builds never
contain it, even if an environment variable sets it. The solution build still compiles the project, so it keeps up
with API changes.

Even when compiled in, the server starts only when `DevAutomation:Port` is configured (`<EnvironmentPrefix>DevAutomation__Port`). It
listens on `http://localhost:{port}/` only. POST bodies must be `application/json`, which a web page cannot send
cross-origin without a CORS preflight, and the server never answers preflights.

Input goes through Avalonia's platform input callback (`ITopLevelImpl.Input`), the same path the OS window uses, so
it gets hit testing, pointer-over, capture and focus without moving the real cursor or bringing the window to the
front. This needs Avalonia private APIs (`AvaloniaAccessUnstablePrivateApis`) and `[UnsafeAccessor]` for members
that Avalonia 12 made internal (`TopLevel.InputRoot`, `Popup.Host`, `KeyboardDevice.Instance`). Recheck them when
updating Avalonia.

## Sandbox

The generic CLI requires explicit consumer identity on `start`; it has no fallback project, executable or environment prefix:

```powershell
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox start --project src\MyLauncher.App\MyLauncher.App.csproj --exe-name MyLauncher.App.exe --environment-prefix MY_ --mock-dir scripts\Mock --fresh
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox tree
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox snapshot save configured
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox stop --mock
```

Those names are placeholders, not kit defaults; each consumer supplies its own values, typically from its own flow CLI. `--sandbox-dir`
defaults to `Sandbox` under the current working directory. `start` builds/publishes to an explicit output (`--output`),
defaulting to `<sandbox>/build/Debug`, or `Release` with `--aot`; `--no-build` consumes that same output. There is no
inferred target-framework output directory. Build output is copied to `<sandbox>/current/Launcher`, used as the
working directory. Repeat `--state-name GLOB` to exclude consumer runtime-state files from copied build output.

`--mock-dir PATH` opts into serving a consumer's mock files on port 8090 and overriding feed/analytics endpoints;
without it the configured real endpoints are used. `--real-feed` disables configured mocks. `--feed FILE` selects
an alternate feed in the configured mock directory. Window size/position settings use the supplied environment
prefix. `--fresh` empties state, while `--snapshot NAME` restores a saved state; otherwise current state is preserved.
`snapshot save NAME` stops the launcher and saves databases/settings/game/data without build output. Snapshots live
at `<sandbox>/snapshots`, screenshots at `<sandbox>/screenshots`, and CLI state at `<sandbox>/state.json`.
Restores use the same absolute paths, preserving installation records.
The NativeAOT automation output belongs to the selected sandbox build directory.


## Commands

Elements come from a semantic tree:

- interactive controls (those with an invoke, toggle, value, range, selection-item or expand automation pattern),
  shown with the joined text of their non-interactive content;
- texts;
- controls with an `AutomationId`, an `x:Name` outside templates, or a tooltip;
- views (`UserControl`).

Layout-only visuals are skipped. Every visible window and every open popup is a root: root 0 is the main window,
then the other windows (e.g. task dialogs), then popups (flyouts, menus, tooltips). Bounds are root-local
device-independent pixels.

```text
root 0 window "My Launcher 1.0.0" 1000x700
  #8 FANavigationViewItem id=SettingsNavItem [-1,632 72x60] tip="Settings"
        #17 Button "Install" [105,369 96x32] disabled
root 1 popup 259x102
  #44 FAMenuFlyoutItem id=SettingsMenuItem [r1:1,67 257x32]
```

Selectors: a positional displayed text (exact) or `#N` (an element's ref), or `--id` (AutomationId or `x:Name`),
`--text`, `--contains`, `--tip`, `--type`, combined with `--index` to pick one of several matches. A ref stays with
its control for the launcher's lifetime, so a ref from any output (tree, find, wait, an action, an error's
candidates) keeps naming the same element.

| Command | Effect |
|---|---|
| `tree [--root N] [--hidden] [--full] [--json]` | Print the tree. |
| `find SELECTOR` | List the matches. |
| `click SELECTOR` / `click --at X Y [--root N]` | Real pointer click at the element center (`--right`, `--double`, `--mod`). Prints the visual that got the click. |
| `hover`, `wheel [--delta]` | Pointer move or wheel. The pointer stays where it was moved, so hover-only UI (mod card buttons) stays open. |
| `invoke SELECTOR` | Invoke, toggle, select or expand through the automation peer, bypassing hit testing. |
| `text SELECTOR VALUE` | Set an editable value (text box), or a number in range (number box, slider), through the automation peer. |
| `key KEY [--mod Control]`, `type TEXT` | Key press (Avalonia `Key` name) or text input to the focused element. |
| `scroll SELECTOR` | `BringIntoView`. |
| `wait [SELECTOR] [--state present\|absent\|enabled\|disabled] [--tasks N] [--timeout S]` | Wait for an element state and/or the task queue's `TaskCount`. |
| `screenshot [SELECTOR] [-o FILE] [--scale S]` | PNG of the main window with open popups, or of one element. Default scale 1 makes pixels equal pointer coordinates. The image grows right and down for popups past the window. |

Every request first waits for a rendered frame, because input hit testing uses the last rendered frame. Actions on a
selected element (`click`, `hover`, `wheel`, `invoke`, `text`, `scroll`, element screenshots) retry each frame for up
to 5 s while the element is missing, disabled, covered, or moving: it must stay at the same place for two frames, so
page transitions and dialog animations finish first. A click fails with 409 when another element would get it. A
click on an element that is transparent to input (a menu item's label) passes when its ancestor would handle it. The
printed hit shows which visual received the click, e.g. a `SelectableTextBlock` that takes the press for text
selection instead of toggling its check box.

## Python client

`kit/scripts/launcher_client.py` (standard library only) wraps the API in `LauncherClient`. The CLI uses it too, so write
a flow as a short script instead of chaining commands or sending raw HTTP:

```python
import sys
sys.path.insert(0, 'kit/scripts')
from launcher_client import AutomationError, LauncherClient

launcher = LauncherClient()                       # port 8095, the sandbox default
launcher.click('Install')
launcher.click(contains='Graphics pack', type='LockableCheckBox')
launcher.click('Finish')
launcher.wait(task_count=0, timeout=600)
card = launcher.find(type='ModCardView')[0]
launcher.hover(card)                              # a Node from find/tree is a target too
launcher.click(id='InstallButton', index=0)
launcher.screenshot('scripts/Sandbox/screenshots/card.png', card)
print(launcher.tree().format())
```

- Methods: `status`, `tree`, `find`, `click`, `hover`, `wheel`, `pointer` (`Down`/`Up` for drags), `invoke`,
  `set_text`, `key`, `type_text`, `scroll`, `wait` and `screenshot`. Each matches the CLI command of the same name.
- A target is a displayed text, `'#N'` or an `int` ref, or a `Node`. The keywords `id`, `text`, `contains`, `tip`,
  `type`, `ref` and `index` work as the CLI options do, and `at=(x, y), root=N` gives coordinates instead.
- Results are frozen dataclasses: `Tree` (`roots`, `walk()`, `format()`), `Root`, `Node` (`walk()`, `describe()`,
  `bounds.center`), `PointerResult` (`target`, `hit`), `WaitResult` and `Status`.
- A refused request raises `AutomationError` with `status`, `message` and, for ambiguous selectors, `candidates`.

## HTTP API

| Method and path | Body / query | Response |
|---|---|---|
| `GET /status` | | process ID, title, client size, render scaling, `taskCount` |
| `GET /tree` | `?hidden=1` | `roots[]` with nested `children` |
| `POST /find` | selector | `matches[]` |
| `POST /pointer` | selector or `x`, `y`, `root`; `action` (`Click`, `Move`, `Down`, `Up`, `Wheel`), `button`, `double`, `delta`, `modifiers` | `root`, `x`, `y`, `target`, `hit` |
| `POST /invoke`, `POST /scroll` | selector | `element` |
| `POST /text` | selector + `value` | `element` |
| `POST /key` | `key` or `text`, `modifiers`, `root` | `focused` |
| `POST /wait` | selector + `state`, `taskCount`, `timeout` (s) | `matches[]`, `taskCount` |
| `GET /screenshot`, `POST /screenshot` | selector, `scale` | `image/png` |

A selector is `ref`, `id`, `text`, `contains`, `tip`, `type` and `index`. Errors are `{"error": ..., "candidates":
[...]}` with 400 (bad request), 404 (no match), 408 (wait timed out), 409 (ambiguous, disabled, covered), 410 (ref
of a control that no longer exists) or 415 (body not JSON). Element responses show the element as found, before the
action.


### Feed editor sandbox

Check feed editor changes without R2 credentials with `kit/scripts/feed_editor_dev.py`. It serves a fake R2 bucket (an in-memory S3 subset on `http://127.0.0.1:5005`, explicit `--bucket`: `GetObject`, `HeadObject`, `PutObject` with `If-Match`, `ListObjectsV2`, multipart uploads through presigned URLs, and CORS for the browser upload; signatures are not checked) and runs the editor on `http://127.0.0.1:5090` pointed at it (`--port`, `--r2-port`; `--no-editor` serves only the bucket for an editor started elsewhere).

Every run serves the feed `feedV11.json` from `kit/tests/LauncherKit.FeedEditor.Tests/Fixtures/feedV11.json` (`--feed`; its object key is `R2:FeedKey`). Every bucket path named in the feed, including each localized markdown path, exists with small fixed content and SHA-256 metadata. `Smoke/no-checksum.7z`, `Smoke/invalid-checksum.7z` and `Smoke/uppercase-checksum.7z` cover other metadata cases. Any access key signs in except `denied`. Saves and uploads live in memory until restart. The script uses only the Python standard library.

```powershell
python kit\scripts\feed_editor_dev.py --bucket my-bucket
```

Browser smoke after integration: run the sandbox, open `http://127.0.0.1:5090`, sign in with any nonempty credentials except access key `denied`, and edit `DefaultLanguage` plus the values in both languages of mod names/descriptions, recommendations and changelog paths. Each language is a row labelled `{field} ({language})` with its delete button, followed by the row adding a language; it must line up with plain fields in neighbouring columns. Add/remove component and prerequisite entries, choose each component kind/mod reference, change numeric order and support ranges, and save/reload. Confirm missing default translations, unknown mod references, duplicate IDs, invalid ranges and invalid download hashes block saving. Open a second browser context on the same page, save a change there, then save in the first to exercise conflict cancel/overwrite/reload. Reloading or leaving the page must warn about unsaved changes.

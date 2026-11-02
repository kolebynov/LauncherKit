# Dev automation

A local HTTP API inside an automation-enabled launcher reads and drives its UI. The reusable tools are
`kit/scripts/launcher_dev.py` (sandbox/CLI) and `kit/scripts/launcher_client.py` (Python API client).
STB-specific user flows and their configured CLI remain in `scripts/launcher_flows.py`; mock generation and serving
remain consumer-owned. Prefer these tools over OS-level mouse scripts or screen captures. All commands below run
from the consumer repository root. See [kit contracts](../AGENTS.md) and [STB mock/release guidance](../../AGENTS.md).

## Build gating

The API lives in `kit/src/LauncherKit.DevAutomation`. The launcher references it, and defines `DEV_AUTOMATION` for the
start-up hook in `App.OnFrameworkInitializationCompleted`, only when it is built with `-p:EnableDevAutomation=true`.
`PublishLauncher` inherited from `LauncherKit.Build.LauncherBuild` passes `EnableDevAutomation=false` as a global property, so release builds never
contain it, even if an environment variable sets it. The solution build still compiles the project, so it keeps up
with API changes.

Even when compiled in, the server starts only when `DevAutomation:Port` is configured (`<EnvironmentPrefix>DevAutomation__Port`; STB uses `STB_DevAutomation__Port`). It
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
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox start --project src\STBLauncher.App\STBLauncher.App.csproj --exe-name STBLauncher.App.exe --environment-prefix STB_ --mock-dir scripts\Mock --fresh
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox tree
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox snapshot save configured
python kit\scripts\launcher_dev.py --sandbox-dir scripts\Sandbox stop --mock
```

Those names are an explicit STB example, not kit defaults. Another consumer supplies its own values. `--sandbox-dir`
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

STB supplies its identity, mock directory, sandbox path and `launcher.db*` state exclusions in the consumer flow CLI.
It retains generic sandbox commands as well as the STB flows:

```powershell
python scripts\launcher_flows.py start --fresh
python scripts\launcher_flows.py start --aot
python scripts\launcher_flows.py snapshot save configured
python scripts\launcher_flows.py start --snapshot configured --no-build
python scripts\launcher_flows.py start --feed feedV12-stb-3.3.0.json
python scripts\launcher_flows.py log -n 50
python scripts\launcher_flows.py stop --mock
```

The consumer CLI uses `scripts/Sandbox` and `scripts/Mock`. Its stage-feed example installs an older STB; restarting
with default `feedV12.json` offers the update. See [Mock install archives](../../AGENTS.md#mock-install-archives).
The NativeAOT automation output now belongs to the selected sandbox build directory, not `out/dev-automation`.

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
root 0 window "STB Launcher 1.23.0" 1000x700
  #8 FANavigationViewItem id=SettingsNavItem [-1,632 72x60] tip="Настройки"
        #17 Button "Установить" [105,369 96x32] disabled
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
launcher.click('Установить')
launcher.click(contains="MD's Graphics Lite", type='LockableCheckBox')
launcher.click('Завершить')
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

## STB consumer flows

`scripts/launcher_flows.py` performs whole user actions with `LauncherFlows` and waits for their tasks. Prefer a flow
to chaining clicks, and add one here when a sequence repeats. `python scripts/launcher_flows.py <command>` exposes each flow; they are not generic kit commands.

| Command | `LauncherFlows` | Effect |
|---|---|---|
| `setup [--skyrim DIR] [--data DIR] [--offline]` | `first_setup` | First setup through the availability check; defaults to the sandbox `Skyrim` and `Data` directories. |
| `state` | `home_state` | Installed STB, offered install/patch/major update, launcher update, mod cards (name, section, button, uninstall). |
| `open PAGE` | `open` | `home`, `tasks`, `downloads`, `changelog` or `settings`. |
| `install-stb [--version V] [--choice S=O]...` | `install_stb` | "Установить", picking STB `V` (the latest version including patches) when the feed offers several main versions, then the whole chain. |
| `install-other-stb [--version V] [--choice S=O]...` | `install_other_stb` | "Установить другую версию STB" from the "Играть" menu, then the whole chain. |
| `update-stb [--choice S=O]...` | `update_stb` | The offered major update ("Обновить") or patch, confirming its dialog. |
| `install-mod NAME [--choice S=O]...` | `install_mod` | Install or update a graph pack or optional mod from its card, confirming the warning. |
| `uninstall-mod NAME` | `uninstall_mod` | The card's "Удалить". |
| `mod-info NAME` | `mod_description` | Text of the card's "Подробнее". |
| `delete-saves [--keep N]` | `delete_old_saves` | "Удалить старые сохранения"; prints the notification. |
| `uninstall-stb` | `uninstall_stb` | "Удалить STB". |
| `tasks`, `cancel-task NAME`, `resume-task NAME` | `tasks`, `cancel_task`, `resume_task` | The task queue page; `NAME` is the start of the task text. |
| `downloads`, `delete-downloads NAME...\|--all` | `downloads`, `delete_downloads` | The downloads page. |
| `changelog [VERSION]` | `changelog` | Text of an STB changelog. |
| `close-dialogs [--timeout S]` | `close_dialogs` | Close "Что нового" and other dialogs with a "Закрыть" button. |

The home page's "Графпаки" and "Опциональные моды" sections start collapsed. Expand their `ToggleButton` headers
before selecting a card, e.g. `launcher.click(contains='Графпаки', type='ToggleButton')`. Both lists wrap cards onto
the next row when the viewport is too narrow, with 8 px between cards and rows. Both headers stay visible even
when their lists are empty. A section receives its view model only while expanded, activating it through the
normal view lifecycle; collapsing clears its data context and deactivates it. An indeterminate progress bar is
shown while that section loads its cards. The `state` and card-action flows expand these sections and wait for
loading to finish.
Each section view model owns an instance of the shared `Loader` helper, exposed through `Loader.IsLoading`. Loading operations hold
`using var loading = Loader.BeginLoading()` scopes; overlapping operations keep the indicator active until the
last scope is disposed, including when a cancelled operation finishes after a section is reopened.

Task flows answer every choice step that appears while the queue runs, the STB components and the graph pack's FOMOD
alike. `--choice STEP=OPTION` (Python: `{'STEP': 'OPTION'}`, or a list for several options) names the step by its
title and the option by its name; the step then gets exactly those options. A step that is not given keeps its
default selection, and one without a default fails with the list of its options. The CLI prints every step with the
options it was left with and warns about given steps that no dialog showed. A failed task raises
`TaskFailedError` with the error shown in the queue, leaving it there for `resume_task`/`cancel_task`.

V12 component titles follow the launcher's current UI culture (then parent cultures, then the feed's default).
Use the titles actually printed by the flow: the bilingual mocks show `English voices` and `Graphics pack`
on an English host, so their overrides are `--choice "English voices=Install"` and
`--choice "Graphics pack=MD's Graphics Lite"`. The example below uses Russian feed titles. FOMOD titles
come from the archive and may remain Russian independently of the feed language.

```python
import sys
sys.path.insert(0, 'scripts')
from launcher_flows import LauncherFlows

flows = LauncherFlows()
flows.install_stb({'Графпак': "MD's Graphics Lite", 'Английская озвучка': 'Установить', 'HDR': 'Нет', 'Хвосты': 'Нет',
                   'Музыка': 'Нет'})
assert flows.home_state().installed_stb == '3.3.1'
```

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

Check feed editor changes without R2 credentials with `kit/scripts/feed_editor_dev.py`. It serves a fake R2 bucket (an in-memory S3 subset on `http://127.0.0.1:5005`, explicit `--bucket` (STB uses `stb`): `GetObject`, `HeadObject`, `PutObject` with `If-Match`, `ListObjectsV2`, multipart uploads through presigned URLs, and CORS for the browser upload; signatures are not checked) and runs the editor on `http://127.0.0.1:5090` pointed at it (`--port`, `--r2-port`; `--no-editor` serves only the bucket for an editor started elsewhere).

Every run serves V12 `feedV12.json` from `kit/tests/LauncherKit.FeedEditor.Tests/Fixtures/feedV12.json` (`--feed`) and frozen V11 `feedV11.json` from that directory's `feed.json` (`--legacy-feed`). The editor's `/` page edits V12; `/legacy` edits only V11 launchers. Both object keys are configured separately through `R2:FeedKey` and `R2:LegacyFeedKey`. Every bucket path named in either feed, including each localized markdown path, exists with small fixed content and SHA-256 metadata. `Smoke/no-checksum.7z`, `Smoke/invalid-checksum.7z` and `Smoke/uppercase-checksum.7z` cover other metadata cases. Any access key signs in except `denied`. Saves and uploads live in memory until restart. The script uses only the Python standard library.

```powershell
python kit\scripts\feed_editor_dev.py --bucket stb
```

Browser smoke after integration: run the sandbox, open `http://127.0.0.1:5090`, sign in with any nonempty credentials except access key `denied`, and edit `DefaultLanguage` plus both language columns of mod names/descriptions, recommendations and changelog paths. Add/remove component and prerequisite entries, choose each component kind/mod reference, change numeric order and support ranges, and save/reload. Confirm missing default translations, unknown mod references, duplicate IDs, invalid ranges and invalid download hashes block saving. Open a second browser context on the same page, save a change there, then save in the first to exercise conflict cancel/overwrite/reload. Visit `/legacy`, sign in again, edit launcher scalars/downloads, save and reload; compare the JSON preview's non-`Launchers` properties with the frozen V11 fixture. Repeat conflict/sign-out/discard behavior on this page. Page switches must warn about unsaved changes and require fresh sign-in; legacy opaque fields must never appear as editable or cause validation errors.

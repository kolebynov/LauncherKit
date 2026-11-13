"""Build and run a consumer launcher in a sandbox and drive its dev automation API.

Start requires --project, --exe-name and --environment-prefix supplied by the consumer.
Use --sandbox-dir before the command to isolate state, snapshots and screenshots.
An optional --mock-dir serves a local feed; otherwise the launcher's configured servers are used.
"""

import argparse
import dataclasses
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime
from pathlib import Path

from launcher_client import DEFAULT_PORT, AutomationError, LauncherClient, PointerResult
SCRIPT_DIR = Path(__file__).resolve().parent
SANDBOX_DIR = Path.cwd() / 'Sandbox'
CURRENT_DIR = SANDBOX_DIR / 'current'
LAUNCHER_DIR = CURRENT_DIR / 'Launcher'
SNAPSHOTS_DIR = SANDBOX_DIR / 'snapshots'
SCREENSHOTS_DIR = SANDBOX_DIR / 'screenshots'
STATE_FILE = SANDBOX_DIR / 'state.json'

DEFAULT_MOCK_PORT = 8090
DEFAULT_WIDTH = 1000
DEFAULT_HEIGHT = 700
# Settings and log files are runtime state, not build artifacts.
STATE_NAMES = ('appsettings.override.json', 'logs', 'console.log')


def main(configure=None, flow_errors=()) -> int:
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    args = parse_args(configure)
    try:
        return args.handler(args) or 0
    except AutomationError as error:
        print_api_error(error)
        return 1
    except urllib.error.URLError as error:
        print(f'error: the automation API is not reachable ({error.reason}); start the sandbox launcher first',
              file=sys.stderr)
        return 1
    except (SandboxError, *flow_errors) as error:
        print(f'error: {error}', file=sys.stderr)
        return 1


# ----- sandbox -----

class SandboxError(Exception):
    pass


def load_state() -> dict:
    return json.loads(STATE_FILE.read_text(encoding='utf-8')) if STATE_FILE.exists() else {}


def save_state(state: dict) -> None:
    SANDBOX_DIR.mkdir(parents=True, exist_ok=True)
    STATE_FILE.write_text(json.dumps(state, indent=2), encoding='utf-8')


def is_running(pid: int | None) -> bool:
    if not pid:
        return False
    # Bytes: tasklist writes in the console code page.
    result = subprocess.run(['tasklist', '/FI', f'PID eq {pid}', '/NH', '/FO', 'CSV'], capture_output=True)
    return f'"{pid}"'.encode('ascii') in result.stdout


def kill(pid: int | None) -> None:
    if is_running(pid):
        # /T also ends the processes the launcher started (Mod Organizer, 7-Zip, ...).
        subprocess.run(['taskkill', '/PID', str(pid), '/T', '/F'], capture_output=True)
        deadline = time.monotonic() + 10
        while is_running(pid) and time.monotonic() < deadline:
            time.sleep(0.2)


def build(args) -> Path:
    output = args.output.resolve()
    command = ['dotnet', 'publish' if args.aot else 'build', str(args.project.resolve()),
               '-c', 'Release' if args.aot else 'Debug', '-p:EnableDevAutomation=true',
               '-o', str(output), '-nologo', '-v', 'q']
    if args.aot:
        command.extend(['-r', 'win-x64', '--self-contained', '-p:PublishAot=true'])
    print('Building:', ' '.join(command))
    if subprocess.run(command, cwd=args.project.resolve().parent).returncode != 0:
        raise SandboxError('build failed')
    return output


def copy_build(output: Path, exe_name: str, state_names: tuple[str, ...]) -> None:
    if not (output / exe_name).exists():
        raise SandboxError(f'{output / exe_name} not found; build first (omit --no-build)')
    shutil.copytree(output, LAUNCHER_DIR, dirs_exist_ok=True, ignore=shutil.ignore_patterns(*state_names))


def ensure_mock_server(port: int, state: dict, mock_dir: Path, feed: str) -> str:
    url = f'http://127.0.0.1:{port}/'
    if url_responds(url + feed):
        return url
    if not (mock_dir / feed).is_file():
        raise SandboxError(f'{mock_dir / feed} not found; generate the consumer mocks first')
    log = open(SANDBOX_DIR / 'mock-server.log', 'ab')
    process = subprocess.Popen(
        [sys.executable, '-m', 'http.server', str(port), '--bind', '127.0.0.1', '--directory', str(mock_dir)],
        stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
        creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP)
    state['mockPid'] = process.pid
    state['mockPort'] = port
    deadline = time.monotonic() + 10
    while not url_responds(url + feed):
        if time.monotonic() > deadline:
            raise SandboxError(f'mock server did not start on port {port}; see {SANDBOX_DIR / "mock-server.log"}')
        time.sleep(0.2)
    print(f'Mock feed server: {url} (pid {process.pid})')
    return url


def url_responds(url: str) -> bool:
    try:
        with urllib.request.urlopen(urllib.request.Request(url, method='HEAD'), timeout=2):
            return True
    except (urllib.error.URLError, OSError):
        return False


def cmd_start(args) -> int:
    SANDBOX_DIR.mkdir(parents=True, exist_ok=True)
    state = load_state()
    kill(state.get('pid'))
    output = args.output.resolve()
    if not args.no_build:
        output = build(args)

    if args.snapshot:
        snapshot = SNAPSHOTS_DIR / args.snapshot
        if not snapshot.is_dir():
            raise SandboxError(f'snapshot {args.snapshot} not found; existing: {", ".join(list_snapshots()) or "none"}')
        remove_tree(CURRENT_DIR)
        shutil.copytree(snapshot, CURRENT_DIR)
    elif args.fresh:
        remove_tree(CURRENT_DIR)
    LAUNCHER_DIR.mkdir(parents=True, exist_ok=True)
    state_names = (*STATE_NAMES, *args.state_name)
    copy_build(output, args.exe_name, state_names)

    env = os.environ.copy()
    prefix = args.environment_prefix
    env.update({
        f'{prefix}DevAutomation__Port': str(args.port),
        f'{prefix}Window__Width': str(args.width),
        f'{prefix}Window__Height': str(args.height),
        f'{prefix}Window__IsMaximized': 'false',
        f'{prefix}Window__PositionX': '80',
        f'{prefix}Window__PositionY': '80',
    })
    if args.mock_dir is not None and not args.real_feed:
        feed = args.feed or 'feedV12.json'
        mock_url = ensure_mock_server(args.mock_port, state, args.mock_dir.resolve(), feed)
        for key in ('Feed', 'Analytics'):
            env[f'{prefix}Urls__{key}__MainUrl'] = mock_url
            env[f'{prefix}Urls__{key}__ProxyUrl'] = mock_url
        env[f'{prefix}Feed__FeedPath'] = feed
    elif args.feed:
        raise SandboxError('--feed requires --mock-dir and cannot be combined with --real-feed')

    process = subprocess.Popen(
        [str(LAUNCHER_DIR / args.exe_name)], cwd=LAUNCHER_DIR, env=env,
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP)
    state.update({'pid': process.pid, 'port': args.port, 'build': str(output), 'stateNames': state_names})
    save_state(state)

    deadline = time.monotonic() + args.timeout
    while True:
        try:
            status = LauncherClient(args.port, timeout=2).status()
            break
        except (AutomationError, urllib.error.URLError, OSError):
            if process.poll() is not None:
                raise SandboxError(f'launcher exited with code {process.returncode}; see {LAUNCHER_DIR / "console.log"} '
                                   f'and {LAUNCHER_DIR / "logs"}') from None
            if time.monotonic() > deadline:
                raise SandboxError(f'automation API did not answer within {args.timeout} s; with --no-build, the last '
                                   'build may lack -p:EnableDevAutomation=true (a plain solution build replaces it)') from None
            time.sleep(0.3)
    print(f'Launcher pid {process.pid}, API http://localhost:{args.port}/, sandbox {CURRENT_DIR}')
    print_json(dataclasses.asdict(status))
    return 0


def cmd_stop(args) -> int:
    state = load_state()
    kill(state.pop('pid', None))
    if args.mock:
        kill(state.pop('mockPid', None))
        state.pop('mockPort', None)
    save_state(state)
    return 0


def cmd_reset(args) -> int:
    state = load_state()
    kill(state.pop('pid', None))
    save_state(state)
    remove_tree(CURRENT_DIR)
    return 0


def cmd_snapshot(args) -> int:
    if args.action == 'list':
        for name in list_snapshots():
            print(name)
        return 0

    if not args.name:
        raise SandboxError(f'snapshot {args.action} needs a name')
    target = SNAPSHOTS_DIR / args.name
    if args.action == 'delete':
        remove_tree(target)
        return 0

    # save: the launcher is stopped first so its databases and settings are not copied mid-write.
    state = load_state()
    kill(state.pop('pid', None))
    save_state(state)
    if not CURRENT_DIR.is_dir():
        raise SandboxError('there is no sandbox to save; run start first')
    build_output = Path(state['build']) if state.get('build') else None
    remove_tree(target)

    def ignore_build_files(directory: str, names: list[str]) -> list[str]:
        # The build output is copied again on start, so only the state next to it is kept.
        if build_output is None or Path(directory).resolve() != LAUNCHER_DIR.resolve():
            return []
        import fnmatch
        state_names = state.get('stateNames', STATE_NAMES)
        return [name for name in names if (build_output / name).exists()
                and not any(fnmatch.fnmatch(name, pattern) for pattern in state_names)]

    shutil.copytree(CURRENT_DIR, target, ignore=ignore_build_files)
    print(f'Saved {target}')
    return 0


def list_snapshots() -> list[str]:
    return sorted(x.name for x in SNAPSHOTS_DIR.iterdir() if x.is_dir()) if SNAPSHOTS_DIR.is_dir() else []


def remove_tree(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path, onexc=lambda function, failed, _: (os.chmod(failed, 0o700), function(failed)))


def cmd_log(args) -> int:
    logs = sorted((LAUNCHER_DIR / 'logs').glob('log-*.log'))
    if not logs:
        raise SandboxError(f'no logs in {LAUNCHER_DIR / "logs"}')
    lines = logs[-1].read_text(encoding='utf-8', errors='replace').splitlines()
    print('\n'.join(lines[-args.lines:]))
    return 0


# ----- automation API -----

def client() -> LauncherClient:
    return LauncherClient(load_state().get('port') or DEFAULT_PORT)


def selector(args) -> dict:
    """Target and selector keywords of a client call from the command line."""
    keys = ('target', 'id', 'text', 'contains', 'tip', 'type', 'ref', 'index')
    return {name: getattr(args, name) for name in keys if getattr(args, name, None) is not None}


def point_args(args) -> dict:
    """Keywords of a pointer call: the selector, or the --at coordinates and root."""
    if args.at:
        return {'at': tuple(args.at), 'root': args.root}
    return selector(args)


def cmd_status(args) -> int:
    print_json(dataclasses.asdict(client().status()))
    return 0


def cmd_tree(args) -> int:
    roots = [x for x in client().tree(args.hidden).roots if args.root is None or x.index == args.root]
    for root in roots:
        print(json.dumps(dataclasses.asdict(root), indent=2, ensure_ascii=False) if args.json else root.format(args.full))
    return 0


def cmd_find(args) -> int:
    for node in client().find(**selector(args)):
        print(node.describe(full=True))
    return 0


def cmd_click(args) -> int:
    print_pointer(client().click(right=args.right, double=args.double, modifiers=args.mod or (), **point_args(args)))
    return 0


def cmd_hover(args) -> int:
    print_pointer(client().hover(**point_args(args)))
    return 0


def cmd_wheel(args) -> int:
    print_pointer(client().wheel(delta=args.delta, **point_args(args)))
    return 0


def cmd_invoke(args) -> int:
    print(client().invoke(**selector(args)).describe(full=True))
    return 0


def cmd_text(args) -> int:
    print(client().set_text(args.value, **selector(args)).describe(full=True))
    return 0


def cmd_key(args) -> int:
    print(f'focused: {client().key(args.key, modifiers=args.mod or (), root=args.root)}')
    return 0


def cmd_type(args) -> int:
    print(f'focused: {client().type_text(args.value, root=args.root)}')
    return 0


def cmd_scroll(args) -> int:
    print(client().scroll(**selector(args)).describe(full=True))
    return 0


def cmd_wait(args) -> int:
    result = client().wait(state=args.state, task_count=args.tasks, timeout=args.timeout, **selector(args))
    for node in result.matches:
        print(node.describe(full=True))
    if args.tasks is not None:
        print(f'taskCount {result.task_count}')
    return 0


def cmd_screenshot(args) -> int:
    path = Path(args.output) if args.output else SCREENSHOTS_DIR / f'{datetime.now():%Y%m%d-%H%M%S-%f}.png'
    client().screenshot(path, scale=args.scale, **selector(args))
    print(path)
    return 0




# ----- output -----

def print_pointer(result: PointerResult) -> None:
    root = f'root {result.root} ' if result.root else ''
    hit = f' -> {result.hit}' if result.hit else ''
    print(f'{root}at ({result.x:g}, {result.y:g}){hit}')
    if result.target:
        print(result.target.describe(full=True))


def print_json(value) -> None:
    print(json.dumps(value, indent=2, ensure_ascii=False))


def print_api_error(error: AutomationError) -> None:
    print(f'error {error}', file=sys.stderr)
    for node in error.candidates:
        print('  ' + node.describe(), file=sys.stderr)


# ----- arguments -----

def parse_args(configure=None):
    global SANDBOX_DIR, CURRENT_DIR, LAUNCHER_DIR, SNAPSHOTS_DIR, SCREENSHOTS_DIR, STATE_FILE
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--sandbox-dir', type=Path, default=Path.cwd() / 'Sandbox',
                        help='sandbox state directory (default: Sandbox in the working directory)')
    commands = parser.add_subparsers(required=True, metavar='command')

    start = commands.add_parser('start', help='build, prepare the sandbox and start the launcher')
    start.add_argument('--snapshot', help='restore this snapshot first (replaces the current sandbox)')
    start.add_argument('--fresh', action='store_true', help='start from an empty sandbox (first setup)')
    start.add_argument('--aot', action='store_true', help='NativeAOT publish instead of the Debug build')
    start.add_argument('--no-build', action='store_true', help='use the last build output')
    start.add_argument('--project', type=Path, required=True, help='consumer application project path')
    start.add_argument('--exe-name', required=True, help='consumer executable filename, including .exe')
    start.add_argument('--environment-prefix', required=True, help='consumer configuration prefix, including separators')
    start.add_argument('--output', type=Path, help='build output (default: sandbox/build/Debug or Release)')
    start.add_argument('--state-name', action='append', default=[], help='runtime filename/glob excluded from builds')
    start.add_argument('--mock-dir', type=Path, help='consumer mock feed directory (otherwise use configured servers)')
    start.add_argument('--real-feed', action='store_true', help='use configured servers even when --mock-dir is set')
    start.add_argument('--feed', help='mock feed file (default: feedV12.json)')
    start.add_argument('--port', type=int, default=DEFAULT_PORT, help=f'automation API port ({DEFAULT_PORT})')
    start.add_argument('--mock-port', type=int, default=DEFAULT_MOCK_PORT, help=f'mock feed port ({DEFAULT_MOCK_PORT})')
    start.add_argument('--width', type=int, default=DEFAULT_WIDTH)
    start.add_argument('--height', type=int, default=DEFAULT_HEIGHT)
    start.add_argument('--timeout', type=float, default=120, help='seconds to wait for the API')
    start.set_defaults(handler=cmd_start)

    stop = commands.add_parser('stop', help='kill the sandbox launcher')
    stop.add_argument('--mock', action='store_true', help='also stop the mock feed server')
    stop.set_defaults(handler=cmd_stop)

    commands.add_parser('reset', help='kill the launcher and delete the current sandbox').set_defaults(handler=cmd_reset)

    snapshot = commands.add_parser('snapshot', help='save, list or delete sandbox snapshots')
    snapshot.add_argument('action', choices=('save', 'list', 'delete'))
    snapshot.add_argument('name', nargs='?')
    snapshot.set_defaults(handler=cmd_snapshot)

    log = commands.add_parser('log', help='print the end of the sandbox launcher log')
    log.add_argument('-n', '--lines', type=int, default=40)
    log.set_defaults(handler=cmd_log)

    commands.add_parser('status', help='window size, scaling and task count').set_defaults(handler=cmd_status)

    tree = commands.add_parser('tree', help='print the semantic UI tree of every root')
    tree.add_argument('--hidden', action='store_true', help='include invisible elements')
    tree.add_argument('--root', type=int, help='only this root')
    tree.add_argument('--full', action='store_true', help='do not shorten long texts')
    tree.add_argument('--json', action='store_true')
    tree.set_defaults(handler=cmd_tree)

    def add_selector(command, positional=True):
        if positional:
            command.add_argument('target', nargs='?', help="displayed text, or '#N' for a ref")
        command.add_argument('--id', help='AutomationId or x:Name')
        command.add_argument('--text', help='exact displayed text')
        command.add_argument('--contains', help='part of the displayed text')
        command.add_argument('--tip', help='exact tooltip')
        command.add_argument('--type', help='control type, e.g. Button')
        command.add_argument('--ref', type=int)
        command.add_argument('--index', type=int, help='pick one of several matches (0-based)')

    def add_point(command):
        add_selector(command)
        command.add_argument('--at', nargs=2, type=float, metavar=('X', 'Y'), help='root-local coordinates instead')
        command.add_argument('--root', type=int, default=0, help='root for --at (0 = main window)')
        command.add_argument('--mod', action='append', choices=('Alt', 'Control', 'Shift', 'Meta'))

    find = commands.add_parser('find', help='list the elements matching a selector')
    add_selector(find)
    find.set_defaults(handler=cmd_find)

    click = commands.add_parser('click', help='real pointer click at the element center (checks it is reachable)')
    add_point(click)
    click.add_argument('--right', action='store_true')
    click.add_argument('--double', action='store_true')
    click.set_defaults(handler=cmd_click)

    hover = commands.add_parser('hover', help='move the pointer over the element')
    add_point(hover)
    hover.set_defaults(handler=cmd_hover)

    wheel = commands.add_parser('wheel', help='mouse wheel over the element')
    add_point(wheel)
    wheel.add_argument('--delta', type=float, default=-3, help='lines; negative scrolls down (default -3)')
    wheel.set_defaults(handler=cmd_wheel)

    invoke = commands.add_parser('invoke', help='invoke/toggle/select/expand through the automation peer')
    add_selector(invoke)
    invoke.set_defaults(handler=cmd_invoke)

    text = commands.add_parser('text', help='set an editable value (text box) or a number (number box)')
    add_selector(text, positional=False)
    text.add_argument('value')
    text.set_defaults(handler=cmd_text)

    key = commands.add_parser('key', help='press and release a key (Avalonia Key name, e.g. Enter, Escape, Tab)')
    key.add_argument('key')
    key.add_argument('--mod', action='append', choices=('Alt', 'Control', 'Shift', 'Meta'))
    key.add_argument('--root', type=int, default=0)
    key.set_defaults(handler=cmd_key)

    type_ = commands.add_parser('type', help='type text into the focused element')
    type_.add_argument('value')
    type_.add_argument('--root', type=int, default=0)
    type_.set_defaults(handler=cmd_type)

    scroll = commands.add_parser('scroll', help='bring the element into view')
    add_selector(scroll)
    scroll.set_defaults(handler=cmd_scroll)

    wait = commands.add_parser('wait', help='wait for an element state and/or a task count')
    add_selector(wait)
    wait.add_argument('--state', choices=('present', 'absent', 'enabled', 'disabled'), default='present')
    wait.add_argument('--tasks', type=int, help='wait until the task queue has this many visible tasks')
    wait.add_argument('--timeout', type=float, default=30, help='seconds (default 30)')
    wait.set_defaults(handler=cmd_wait)

    screenshot = commands.add_parser('screenshot', help='PNG of the main window with open popups, or of one element')
    add_selector(screenshot)
    screenshot.add_argument('-o', '--output', help=f'file (default {SCREENSHOTS_DIR}/<time>.png)')
    screenshot.add_argument('--scale', type=float, default=1, help='pixels per DIP (default 1 = pointer coordinates)')
    screenshot.set_defaults(handler=cmd_screenshot)


    if configure is not None:
        configure(parser, commands)
    args = parser.parse_args()
    SANDBOX_DIR = args.sandbox_dir.resolve()
    CURRENT_DIR = SANDBOX_DIR / 'current'
    LAUNCHER_DIR = CURRENT_DIR / 'Launcher'
    SNAPSHOTS_DIR = SANDBOX_DIR / 'snapshots'
    SCREENSHOTS_DIR = SANDBOX_DIR / 'screenshots'
    STATE_FILE = SANDBOX_DIR / 'state.json'
    if getattr(args, 'handler', None) is cmd_start:
        if not args.exe_name.strip() or Path(args.exe_name).name != args.exe_name:
            parser.error('--exe-name must be a nonempty executable filename')
        if not args.environment_prefix.strip():
            parser.error('--environment-prefix must be nonempty')
        if args.output is None:
            args.output = SANDBOX_DIR / 'build' / ('Release' if args.aot else 'Debug')
    return args


if __name__ == '__main__':
    sys.exit(main())

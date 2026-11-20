"""Sample launcher flows and CLI on top of the kit's launcher_dev and LauncherClient. Standard library only.

    python scripts/make_mocks.py
    python scripts/launcher_flows.py start --fresh
    python scripts/launcher_flows.py smoke                 # setup, Home and a new sample download
    python scripts/launcher_flows.py start --no-build
    python scripts/launcher_flows.py smoke --configured    # Home and the download (reports cache hit or transfer)
    python scripts/launcher_flows.py stop --mock

Every generic launcher_dev command (tree, find, click, wait, screenshot, snapshot, log, ...) works too, with the
sample's project, executable, environment prefix, mock directory, sandbox (scripts/Sandbox) and ports (API 8195,
mock feed 8190) as defaults; commands refuse an automation API not served by the sample sandbox's launcher.
"""

import hashlib
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
SAMPLE_DIR = SCRIPT_DIR.parent
sys.path.insert(0, str(SAMPLE_DIR.parent.parent / 'scripts'))

import launcher_dev
import make_mocks
from launcher_client import AutomationError

SKYRIM_HEADING = 'Skyrim directory (an empty directory is fine)'
DATA_HEADING = 'Directory for downloads and temporary files'
DOWNLOAD_FILE_NAME = Path(make_mocks.SAMPLE_DOWNLOAD_PATH).name
POLL_INTERVAL = 0.5
# Distinct from other launchers' defaults (kit 8095/8090) so a running launcher is never mistaken for the sample.
SAMPLE_API_PORT = 8195
SAMPLE_MOCK_PORT = 8190


class FlowError(Exception):
    pass


def visible(**selector) -> list:
    return [x for x in client().find(**selector) if not x.hidden]


def sample_client() -> launcher_dev.LauncherClient:
    """Client for the sample sandbox's launcher; refuses an API served by any other process."""
    state = launcher_dev.load_state()
    pid = state.get('pid')
    if not pid or not launcher_dev.is_running(pid):
        raise FlowError('the sample sandbox launcher is not running; use start first')
    launcher = launcher_dev.LauncherClient(state.get('port') or SAMPLE_API_PORT)
    reported = launcher.status().process_id
    if reported != pid:
        raise FlowError(f'the automation API on port {state.get("port")} belongs to process {reported}, '
                        f'not the sample launcher (pid {pid})')
    return launcher


def client() -> launcher_dev.LauncherClient:
    return sample_client()


def first_setup(skyrim_dir: Path, data_dir: Path) -> None:
    """Runs the first setup from the step it shows (Skyrim or data directory, download server check) up to Home.

    An interrupted setup resumes later: a setup continued without downloads shows only the server check."""
    launcher = client()
    launcher.wait(type='FirstSetupView', timeout=30)
    # Always explicit: automation never relies on a suggested directory (the sample's finder suggests none).
    directories = {SKYRIM_HEADING: skyrim_dir, DATA_HEADING: data_dir}
    deadline = time.monotonic() + 10
    while True:
        heading = next((x for x in directories if visible(text=x)), None)
        if heading is not None:
            launcher.set_text(str(directories.pop(heading)), id='DirectoryBox')
            launcher.click(id='NextButton')
            launcher.wait(text=heading, state='absent', timeout=10)
            deadline = time.monotonic() + 10
        elif visible(type='CheckAvailabilityView'):
            break
        elif visible(type='HomeView') and not visible(type='FirstSetupView'):
            # A successful check persisted by an interrupted setup skips the availability step.
            print('First setup finished (download server check already passed)')
            return
        elif time.monotonic() > deadline:
            raise FlowError('no setup step, server check or Home appeared within 10 s')
        else:
            time.sleep(POLL_INTERVAL)
    deadline = time.monotonic() + 60
    while True:
        # "Continue" stays disabled while the check runs; a failed check offers continuing without downloads.
        if any(not x.disabled for x in visible(id='ContinueButton')):
            break
        if visible(id='ContinueOfflineButton'):
            raise FlowError('the download server check failed; is the mock server running (start --mock-dir)?')
        if time.monotonic() > deadline:
            raise FlowError('the download server check did not finish within 60 s')
        time.sleep(POLL_INTERVAL)
    launcher.click(id='ContinueButton')
    launcher.wait(type='HomeView', timeout=30)
    launcher.wait(type='FirstSetupView', state='absent', timeout=10)
    print('First setup finished')


def check_home() -> None:
    """Opens Home and waits for the feed's category, which the kit's ModCategoriesViewModel loads."""
    launcher = client()
    launcher.click(id='HomeNavButton')
    launcher.wait(type='HomeView', timeout=10)
    launcher.wait(text=make_mocks.CATEGORY_TITLE, timeout=30)
    print(f'Home shows feed category "{make_mocks.CATEGORY_TITLE}"')


def mock_log_size() -> int:
    path = launcher_dev.SANDBOX_DIR / 'mock-server.log'
    return path.stat().st_size if path.is_file() else 0


def mock_requests_since(offset: int) -> int | None:
    """GET requests for the sample file logged since offset by the mock server the current start selected; None if
    unknown (that server is not one the sandbox started, so its requests are not in the sandbox log)."""
    pid = (launcher_dev.load_state().get('activeMock') or {}).get('pid')
    if not pid or not launcher_dev.is_running(pid):
        return None
    path = launcher_dev.SANDBOX_DIR / 'mock-server.log'
    with open(path, 'rb') as log:
        log.seek(offset)
        return log.read().count(f'GET /{make_mocks.SAMPLE_DOWNLOAD_PATH} '.encode('ascii'))


def download_sample(data_dir: Path, timeout: float, fresh: bool = False) -> None:
    """Downloads the sample file through the task queue and checks Downloads and the stored file's SHA-256.

    fresh requires an absent destination and a logged transfer. Otherwise the output says whether the file was
    downloaded, downloaded again over an existing file, or kept from storage without a request (cache hit)."""
    path = data_dir / 'Downloads' / DOWNLOAD_FILE_NAME
    existed = path.exists()
    if fresh and existed:
        raise FlowError(f'{path} exists before the fresh download test; use an empty --data directory')
    log_offset = mock_log_size()
    launcher = client()
    launcher.click(id='HomeNavButton')
    launcher.click(id='DownloadSampleButton')
    try:
        launcher.wait(id='SampleDownloadedText', timeout=timeout)
    except AutomationError as error:
        launcher.click(id='TasksNavButton')
        tasks = '\n'.join(x.describe() for x in launcher.tree().walk() if x.type == 'TextBlock' and x.text)
        raise FlowError(f'the sample download did not complete ({error}); task page:\n{tasks}') from None
    launcher.wait(task_count=0, timeout=10)
    launcher.click(id='DownloadsNavButton')
    launcher.wait(type='DownloadsView', timeout=10)
    row = launcher.wait(text=DOWNLOAD_FILE_NAME, timeout=10).matches[0]
    texts = [x.text for x in launcher.tree().walk() if x.type == 'TextBlock' and x.text]
    status = texts[texts.index(row.text) + 2] if row.text in texts and texts.index(row.text) + 2 < len(texts) else None
    if status != 'Complete':
        raise FlowError(f'Downloads shows {DOWNLOAD_FILE_NAME} as {status!r}, not Complete')
    if not path.is_file():
        raise FlowError(f'{path} was not stored')
    digest = hashlib.sha256(path.read_bytes()).hexdigest().upper()
    if digest != make_mocks.SAMPLE_DOWNLOAD_SHA256:
        raise FlowError(f'{path} has SHA-256 {digest}, expected {make_mocks.SAMPLE_DOWNLOAD_SHA256}')
    requests = mock_requests_since(log_offset)
    if fresh and not requests:
        raise FlowError(f'{path} was stored, but no sandbox log shows a request for it from the selected mock server')
    if requests is None:
        kind = 'Stored (pre-existing file; transfer unknown, the selected mock server has no sandbox log)' if existed \
            else 'Downloaded (transfer not confirmed, the selected mock server has no sandbox log)'
    elif requests:
        kind = 'Downloaded again over the pre-existing file' if existed else 'Downloaded'
    else:
        kind = 'Cache hit: kept the pre-existing file without a request' if existed \
            else 'Stored without a logged request'
    print(f'{kind}: {path} ({path.stat().st_size} bytes, SHA-256 {digest}), shown as Complete in Downloads')
    if existed:
        print('The file existed before this run; use start --fresh and smoke for the transfer test')


# ----- CLI -----

def default_dir(value: str | None, name: str) -> Path:
    return Path(value) if value else launcher_dev.CURRENT_DIR / name


def saved_data_dir() -> Path | None:
    """The data directory the launcher saved (its setup step is then skipped), or None."""
    path = launcher_dev.LAUNCHER_DIR / 'appsettings.override.json'
    if not path.is_file():
        return None
    saved = json.loads(path.read_text(encoding='utf-8-sig')).get('App', {}).get('AppDataDirectory')
    return Path(saved) if saved else None


def effective_data_dir(args) -> Path:
    """The saved data directory if any, else --data or the sandbox default; a conflicting --data is rejected."""
    saved = saved_data_dir()
    if saved is None:
        return default_dir(args.data, 'Data')
    if args.data and Path(args.data).resolve() != saved.resolve():
        raise FlowError(f'--data {args.data} differs from the launcher\'s saved data directory {saved}')
    return saved


def cmd_setup(args) -> int:
    first_setup(default_dir(args.skyrim, 'Skyrim'), effective_data_dir(args))
    return 0


def cmd_home(args) -> int:
    check_home()
    return 0


def cmd_download(args) -> int:
    download_sample(effective_data_dir(args), args.timeout)
    return 0


def cmd_smoke(args) -> int:
    data_dir = effective_data_dir(args)
    launcher = client()
    # The home button shows the scope's home page: the first setup while it is needed, otherwise Home.
    launcher.click(id='HomeNavButton')
    deadline = time.monotonic() + 30
    while not (in_setup := bool(visible(type='FirstSetupView'))) and not visible(type='HomeView'):
        if time.monotonic() > deadline:
            raise FlowError('neither the first setup nor Home appeared within 30 s')
        time.sleep(POLL_INTERVAL)
    if in_setup:
        if args.configured:
            raise FlowError('the first setup is shown, but --configured expects a configured launcher')
        download_path = data_dir / 'Downloads' / DOWNLOAD_FILE_NAME
        if download_path.exists():  # checked before setup changes any state
            raise FlowError(f'{download_path} exists before the fresh download test; use an empty --data directory')
        first_setup(default_dir(args.skyrim, 'Skyrim'), data_dir)
    elif not args.configured:
        raise FlowError('Home is shown instead of the first setup; use start --fresh, or pass --configured')
    check_home()
    download_sample(data_dir, args.timeout, fresh=in_setup)
    print('Smoke flow passed')
    return 0


def verify_mock_fixtures(args, mock_url: str) -> None:
    """Kit start hook: the selected mock server (possibly an already running one, which the kit reuses whenever it
    answers the feed) must serve this sample's fixtures byte for byte."""
    for name in (args.feed or 'feedV12.json', 'Test.zip', make_mocks.SAMPLE_DOWNLOAD_PATH):
        try:
            with urllib.request.urlopen(mock_url + name, timeout=5) as response:
                served = response.read()
        except (urllib.error.URLError, OSError) as error:
            raise FlowError(f'{name} could not be verified on the mock server {mock_url} ({error}); '
                            'stop that server or pass --mock-port') from None
        local = args.mock_dir / name
        if not local.is_file() or served != local.read_bytes():
            raise FlowError(f'the mock server {mock_url} serves a different {name} than {local}; '
                            'stop that server or pass --mock-port')


def configure_cli(parser, commands) -> None:
    parser.set_defaults(sandbox_dir=SCRIPT_DIR / 'Sandbox')
    # Generic kit commands (click, tree, ...) go through the same PID-checked client.
    launcher_dev.client = sample_client
    start = commands.choices['start']
    for action in start._actions:
        if action.dest in ('project', 'exe_name', 'environment_prefix'):
            action.required = False
        elif action.dest == 'port':
            action.help = f'automation API port ({SAMPLE_API_PORT})'
        elif action.dest == 'mock_port':
            action.help = f'mock feed port ({SAMPLE_MOCK_PORT})'
    start.set_defaults(
        project=SAMPLE_DIR / 'src' / 'SampleLauncher.App' / 'SampleLauncher.App.csproj',
        exe_name='SampleLauncher.App.exe', environment_prefix='SAMPLE_LAUNCHER_',
        mock_dir=SCRIPT_DIR / 'Mock', state_name=['launcher.db*'],
        port=SAMPLE_API_PORT, mock_port=SAMPLE_MOCK_PORT, verify_mock=verify_mock_fixtures)

    def add_directories(command, skyrim=True):
        if skyrim:
            command.add_argument('--skyrim', help='Skyrim directory for the setup (default: sandbox current/Skyrim)')
        command.add_argument('--data', help='launcher data directory (default: sandbox current/Data)')

    setup = commands.add_parser('setup', help='first setup: Skyrim and data directories, download server check')
    add_directories(setup)
    setup.set_defaults(handler=cmd_setup)

    commands.add_parser('home', help='open Home and wait for the feed category').set_defaults(handler=cmd_home)

    download = commands.add_parser('download', help='download the sample file and check Downloads and its hash')
    add_directories(download, skyrim=False)
    download.add_argument('--timeout', type=float, default=60, help='seconds for the download task (60)')
    download.set_defaults(handler=cmd_download)

    smoke = commands.add_parser('smoke', help='setup (unless --configured), Home and the sample download')
    add_directories(smoke)
    smoke.add_argument('--configured', action='store_true', help='expect Home instead of the first setup')
    smoke.add_argument('--timeout', type=float, default=60, help='seconds for the download task (60)')
    smoke.set_defaults(handler=cmd_smoke)


if __name__ == '__main__':
    sys.exit(launcher_dev.main(configure_cli, (FlowError,)))

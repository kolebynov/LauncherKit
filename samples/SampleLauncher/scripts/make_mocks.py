"""Generate the sample launcher's local mock server files in scripts/Mock. Standard library only.

    python scripts/make_mocks.py

Writes:
- feedV12.json: a minimal valid feed (English only) with the category the smoke flow expects on Home;
- Test.zip: the file the first setup's download server check reads;
- Sample/sample-download.txt: the file the Home page's sample download fetches through the task queue. Its SHA-256 is
  compiled into src/SampleLauncher.App/SampleDownload.cs; this script fails when they differ.

The output is deterministic and ignored by Git. launcher_flows.py serves the directory on http://127.0.0.1:8190/.
"""

import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
MOCK_DIR = SCRIPT_DIR / 'Mock'
SAMPLE_DOWNLOAD_SOURCE = SCRIPT_DIR.parent / 'src' / 'SampleLauncher.App' / 'SampleDownload.cs'

CATEGORY_ID = 'sample-tools'
CATEGORY_TITLE = 'Sample Tools'
SAMPLE_DOWNLOAD_PATH = 'Sample/sample-download.txt'
# 38 KB, so the download reports progress, yet stays instant on localhost.
SAMPLE_DOWNLOAD_CONTENT = b''.join(f'LauncherKit sample download line {i:04}\n'.encode('ascii') for i in range(1000))
SAMPLE_DOWNLOAD_SHA256 = hashlib.sha256(SAMPLE_DOWNLOAD_CONTENT).hexdigest().upper()


def feed() -> dict:
    return {
        'DefaultLanguage': 'en',
        # Required by the feed contract; the sample never downloads them.
        'SkyrimDownloadInfo': {'S3Path': 'Unused/Skyrim.7z'},
        'SevenZipDownloadInfo': {'S3Path': 'Unused/7z.zip'},
        'Modpacks': [],
        'ModpackChangelogs': [],
        'Launchers': [],
        'ModCategories': [{'Id': CATEGORY_ID, 'Title': {'en': CATEGORY_TITLE}}],
        'Mods': [],
    }


def write_test_zip(path: Path) -> None:
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
        # A fixed timestamp keeps the archive byte-identical between runs.
        archive.writestr(zipfile.ZipInfo('test.txt', date_time=(2020, 1, 1, 0, 0, 0)), 'LauncherKit sample\n')


def compiled_hash() -> str:
    match = re.search(r'new Sha256Hash\("([0-9A-F]{64})"\)', SAMPLE_DOWNLOAD_SOURCE.read_text(encoding='utf-8'))
    if match is None:
        raise SystemExit(f'error: no Sha256Hash literal in {SAMPLE_DOWNLOAD_SOURCE}')
    return match.group(1)


def main() -> int:
    if compiled_hash() != SAMPLE_DOWNLOAD_SHA256:
        print(f'error: {SAMPLE_DOWNLOAD_SOURCE.name} expects another hash; set it to {SAMPLE_DOWNLOAD_SHA256}',
              file=sys.stderr)
        return 1
    MOCK_DIR.mkdir(exist_ok=True)
    (MOCK_DIR / 'feedV12.json').write_text(json.dumps(feed(), indent=2), encoding='utf-8')
    write_test_zip(MOCK_DIR / 'Test.zip')
    download = MOCK_DIR / SAMPLE_DOWNLOAD_PATH
    download.parent.mkdir(parents=True, exist_ok=True)
    download.write_bytes(SAMPLE_DOWNLOAD_CONTENT)
    print(f'Mock files written to {MOCK_DIR}')
    return 0


if __name__ == '__main__':
    sys.exit(main())

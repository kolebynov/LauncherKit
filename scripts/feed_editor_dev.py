"""Run the feed editor against a fake R2 bucket with stable data, to check editor changes without R2 credentials.

The fake bucket is an in-memory S3 subset served on localhost (the requests FeedSession and the browser upload send:
GetObject, HeadObject, PutObject with If-Match, ListObjectsV2, multipart uploads and CORS preflights; signatures are
not checked). Each run starts from the same data:

- V12 under `feedV12.json` (--feed), plus frozen V11 under `feedV11.json` (--legacy-feed);
- an object for every bucket path the feed names (downloads, logos, descriptions, changelogs) with small fixed content
  and `x-amz-meta-checksum-sha256` metadata holding that content's SHA-256, so picking such a path fills its hash;
- `Smoke/` objects for the other cases: `no-checksum.7z` (no metadata), `invalid-checksum.7z` (metadata that is not a
  SHA-256) and `uppercase-checksum.7z` (uppercase hex metadata).

Any access key signs in except `denied`, which the fake bucket rejects like revoked credentials. Saves and uploads
stay in memory until the script stops, so restarting it resets the bucket. With --no-editor only the bucket runs,
e.g. for a feed editor started from an IDE with R2__ServiceUrl pointing at it.
"""

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import threading
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from email.utils import format_datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlsplit
from xml.sax.saxutils import escape

SCRIPT_DIR = Path(__file__).resolve().parent
KIT_DIR = SCRIPT_DIR.parent
EDITOR_PROJECT = KIT_DIR / 'src' / 'LauncherKit.FeedEditor'
DEFAULT_FEED = KIT_DIR / 'tests' / 'LauncherKit.FeedEditor.Tests' / 'Fixtures' / 'feedV12.json'
DEFAULT_LEGACY_FEED = KIT_DIR / 'tests' / 'LauncherKit.FeedEditor.Tests' / 'Fixtures' / 'feed.json'
FEED_KEY = 'feedV12.json'
LEGACY_FEED_KEY = 'feedV11.json'
DEFAULT_EDITOR_PORT = 5090
DEFAULT_R2_PORT = 5005
DENIED_ACCESS_KEY = 'denied'
CHECKSUM_METADATA = 'x-amz-meta-checksum-sha256'
LIST_PAGE_SIZE = 1000
S3_NAMESPACE = 'http://s3.amazonaws.com/doc/2006-03-01/'


def main() -> int:
    sys.stdout.reconfigure(encoding='utf-8')
    args = parse_args()
    store = seed_store(args.feed, args.legacy_feed, args.bucket)
    server = ThreadingHTTPServer(('127.0.0.1', args.r2_port), lambda *a: FakeR2Handler(store, *a))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    r2_url = f'http://127.0.0.1:{args.r2_port}'
    print(f'Fake R2: {r2_url} (bucket {args.bucket}, {len(store.objects)} objects, feeds {FEED_KEY}, {LEGACY_FEED_KEY})')
    print(f'Sign in with any access key and secret; access key "{DENIED_ACCESS_KEY}" is rejected.')
    try:
        if args.no_editor:
            print(f'Point the editor at it: R2__ServiceUrl={r2_url} R2__BucketName={args.bucket} '
                  f'R2__FeedKey={FEED_KEY} R2__LegacyFeedKey={LEGACY_FEED_KEY}')
            print('Press Ctrl+C to stop.')
            threading.Event().wait()
            return 0

        editor_url = f'http://127.0.0.1:{args.port}'
        env = os.environ | {
            'ASPNETCORE_ENVIRONMENT': 'Development',
            'ASPNETCORE_URLS': editor_url,
            'R2__ServiceUrl': r2_url,
            'R2__BucketName': args.bucket,
            'R2__FeedKey': FEED_KEY,
            'R2__LegacyFeedKey': LEGACY_FEED_KEY,
        }
        print(f'Feed editor: {editor_url}')
        return subprocess.call(['dotnet', 'run', '--project', str(EDITOR_PROJECT), '--no-launch-profile'], env=env)
    except KeyboardInterrupt:
        return 0
    finally:
        server.shutdown()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--bucket', required=True, help='consumer bucket name for the fake R2 service')
    parser.add_argument('--port', type=int, default=DEFAULT_EDITOR_PORT, help='feed editor port (default %(default)s)')
    parser.add_argument('--r2-port', type=int, default=DEFAULT_R2_PORT, help='fake R2 port (default %(default)s)')
    parser.add_argument('--feed', type=Path, default=DEFAULT_FEED, help='V12 feed JSON (default: the editor V12 fixture)')
    parser.add_argument('--legacy-feed', type=Path, default=DEFAULT_LEGACY_FEED,
                        help='V11 feed JSON (default: the frozen editor fixture)')
    parser.add_argument('--no-editor', action='store_true', help='serve only the fake bucket')
    return parser.parse_args()


@dataclass
class StoredObject:
    body: bytes
    content_type: str
    cache_control: str | None = None
    metadata: dict[str, str] = field(default_factory=dict)
    etag: str = ''
    modified: datetime = field(default_factory=lambda: datetime.now(timezone.utc))

    def __post_init__(self) -> None:
        if not self.etag:
            self.etag = f'"{hashlib.md5(self.body).hexdigest()}"'


@dataclass
class MultipartUpload:
    key: str
    content_type: str
    parts: dict[int, bytes] = field(default_factory=dict)


class Store:
    def __init__(self, bucket: str) -> None:
        self.bucket = bucket
        self.lock = threading.Lock()
        self.objects: dict[str, StoredObject] = {}
        self.uploads: dict[str, MultipartUpload] = {}


def seed_store(feed_path: Path, legacy_feed_path: Path, bucket: str) -> Store:
    store = Store(bucket)
    paths: set[str] = set()
    for key, path in ((FEED_KEY, feed_path), (LEGACY_FEED_KEY, legacy_feed_path)):
        feed_bytes = path.read_bytes()
        feed = json.loads(feed_bytes)
        store.objects[key] = StoredObject(feed_bytes, 'application/json', cache_control='no-cache')
        paths.update(bucket_paths(feed))
    for key in sorted(paths):
        body = fake_content(key)
        store.objects[key] = StoredObject(
            body, content_type_of(key), metadata={CHECKSUM_METADATA: hashlib.sha256(body).hexdigest()})

    store.objects['Smoke/no-checksum.7z'] = StoredObject(fake_content('Smoke/no-checksum.7z'), 'application/x-7z-compressed')
    store.objects['Smoke/invalid-checksum.7z'] = StoredObject(
        fake_content('Smoke/invalid-checksum.7z'), 'application/x-7z-compressed',
        metadata={CHECKSUM_METADATA: 'not-a-sha256'})
    uppercase = fake_content('Smoke/uppercase-checksum.7z')
    store.objects['Smoke/uppercase-checksum.7z'] = StoredObject(
        uppercase, 'application/x-7z-compressed',
        metadata={CHECKSUM_METADATA: hashlib.sha256(uppercase).hexdigest().upper()})
    return store


def bucket_paths(feed: dict) -> set[str]:
    """Every bucket-relative path the feed names: download S3Paths, logos, descriptions and changelogs."""
    paths: set[str] = set()

    def walk(value: object) -> None:
        if isinstance(value, dict):
            for name, item in value.items():
                if name in ('S3Path', 'LogoPath', 'DescriptionPath', 'ChangelogPath', 'Path'):
                    if isinstance(item, str):
                        paths.add(item)
                    elif isinstance(item, dict):
                        paths.update(path for path in item.values() if isinstance(path, str))
                else:
                    walk(item)
        elif isinstance(value, list):
            for item in value:
                walk(item)

    walk(feed)
    return {path for path in paths if path}


def fake_content(key: str) -> bytes:
    if key.endswith('.md'):
        return f'# {key}\n\nFake content served by feed_editor_dev.py.\n'.encode()
    return f'Fake content of {key}\n'.encode()


def content_type_of(key: str) -> str:
    suffix = key.rsplit('.', 1)[-1].lower()
    return {
        'md': 'text/markdown', 'json': 'application/json', 'webp': 'image/webp', 'png': 'image/png',
        '7z': 'application/x-7z-compressed', 'zip': 'application/zip',
    }.get(suffix, 'application/octet-stream')


class FakeR2Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def __init__(self, store: Store, *args) -> None:
        self.store = store
        super().__init__(*args)

    def log_message(self, format: str, *args) -> None:  # noqa: A002 - BaseHTTPRequestHandler's signature
        print(f'[fake R2] {format % args}', flush=True)

    def do_OPTIONS(self) -> None:
        # Browser uploads are cross-origin PUTs with a Content-Type, so they are preflighted.
        self.send_response(200)
        self.send_header('Access-Control-Allow-Methods', 'GET, HEAD, PUT')
        self.send_header('Access-Control-Allow-Headers', self.headers.get('Access-Control-Request-Headers', '*'))
        self.send_header('Access-Control-Max-Age', '600')
        self.send_header('Content-Length', '0')
        self.end_headers()

    def do_GET(self) -> None:
        self.dispatch()

    def do_HEAD(self) -> None:
        self.dispatch()

    def do_PUT(self) -> None:
        self.dispatch()

    def do_POST(self) -> None:
        self.dispatch()

    def do_DELETE(self) -> None:
        self.dispatch()

    def end_headers(self) -> None:
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Expose-Headers', 'ETag')
        super().end_headers()

    def dispatch(self) -> None:
        url = urlsplit(self.path)
        query = {name: values[0] for name, values in parse_qs(url.query, keep_blank_values=True).items()}
        bucket, _, key = unquote(url.path).lstrip('/').partition('/')
        body = self.read_body()
        if self.access_key(query) == DENIED_ACCESS_KEY:
            return self.error(403, 'AccessDenied', 'Access denied.')
        if bucket != self.store.bucket:
            return self.error(404, 'NoSuchBucket', 'The specified bucket does not exist.')

        method = self.command
        if not key:
            if method == 'GET':
                return self.list_objects(query)
        elif method in ('GET', 'HEAD'):
            return self.get_object(key, head=method == 'HEAD')
        elif method == 'PUT' and 'uploadId' in query:
            return self.upload_part(query['uploadId'], int(query['partNumber']), body)
        elif method == 'PUT':
            return self.put_object(key, body)
        elif method == 'POST' and 'uploads' in query:
            return self.initiate_upload(key)
        elif method == 'POST' and 'uploadId' in query:
            return self.complete_upload(key, query['uploadId'], body)
        elif method == 'DELETE' and 'uploadId' in query:
            return self.abort_upload(query['uploadId'])
        self.error(501, 'NotImplemented', f'{method} {self.path} is not supported by the fake bucket.')

    def read_body(self) -> bytes:
        length = int(self.headers.get('Content-Length') or 0)
        return self.rfile.read(length) if length else b''

    def access_key(self, query: dict[str, str]) -> str | None:
        # Header-signed requests carry "Credential=<key>/<scope>" in Authorization; presigned URLs in X-Amz-Credential.
        match = re.search(r'Credential=([^/,]+)/', self.headers.get('Authorization', ''))
        if match:
            return match.group(1)
        credential = query.get('X-Amz-Credential')
        return credential.split('/', 1)[0] if credential else None

    def list_objects(self, query: dict[str, str]) -> None:
        with self.store.lock:
            keys = sorted(self.store.objects)
            objects = {key: self.store.objects[key] for key in keys}
        start = int(query.get('continuation-token') or 0)
        page = keys[start:start + LIST_PAGE_SIZE]
        truncated = start + LIST_PAGE_SIZE < len(keys)
        contents = ''.join(
            f'<Contents><Key>{escape(key)}</Key><LastModified>{objects[key].modified.strftime("%Y-%m-%dT%H:%M:%S.000Z")}'
            f'</LastModified><ETag>{escape(objects[key].etag)}</ETag><Size>{len(objects[key].body)}</Size>'
            f'<StorageClass>STANDARD</StorageClass></Contents>'
            for key in page)
        token = f'<NextContinuationToken>{start + LIST_PAGE_SIZE}</NextContinuationToken>' if truncated else ''
        self.xml(200, f'<ListBucketResult xmlns="{S3_NAMESPACE}"><Name>{escape(self.store.bucket)}</Name><KeyCount>{len(page)}</KeyCount>'
                      f'<MaxKeys>{LIST_PAGE_SIZE}</MaxKeys><IsTruncated>{str(truncated).lower()}</IsTruncated>'
                      f'{token}{contents}</ListBucketResult>')

    def get_object(self, key: str, head: bool) -> None:
        with self.store.lock:
            stored = self.store.objects.get(key)
        if stored is None:
            return self.error(404, 'NoSuchKey', 'The specified key does not exist.')

        self.send_response(200)
        self.send_header('Content-Type', stored.content_type)
        self.send_header('Content-Length', str(len(stored.body)))
        self.send_header('ETag', stored.etag)
        self.send_header('Last-Modified', format_datetime(stored.modified, usegmt=True))
        if stored.cache_control:
            self.send_header('Cache-Control', stored.cache_control)
        for name, value in stored.metadata.items():
            self.send_header(name, value)
        self.end_headers()
        if not head:
            self.wfile.write(stored.body)

    def put_object(self, key: str, body: bytes) -> None:
        metadata = {name.lower(): value for name, value in self.headers.items() if name.lower().startswith('x-amz-meta-')}
        stored = StoredObject(
            body, self.headers.get('Content-Type') or 'application/octet-stream',
            cache_control=self.headers.get('Cache-Control'), metadata=metadata)
        with self.store.lock:
            if_match = self.headers.get('If-Match')
            current = self.store.objects.get(key)
            if if_match is not None and (current is None or current.etag != if_match):
                return self.error(412, 'PreconditionFailed', 'At least one of the pre-conditions you specified did not hold.')
            self.store.objects[key] = stored
        self.empty(200, stored.etag)

    def initiate_upload(self, key: str) -> None:
        upload_id = uuid.uuid4().hex
        with self.store.lock:
            self.store.uploads[upload_id] = MultipartUpload(key, self.headers.get('Content-Type') or 'application/octet-stream')
        self.xml(200, f'<InitiateMultipartUploadResult xmlns="{S3_NAMESPACE}"><Bucket>{escape(self.store.bucket)}</Bucket>'
                      f'<Key>{escape(key)}</Key><UploadId>{upload_id}</UploadId></InitiateMultipartUploadResult>')

    def upload_part(self, upload_id: str, number: int, body: bytes) -> None:
        with self.store.lock:
            upload = self.store.uploads.get(upload_id)
            if upload is None:
                return self.error(404, 'NoSuchUpload', 'The specified multipart upload does not exist.')
            upload.parts[number] = body
        self.empty(200, f'"{hashlib.md5(body).hexdigest()}"')

    def complete_upload(self, key: str, upload_id: str, body: bytes) -> None:
        numbers = [int(number) for number in re.findall(rb'<PartNumber>(\d+)</PartNumber>', body)]
        with self.store.lock:
            upload = self.store.uploads.pop(upload_id, None)
            if upload is None:
                return self.error(404, 'NoSuchUpload', 'The specified multipart upload does not exist.')
            if any(number not in upload.parts for number in numbers):
                return self.error(400, 'InvalidPart', 'One or more of the specified parts could not be found.')
            parts = [upload.parts[number] for number in numbers]
            digest = hashlib.md5(b''.join(hashlib.md5(part).digest() for part in parts)).hexdigest()
            stored = StoredObject(b''.join(parts), upload.content_type, etag=f'"{digest}-{len(parts)}"')
            self.store.objects[key] = stored
        self.xml(200, f'<CompleteMultipartUploadResult xmlns="{S3_NAMESPACE}"><Bucket>{escape(self.store.bucket)}</Bucket>'
                      f'<Key>{escape(key)}</Key><ETag>{escape(stored.etag)}</ETag></CompleteMultipartUploadResult>')

    def abort_upload(self, upload_id: str) -> None:
        with self.store.lock:
            self.store.uploads.pop(upload_id, None)
        self.empty(204, None)

    def xml(self, status: int, content: str) -> None:
        data = f'<?xml version="1.0" encoding="UTF-8"?>{content}'.encode()
        self.send_response(status)
        self.send_header('Content-Type', 'application/xml')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def empty(self, status: int, etag: str | None) -> None:
        self.send_response(status)
        if etag:
            self.send_header('ETag', etag)
        self.send_header('Content-Length', '0')
        self.end_headers()

    def error(self, status: int, code: str, message: str) -> None:
        if self.command == 'HEAD':
            # HEAD responses carry no body, so the SDK reads the error from the status code alone.
            return self.empty(status, None)
        self.xml(status, f'<Error><Code>{code}</Code><Message>{escape(message)}</Message>'
                         f'<RequestId>{uuid.uuid4().hex}</RequestId></Error>')


if __name__ == '__main__':
    sys.exit(main())

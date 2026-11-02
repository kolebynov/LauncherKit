"""Python client for the launcher's dev automation API (kit/docs/dev-automation.md). Standard library only.

    import sys
    sys.path.insert(0, 'kit/scripts')
    from launcher_client import LauncherClient

    launcher = LauncherClient()                      # http://localhost:8095/, the sandbox default
    launcher.click('Установить')                     # exact displayed text
    launcher.click(contains='Graphics', type='LockableCheckBox')
    launcher.click(tip='Настройки')                  # icon button by tooltip
    for node in launcher.find(type='ModCardView'):   # nodes can be passed back as targets
        launcher.hover(node)
    launcher.wait(task_count=0, timeout=600)
    launcher.screenshot('shot.png')
    print(launcher.tree().format())

A target is a displayed text, '#N' or an int ref, or a Node from any result: a ref stays with its control for the
launcher's lifetime. Selector keywords: id (AutomationId or x:Name), text, contains, tip, type, ref, index. Failed
requests raise AutomationError.
"""

import json
import urllib.error
import urllib.request
from collections.abc import Iterator
from dataclasses import dataclass
from pathlib import Path

DEFAULT_PORT = 8095
SELECTOR_KEYS = frozenset({'id', 'text', 'contains', 'tip', 'type', 'ref', 'index'})
MODIFIERS = frozenset({'Alt', 'Control', 'Shift', 'Meta'})


@dataclass(frozen=True)
class Bounds:
    x: float
    y: float
    width: float
    height: float

    @property
    def center(self) -> tuple[float, float]:
        return self.x + self.width / 2, self.y + self.height / 2

    @staticmethod
    def from_json(value: dict) -> 'Bounds':
        return Bounds(value['x'], value['y'], value['width'], value['height'])


@dataclass(frozen=True)
class Node:
    """An element of the semantic tree; bounds are root-local device-independent pixels."""
    ref: int
    type: str
    root: int
    bounds: Bounds
    id: str | None = None
    text: str | None = None
    value: str | None = None
    tip: str | None = None
    disabled: bool = False
    transparent: bool = False
    hidden: bool = False
    focused: bool = False
    checked: bool | None = None
    selected: bool | None = None
    expanded: bool | None = None
    children: tuple['Node', ...] = ()

    @staticmethod
    def from_json(value: dict) -> 'Node':
        return Node(
            ref=value['ref'], type=value['type'], root=value['root'], bounds=Bounds.from_json(value['bounds']),
            id=value.get('id'), text=value.get('text'), value=value.get('value'), tip=value.get('tip'),
            disabled=value.get('disabled', False), transparent=value.get('transparent', False),
            hidden=value.get('hidden', False), focused=value.get('focused', False), checked=value.get('checked'),
            selected=value.get('selected'), expanded=value.get('expanded'),
            children=tuple(Node.from_json(x) for x in value.get('children') or ()))

    def walk(self) -> Iterator['Node']:
        """This node and its descendants, in tree order."""
        yield self
        for child in self.children:
            yield from child.walk()

    def describe(self, full: bool = False) -> str:
        """One line: ref, type, id, text, value, bounds, states, tooltip; long texts shortened unless full."""
        parts = [f'#{self.ref}', self.type]
        if self.id:
            parts.append(f'id={self.id}')
        if self.text is not None:
            parts.append(_quote(self.text, full))
        if self.value is not None:
            parts.append(f'value={_quote(self.value, full)}')
        root = f'r{self.root}:' if self.root else ''
        b = self.bounds
        parts.append(f'[{root}{_number(b.x)},{_number(b.y)} {_number(b.width)}x{_number(b.height)}]')
        parts.extend(name for name in ('disabled', 'transparent', 'hidden', 'focused') if getattr(self, name))
        for name, on, off in (('checked', 'checked', 'unchecked'), ('selected', 'selected', None),
                              ('expanded', 'expanded', 'collapsed')):
            state = getattr(self, name)
            if state is True:
                parts.append(on)
            elif state is False and off:
                parts.append(off)
        if self.tip:
            parts.append(f'tip={_quote(self.tip, full)}')
        return ' '.join(parts)

    def format(self, depth: int = 0, full: bool = False) -> str:
        """The node and its descendants, one indented line each."""
        lines = ['  ' * depth + self.describe(full)]
        lines.extend(child.format(depth + 1, full) for child in self.children)
        return '\n'.join(lines)


@dataclass(frozen=True)
class Root:
    """A window or popup; index 0 is the main window."""
    index: int
    kind: str
    title: str | None
    bounds: Bounds
    children: tuple[Node, ...]

    def format(self, full: bool = False) -> str:
        title = f' "{self.title}"' if self.title else ''
        lines = [f'root {self.index} {self.kind}{title} {_number(self.bounds.width)}x{_number(self.bounds.height)}']
        lines.extend(child.format(1, full) for child in self.children)
        return '\n'.join(lines)


@dataclass(frozen=True)
class Tree:
    roots: tuple[Root, ...]

    def walk(self) -> Iterator[Node]:
        """Every node of every root, in tree order."""
        for root in self.roots:
            for child in root.children:
                yield from child.walk()

    def format(self, full: bool = False) -> str:
        return '\n'.join(root.format(full) for root in self.roots)


@dataclass(frozen=True)
class Status:
    process_id: int
    title: str | None
    width: float
    height: float
    render_scaling: float
    task_count: int | None


@dataclass(frozen=True)
class PointerResult:
    """Where the input went: root-local point, the selected element (as found, before the input) and the visual hit."""
    root: int
    x: float
    y: float
    target: Node | None
    hit: str | None


@dataclass(frozen=True)
class WaitResult:
    matches: tuple[Node, ...]
    task_count: int | None


class AutomationError(Exception):
    """The API refused the request: status 404 no match, 408 wait timeout, 409 ambiguous/disabled/covered, ..."""

    def __init__(self, status: int, message: str, candidates: tuple[Node, ...] = ()):
        super().__init__(f'{status}: {message}')
        self.status = status
        self.message = message
        self.candidates = candidates


class LauncherClient:
    def __init__(self, port: int = DEFAULT_PORT, host: str = 'localhost', timeout: float = 60):
        self.base_url = f'http://{host}:{port}'
        self.timeout = timeout

    def status(self) -> Status:
        value = self._request('GET', '/status')
        return Status(value['processId'], value.get('title'), value['width'], value['height'],
                      value['renderScaling'], value.get('taskCount'))

    def tree(self, hidden: bool = False) -> Tree:
        """The semantic tree of every root; its refs become the targets '#N'/int of later calls."""
        value = self._request('GET', '/tree?hidden=1' if hidden else '/tree')
        return Tree(tuple(
            Root(x['index'], x['kind'], x.get('title'), Bounds.from_json(x['bounds']),
                 tuple(Node.from_json(child) for child in x['children']))
            for x in value['roots']))

    def find(self, target=None, **selector) -> list[Node]:
        """The matching elements (without children); also refreshes the refs."""
        value = self._request('POST', '/find', _selector(target, selector))
        return [Node.from_json(x) for x in value['matches']]

    def click(self, target=None, *, at: tuple[float, float] | None = None, root: int = 0, right: bool = False,
              double: bool = False, modifiers=(), **selector) -> PointerResult:
        """Real pointer click at the element's center, or at root-local coordinates `at` of root `root`."""
        return self.pointer('Click', target, at=at, root=root, button='Right' if right else 'Left', double=double,
                            modifiers=modifiers, **selector)

    def hover(self, target=None, *, at: tuple[float, float] | None = None, root: int = 0, **selector) -> PointerResult:
        """Moves the pointer there; it stays, so hover-only UI stays open."""
        return self.pointer('Move', target, at=at, root=root, **selector)

    def wheel(self, target=None, *, delta: float = -3, at: tuple[float, float] | None = None, root: int = 0,
              **selector) -> PointerResult:
        """Mouse wheel in lines; negative scrolls down."""
        return self.pointer('Wheel', target, at=at, root=root, delta=delta, **selector)

    def pointer(self, action: str, target=None, *, at: tuple[float, float] | None = None, root: int = 0,
                button: str = 'Left', double: bool = False, delta: float = 0, modifiers=(),
                **selector) -> PointerResult:
        """Any pointer action: Click, Move, Down, Up or Wheel (Down/Up for drags)."""
        body = {'action': action, 'button': button, 'double': double, 'delta': delta,
                'modifiers': _modifiers(modifiers)}
        if at is not None:
            if target is not None or selector:
                raise TypeError('give either a target/selector or at, not both')
            body.update({'x': at[0], 'y': at[1], 'root': root})
        else:
            body.update(_selector(target, selector))
        value = self._request('POST', '/pointer', body)
        target_node = value.get('target')
        return PointerResult(value['root'], value['x'], value['y'],
                             Node.from_json(target_node) if target_node else None, value.get('hit'))

    def invoke(self, target=None, **selector) -> Node:
        """Invoke, toggle, select or expand through the automation peer, without hit testing."""
        return Node.from_json(self._request('POST', '/invoke', _selector(target, selector))['element'])

    def set_text(self, value: str, target=None, **selector) -> Node:
        """Sets an editable value (text box) or a number (number box, slider) through the automation peer."""
        body = {**_selector(target, selector), 'value': value}
        return Node.from_json(self._request('POST', '/text', body)['element'])

    def key(self, key: str, *, modifiers=(), root: int = 0) -> str | None:
        """Presses and releases an Avalonia Key (Enter, Escape, Tab, ...); returns the focused element."""
        body = {'key': key, 'modifiers': _modifiers(modifiers), 'root': root}
        return self._request('POST', '/key', body).get('focused')

    def type_text(self, text: str, *, root: int = 0) -> str | None:
        """Types text into the focused element; returns it."""
        return self._request('POST', '/key', {'text': text, 'root': root}).get('focused')

    def scroll(self, target=None, **selector) -> Node:
        """Brings the element into view."""
        return Node.from_json(self._request('POST', '/scroll', _selector(target, selector))['element'])

    def wait(self, target=None, *, state: str = 'present', task_count: int | None = None, timeout: float = 30,
             **selector) -> WaitResult:
        """Waits until the element is present/absent/enabled/disabled and/or the task queue has task_count tasks."""
        body = {**_selector(target, selector, required=False), 'state': state.capitalize(), 'timeout': timeout}
        if task_count is not None:
            body['taskCount'] = task_count
        value = self._request('POST', '/wait', body, timeout=timeout + 10)
        return WaitResult(tuple(Node.from_json(x) for x in value['matches']), value.get('taskCount'))

    def screenshot(self, path: str | Path | None = None, target=None, *, scale: float = 1, **selector) -> bytes:
        """PNG of the main window with open popups, or of the selected element; also written to path if given."""
        body = {**_selector(target, selector, required=False), 'scale': scale}
        png = self._request('POST', '/screenshot', body, raw=True)
        if path is not None:
            Path(path).parent.mkdir(parents=True, exist_ok=True)
            Path(path).write_bytes(png)
        return png

    def _request(self, method: str, path: str, body: dict | None = None, timeout: float | None = None,
                 raw: bool = False):
        data = None if body is None else json.dumps(body).encode('utf-8')
        request = urllib.request.Request(self.base_url + path, data=data, method=method)
        if data is not None:
            request.add_header('Content-Type', 'application/json')
        try:
            with urllib.request.urlopen(request, timeout=timeout or self.timeout) as response:
                content = response.read()
        except urllib.error.HTTPError as error:
            content = error.read()
            try:
                parsed = json.loads(content)
            except json.JSONDecodeError:
                parsed = {'error': content.decode('utf-8', errors='replace')}
            candidates = tuple(Node.from_json(x) for x in parsed.get('candidates') or ())
            raise AutomationError(error.code, parsed.get('error', ''), candidates) from None
        return content if raw else json.loads(content)


def _selector(target, selector: dict, required: bool = True) -> dict:
    unknown = selector.keys() - SELECTOR_KEYS
    if unknown:
        raise TypeError(f'unknown selector keys: {", ".join(sorted(unknown))}')
    result = {key: value for key, value in selector.items() if value is not None}
    match target:
        case None:
            pass
        case Node():
            result['ref'] = target.ref
        case int():
            result['ref'] = target
        case str() if target.startswith('#') and target[1:].isdigit():
            result['ref'] = int(target[1:])
        case str():
            result['text'] = target
        case _:
            raise TypeError(f'a target is a text, "#N", an int ref or a Node, not {type(target).__name__}')
    if required and not result.keys() - {'index'}:
        raise TypeError('give a target or at least one of id, text, contains, tip, type, ref')
    return result


def _modifiers(modifiers) -> str:
    names = [modifiers] if isinstance(modifiers, str) else list(modifiers)
    unknown = set(names) - MODIFIERS
    if unknown:
        raise ValueError(f'unknown modifiers: {", ".join(sorted(unknown))}')
    return ', '.join(names) or 'None'


def _quote(text: str, full: bool) -> str:
    text = text.replace('\r', '').replace('\n', ' ⏎ ')
    return json.dumps(text if full or len(text) <= 120 else text[:117] + '...', ensure_ascii=False)


def _number(value: float) -> str:
    return f'{value:g}'

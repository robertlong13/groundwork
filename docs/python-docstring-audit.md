# Python Docstring Audit

## Purpose

Review Python docstrings for tone, density, and usefulness. The goal is
docstrings that help a reader skimming `help()` output or hovering in an
IDE -- not prose essays.

## Style

Google style. PEP 257 baseline.

## Summary Line Rules

The first line is the one-liner that shows up in `help()`. It should be:

- **One line.** If you need more, leave a blank line and continue below.
- **Imperative mood.** "Return", not "Returns". "Wait for", not "Waits for".
  (Matches PEP 257: "Prescribe the function's effect as a command.")
- **End with a period.**
- **No type information.** Types live in annotations, not docstrings.

### Opening Patterns

| Member     | Pattern                                    |
| ---------- | ------------------------------------------ |
| Module     | Noun phrase describing purpose.            |
| Function   | Imperative verb: "Return...", "Parse..."   |
| Class      | Noun phrase: "Line-oriented TCP client..." |
| Method     | Imperative verb, like functions.           |
| Property   | Noun phrase describing what it holds.      |
| Fixture    | Imperative or noun phrase, either works.   |

## When to Write More

A one-liner is preferred. Expand to a multi-line docstring only when:

- The behavior has non-obvious preconditions or side effects.
- The function has multiple parameters whose roles aren't clear from names
  and type hints alone.
- Return values have structure that isn't obvious from the type annotation.
- There are important caveats (timeout behavior, blocking, thread safety).

## Sections (Google Style)

Use only when the one-liner isn't enough. Indent with 4 spaces.

```python
def parse_descriptor(raw: str, strict: bool = False) -> Connection:
    """Parse a MAVProxy-style connection descriptor.

    Args:
        raw: Connection string (e.g., "udpin:14550", "flight.tlog").
        strict: Raise on unrecognized schemes instead of returning None.

    Returns:
        The parsed connection, ready to open.

    Raises:
        ValueError: The descriptor is not a recognized format.
    """
```

Rules for sections:

- **Args:** Skip parameters whose meaning is obvious from name + type
  (e.g., `timeout: float`, `path: Path`, `ct: CancellationToken`). Only
  document what's non-obvious.
- **Returns:** Skip for None-returning functions. Skip when the return is
  obvious from the type hint and the summary line.
- **Raises:** Document exceptions the caller should expect to handle. Skip
  exceptions that indicate programming errors (TypeError, AttributeError).
- **Never** repeat the type annotation in the section text.

## What NOT to Docstring

- **Test functions.** The test name should be descriptive. A docstring on
  `test_arm_disarm_round_trip` adds nothing. Exception: a docstring is fine
  if the test does something non-obvious (e.g., "Tests that arming while
  already armed is a no-op, not an error.").
- **Trivial one-line functions.** `def close(self): self._sock.close()` does
  not need a docstring.
- **Private helpers** that are only called from one place and whose name
  tells the story.

## Common Failure Modes

### 1. Returns/Args echo

Bad:

```python
def get_name(self) -> str:
    """Get the name.

    Returns:
        The name.
    """
```

Good: no docstring at all, or at most:

```python
def get_name(self) -> str:
    """Return the human-readable display name."""
```

### 2. Restating the type signature

Bad:

```python
def connect(host: str, port: int, timeout: float = 5) -> socket.socket:
    """Connect to a host.

    Args:
        host: The host string.
        port: The port integer.
        timeout: The timeout float, defaults to 5.

    Returns:
        A socket object.
    """
```

Good:

```python
def connect(host: str, port: int, timeout: float = 5) -> socket.socket:
    """Open a TCP connection to the remote REPL server."""
```

### 3. Narrating implementation

Bad:

```python
def wait_armed(mav, timeout=10):
    """Wait until armed by polling HEARTBEAT messages in a loop and
    checking the MAV_MODE_FLAG_SAFETY_ARMED bit in base_mode.
    """
```

Good:

```python
def wait_armed(mav, timeout=10):
    """Wait until the vehicle reports armed in HEARTBEAT."""
```

### 4. README prose in a module docstring

Bad:

```python
"""This module provides the main entry point for the parity tracking
system. It was designed to solve the problem of tracking which MAVProxy
commands have been implemented in Groundwork. The approach is to scrape
both codebases..."""
```

Good:

```python
"""MAVProxy parity tracker.

Scrapes MAVProxy's command registrations and Groundwork's command
registrations, then shows a per-module coverage report.
"""
```

## Mechanical Checks

- No non-ASCII characters (smart quotes, em dashes).
- A `--` parenthetical aside in a docstring is a voice smell -- if you need
  a dash-separated clause, the sentence is probably too conversational.
  Not banned, but worth a second look.
- Summary ends with a period.
- No blank line between the summary and the opening `"""` (for one-liners).
- Multi-line docstrings: blank line between summary and body.
- Sections use Google style headers (`Args:`, `Returns:`, `Raises:`), not
  reST (`:param:`), not NumPy (`Parameters\n----------`).

## Audit Process

1. Scan modules, classes, and public functions.
2. Check summary lines against opening patterns.
3. Flag any summary longer than one line.
4. Flag any Args/Returns/Raises section that just restates annotations.
5. Flag docstrings on test functions (unless they add real context).
6. Flag implementation narration or design-doc voice.
7. Run mechanical checks.

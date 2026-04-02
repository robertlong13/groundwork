# C# XML Doc Comment Audit

## Purpose

Review XML doc comments for tone, audience, and tag placement. The goal is
doc comments that read like BCL reference docs, not like design discussions
or READMEs.

## Summary Tag Rules

The `<summary>` is what shows up in IntelliSense. It should be:

- **One sentence.** If you need a second, it belongs in `<remarks>`.
- **Written for the caller**, not the maintainer. Ask: "would someone with
  no access to our Slack history understand this?"
- **Free of implementation details.** How it works internally is not the
  caller's concern.

### Opening Patterns (from BCL conventions)

| Member       | Pattern                                          |
| ------------ | ------------------------------------------------ |
| Class/struct | "Provides..." or "Represents..."                 |
| Interface    | "Defines..." or "Provides..."                    |
| Method       | Start with a verb: "Removes...", "Calculates..." |
| Property     | "Gets or sets...", "Gets...", or "Sets..."       |
| Bool prop    | "Gets or sets a value indicating whether..."     |
| Event        | "Occurs when..."                                 |
| Constructor  | "Initializes a new instance of the ... class."   |
| Enum member  | "Specifies..." or "Indicates..."                 |

## Remarks Tag

Use `<remarks>` for:

- How the class/method behaves in detail (threading, lifecycle, ordering)
- Constraints or preconditions the caller should know about
- Relationships to other types (use `<see cref="..."/>`)

Do **not** put design rationale in `<remarks>` either. If you need to
document why a design decision was made, use an inline `//` comment near
the relevant code.

## Common Failure Modes

### 1. Design-doc voice

Bad:

```csharp
/// <summary>
/// Instance-based warning evaluator. The plugin owns the lifecycle.
/// Runs its own async loop based on EvalIntervalMs.
/// </summary>
```

Good:

```csharp
/// <summary>
/// Evaluates warning rules against a data source and fires events on state transitions.
/// </summary>
/// <remarks>
/// Runs an internal polling loop at the interval specified by
/// <see cref="EvalIntervalMs"/>. The creating plugin is responsible for
/// disposing this instance.
/// </remarks>
```

### 2. Rejected-alternative narration

Bad:

```csharp
/// <summary>
/// Buffers messages in memory rather than writing directly to disk,
/// since direct writes caused latency spikes under load.
/// </summary>
```

Good:

```csharp
/// <summary>
/// Buffers outgoing messages in memory before flushing to disk.
/// </summary>
```

The "why not direct writes" context, if it matters, is an inline comment
next to the buffer implementation, not a doc comment.

### 3. Conversational context leak

Bad:

```csharp
/// <summary>
/// We use a dictionary here instead of a list for O(1) lookups.
/// </summary>
```

Good:

```csharp
/// <summary>
/// Maps sensor IDs to their most recent reading.
/// </summary>
```

"We" doesn't belong in API docs. The data-structure choice is obvious from
the type signature; the summary should describe the *domain meaning*.

### 4. README prose

Bad:

```csharp
/// <summary>
/// This class is the main entry point for the alerting subsystem.
/// It coordinates between the rule engine, the notification dispatcher,
/// and the suppression store to deliver timely, deduplicated alerts
/// to the configured channels.
/// </summary>
```

Good:

```csharp
/// <summary>
/// Provides the entry point for evaluating alert rules and dispatching notifications.
/// </summary>
/// <remarks>
/// Coordinates <see cref="IRuleEngine"/>, <see cref="INotificationDispatcher"/>,
/// and <see cref="ISuppressionStore"/> to deliver deduplicated alerts to
/// configured channels.
/// </remarks>
```

## Mechanical Checks

- No non-ASCII characters (smart quotes, em dashes, ellipsis characters).
  Use `--`, `"`, and `...` instead.
- Summaries end with a period.
- No blank lines inside `<summary>`.
- `<param>` tags exist for every parameter on public methods.
- `<returns>` exists for non-void public methods.
- Bool returns use: `true if CONDITION; otherwise, false.`
- `<exception>` tags document thrown exceptions with the condition that
  triggers them.

## Audit Process

1. Scan public API surface: classes, interfaces, public/protected members.
2. For each `<summary>`, check against the opening patterns table.
3. Flag any summary longer than one sentence.
4. Flag any mention of implementation details, alternative approaches,
   or first-person pronouns ("we", "our", "I").
5. Check that `<remarks>`, if present, is consumer-facing (not maintainer
   notes disguised as docs).
6. Run mechanical checks.

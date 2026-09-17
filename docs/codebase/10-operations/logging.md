# Logging

## Purpose

Two sinks, one formatter, and no file on disk.

## Two sinks, no file logging

`Vortex.Logging` registers exactly two `ILoggerProvider`s, and **neither writes a file**.

### 1 · Console

`VortexConsoleFormatter`, formatter name `"vortex"`, registered by
`LoggingBuilderExtensions.AddVortexConsoleLogger()`.

```
[ts] LVL <category padded to 22> : message
```

ANSI-coloured per level, badges `TRC/DBG/INF/WRN/ERR/CRT/NON`, category trimmed to the last N
namespace segments.

Options bind from `Logging:VortexConsole`: `TimestampFormat`, `UseUtcTimestamp`, `SingleLine`,
`IncludeCategory`, `TrimCategoryDepth` (default 1), `UseAnsiColor`, `IncludeScopes`.

### 2 · The dashboard console feed

`ServerConsoleLoggerProvider` renders through **the same formatter type** and publishes each line to
`ServerConsoleFeed` — a ring buffer, `CONSOLE_FEED_LINES = 2000` — which the dashboard streams over
SSE.

`ServerConsoleFeed.Publish` strips ANSI and fans out to per-viewer bounded channels with
`BoundedChannelFullMode.DropOldest`. A slow viewer drops lines rather than backing up the logger.

`IsEnabled` returns `logLevel != LogLevel.None` **on purpose**, so the factory's configured rules stay
authoritative for both sinks — the feed does not get its own filtering.

> `ServerConsoleFeed` lives in `Vortex.Primitives/Console/` rather than `Vortex.Logging` because two
> processes used to need it: the emulator's logger sink, and the supervisor's carrier for the child's
> stdout. The supervisor is gone, so today only the first is real.
> → [Deployment](../01-runtime/deployment.md)

## There is no rotation

**No file sink, no rolling file, no Serilog, no NLog anywhere in `Vortex.Logging`.**

The only files written under `logs/` are:

| File | Written by |
|---|---|
| `logs/audit-dead-letter.jsonl` | `AuditWriterService` after repeated failures |
| `logs/benchmark/run-*.json` | `BenchmarkReportWriter` |

If you need durable logs, capture stdout at the process manager or container level. That is the
deployment's job, not the emulator's.

## Categories and levels

Standard `ILogger<T>` category names, filtered by the `Logging:LogLevel` map in `appsettings.json`.
Notable entries:

```
Vortex
Vortex.Networking.Package.PackageHandler     ← the per-packet "Incoming {MessageType}" line
Vortex.Networking.Package.PackageEncoder     ← "Outgoing {ComposerType}"
SuperSocket
Orleans
```

The two packet lines are at `Debug`, so they are on in Development and off in production.

## Plugin log attribution

`ConfigurePrefixedLogging(services, host, manifest.Name)` replaces the open-generic `ILogger<>` with
`PrefixedLogger<>`, backed by `PrefixedLoggerFactory(hostLoggerFactory, prefix)`.

So a plugin's lines are attributable to it **while still flowing through the host's factory and level
rules**. Called from `PluginManager.CreatePluginServiceProvider`.

## Correlation

`VortexContextAccessor.BeginScope` opens an `ILogger.BeginScope` dictionary carrying `CorrelationId`
and `Operation`, alongside the `AsyncLocal` context and the Orleans `RequestContext` entry that
survives a grain hop. → [Observability](observability.md)

Set `Logging:VortexConsole:IncludeScopes` to see it in the console.

## Conventions

Rules from `AGENTS.md` that are enforced by review rather than by a tool, and are currently upheld:

| Rule | State |
|---|---|
| Every grain doing cross-grain or DB work injects `ILogger<T>` | upheld |
| **No bare `catch { }`** — always `catch (Exception ex)` and log it | upheld; where a catch deliberately swallows, a comment says why |
| **`.Ignore()` is banned** — use `LogAndForget` | **zero `.Ignore()` repo-wide**; 52 `LogAndForget` sites |
| Repeated failures are rate-limited, not spammed | e.g. `RunTickStepAsync` logs the 1st and every 200th consecutive failure |

`LogAndForget` (`Vortex.Logging/Extensions/TaskLoggingExtensions.cs`) is implemented as
`_ = AwaitAndLogAsync(...)` with a `try/catch → LogError`, **not** `ContinueWith` — the doc explains
that `ContinueWith(..., TaskScheduler.Current)` semantics inside a grain turn are subtle.

## Errors that are not log lines

Two paths deliberately avoid the log as the primary record:

- `IErrorGroupingSink` → `ErrorGroupingChannel` → `ErrorGroupingWriterService` → `error_groups` /
  `error_occurrences`, deduped by fingerprint
- `IAuditSink` → `AuditChannel` → `AuditWriterService` → `audit_events`

Both are bounded channels drained by a background writer, so neither sits on a hot path.
→ [Observability](observability.md)

## `VortexException`

`VortexException(VortexErrorCodeEnum, message?, inner?)` lives in `Vortex.Logging`. A typed error code
rather than a string, so a failure can be matched without parsing a sentence — the same instinct as
the dashboard's `IsDomainCode` check.

## Sources

- `Vortex.Logging/**` — `VortexConsoleFormatter`, `VortexConsoleFormatterOptions`,
  `ServerConsoleLoggerProvider`, `LoggingBuilderExtensions`, `PrefixedLogger`, `PrefixedLoggerFactory`,
  `VortexException`, `Extensions/TaskLoggingExtensions.cs`
- `Vortex.Primitives/Console/ServerConsoleFeed.cs`
- `Vortex.Plugins/PluginManager.cs` — `CreatePluginServiceProvider`
- `Vortex.Observability/Context/VortexContextAccessor.cs`
- `Vortex.Rooms/Grains/RoomGrain.cs` — `RunTickStepAsync`
- `appsettings.json` — `Logging:LogLevel`
- `AGENTS.md` — "Never swallow exceptions silently", "Replace .Ignore() with a LogAndForget helper"

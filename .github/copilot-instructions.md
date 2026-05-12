# Copilot Instructions for LogWatcher

## What This Project Is

LogWatcher is a **real-time log statistics tool** for .NET that watches a local directory for `.log` and `.txt` file
changes, tail-reads new content, parses log lines, and prints rolling statistics to the console every few seconds.
It has a **minimal dependency surface**: primarily the .NET BCL (`System.*`) plus a small set of `Microsoft.Extensions.*` packages (e.g., logging/hosting).

---

## Repository Layout

```
LogWatcher.sln
├── LogWatcher.App/          # CLI host — argument parsing, DI wiring, startup/shutdown
├── LogWatcher.Core/         # Library — all domain logic (see Domain Map below)
├── LogWatcher.Seeder/       # Docker helper that generates synthetic log traffic
├── LogWatcher.Tests/        # All tests (Unit, Integration, Stress)
│   ├── Unit/
│   ├── Integration/
│   └── Stress/
├── docs/                    # Specifications, invariants, domain boundaries
│   ├── project_specification.md
│   ├── domain_boundaries.md
│   ├── invariants.md
│   ├── system_diagram.md
├── global.json              # Pins SDK version (currently .NET 10.0.101)
├── compose.yaml             # Docker Compose for app + seeder
└── .github/
    ├── workflows/ci.yml     # CI pipeline (see CI section)
    └── instructions/        # Additional Copilot context files
```

---

## SDK and Target Framework

- **SDK**: .NET 10.0.101 (pinned in `global.json`)
- **Target framework**: `net10.0` in all projects
- **Nullable reference types**: enabled in all projects — respect nullability; avoid `!` (null-forgiving operator)
- **ImplicitUsings**: enabled

---

## Build, Format, and Test Commands

Always restore before building; the CI does so explicitly.

```bash
# Restore dependencies
dotnet restore

# Format check (CI enforces this — zero changes allowed)
dotnet format --verify-no-changes --no-restore --verbosity normal

# Build (Debug configuration, warning-clean)
dotnet build --no-restore --configuration Debug --verbosity normal

# Run all tests (Release configuration as in CI)
dotnet test --no-restore --configuration Release --verbosity normal

# Run the app locally
dotnet run --project LogWatcher.App -- <watchPath> [options]
```

**Critical**: The CI runs `dotnet format --verify-no-changes` before every build. If you modify C# files, run
`dotnet format` to auto-fix formatting before committing, or the build job will fail.

### CI Pipeline Summary (`ci.yml`)

| Job       | OS                     | Triggers                          |
|-----------|------------------------|-----------------------------------|
| `build`   | ubuntu-latest          | push to `main`, all PRs to `main` |
| `test`    | ubuntu-latest, windows | push to `main`, all PRs to `main` |
| `e2e-test`| ubuntu-latest          | PRs to `main` only (needs Docker) |

- `build`: restore → format-check → build (Debug)
- `test`: restore → `dotnet test` (Release)
- `e2e-test`: `docker compose up --build --abort-on-container-exit` (requires `build` and `test` to pass)

---

## Domain Map (Where Code Belongs)

The codebase is divided into **12 domains** with a strict **unidirectional dependency flow**. See
`docs/domain_boundaries.md` for the authoritative reference.

```
Ingestion → Backpressure → FileManagement → Processing → Statistics → Coordination → Reporting
```

No downstream domain ever calls back into an upstream domain.

| # | Domain                  | Namespace                              | Key Types                                |
|---|-------------------------|----------------------------------------|------------------------------------------|
| 1 | Ingestion               | `LogWatcher.Core.Ingestion`            | `FilesystemWatcherAdapter`, `FsEvent`    |
| 2 | Event Distribution      | `LogWatcher.Core.Backpressure`         | `BoundedEventBus<T>`                     |
| 3 | File State Management   | `LogWatcher.Core.FileManagement`       | `FileStateRegistry`, `FileState`, `PartialLineBuffer` |
| 4 | File Tailing            | `LogWatcher.Core.Processing.Tailing`  | `FileTailer`, `TailReadStatus`           |
| 5 | Line Scanning           | `LogWatcher.Core.Processing.Scanning` | `Utf8LineScanner`                        |
| 6 | Log Parsing             | `LogWatcher.Core.Processing.Parsing`  | `LogParser`, `LogLevel`                  |
| 7 | File Processing         | `LogWatcher.Core.Processing`          | `FileProcessor`                          |
| 8 | Processing Coordination | `LogWatcher.Core.Processing`          | `ProcessingCoordinator`                  |
| 9 | Statistics Collection   | `LogWatcher.Core.Statistics`          | `WorkerStatsBuffer`, `LatencyHistogram`, `TopK` |
|10 | Worker Coordination     | `LogWatcher.Core.Coordination`        | `WorkerStats`                            |
|11 | Reporting               | `LogWatcher.Core.Reporting`           | `Reporter`, `GlobalSnapshot`             |
|12 | CLI & Host              | `LogWatcher.App`                      | `Program`, `ApplicationHost`, `CommandConfiguration` |

**Decision rule — "which domain?"**

1. Calls `FileSystemWatcher`? → Ingestion
2. Manages a bounded queue? → Backpressure
3. Tracks per-file offset/flags? → FileManagement
4. Reads file bytes from disk? → Processing.Tailing
5. Splits bytes on newlines? → Processing.Scanning
6. Parses bytes into log records? → Processing.Parsing
7. Orchestrates tailer+scanner+parser? → Processing.FileProcessor
8. Routes events, manages workers? → Processing.ProcessingCoordinator
9. Accumulates counters/histograms? → Statistics
10. Double-buffer swap protocol? → Coordination
11. Merges buffers, prints output? → Reporting
12. Parses CLI args, wires DI? → LogWatcher.App

---

## Architecture Key Points

### Thread Model (4 thread categories)

1. **Main thread** — orchestrates startup/shutdown; never processes logs directly.
2. **Filesystem watcher threads** — runtime-managed; callbacks must be lightweight and non-blocking.
3. **N worker threads** — dequeue `FsEvent`, acquire per-file gates, run the tailing/scanning/parsing pipeline, update stats.
4. **Reporter thread** — periodically requests buffer swaps, merges inactive buffers, computes percentiles and top-K, prints report.

### Backpressure: Drop-Newest

When `BoundedEventBus<T>` is full, the incoming event is **dropped** and counted. No blocking, no retry.

### Per-File Serialization via Gate

Each `FileState` owns a `Gate` object (`Monitor`). Workers call `Monitor.TryEnter(state.Gate)`. If the gate is held,
the worker sets `IsDirty = true` and moves on. The gate-holder loops until `IsDirty` is clear before releasing,
ensuring every appended byte is eventually processed.

### Double-Buffer Swap Protocol

`WorkerStats` holds two `WorkerStatsBuffer` instances (active + inactive). The reporter calls `RequestSwap()`;
each worker acknowledges at its next safe point. After ack, the reporter reads the inactive buffer and resets it.

### Log Line Format

```
<ISO-8601 timestamp> <level> <message> [latency_ms=<int>]
```

- **Strict ISO-8601** timestamp parsing only — invalid timestamp → malformed line.
- **Level** — `Trace|Debug|Info|Warn|Error|Fatal|Other`; unknown level → `Other` (not malformed).
- **Message key** — first whitespace-delimited token of the message field.
- **Latency** — optional; missing/malformed latency does **not** mark the line malformed.

---

## Invariants

`docs/invariants.md` lists all behavioral guarantees tagged with IDs (e.g., `BP-001`, `FM-003`, `PROC-001`).
Tests are tagged with `[Invariant("ID")]` to declare coverage. Always tag new invariant tests appropriately.

```csharp
[Fact]
[Invariant("BP-001")]
public void Bus_NeverExceedsCapacity() { ... }
```

---

## Testing Conventions

- **Framework**: xUnit 2.9.3; `Assert.*` is the assertion API (not fluent).
- **Global using**: `Xunit` namespace is available everywhere in the test project without an explicit `using`.
- **Test structure**: Mirror source structure — `Unit/`, `Integration/`, `Stress/`.
- **Test naming**: `MethodOrScenario_Condition_ExpectedBehavior` (PascalCase, underscores between parts).
- **Determinism**: No timing-based assertions. No network. No dependency on installed software.
- **Invariant tagging**: Apply `[Invariant("ID")]` from `docs/invariants.md` to all tests that protect a documented invariant. An invariant is an architectural guarantee that crosses a component boundary or describes a system-wide safety property — not all correct behavior qualifies. Tests without `[Invariant]` are complete; the absence of a tag means the behavior is self-contained within one component and needs no cross-cutting documentation.
- **Stress tests**: Live in `Stress/` and may use real threads; they must still terminate reliably without timing-based flakiness.
- **Coverage check**: `InvariantCoverageTests.cs` enforces that every documented invariant in `docs/invariants.md` is covered by at least one `[Invariant("ID")]` test — do not break this.

---

## Design Rules (Critical)

1. **No backward dependencies.** Statistics never calls Processing. Reporting never calls Coordination.
2. **Inject dependencies via constructor.** No static mutable state.
3. **Gate must use try/finally.** `Monitor.Enter`/`Monitor.TryEnter` must always be paired with `Monitor.Exit` in a `finally` block.
4. **Spans do not escape callbacks.** Spans passed to `onChunk`, `onLine`, etc. are only valid for the duration of the callback. Never store them.
5. **Offset advances only on success.** `FileTailer` must not advance the file offset when an IO error occurs.
6. **Immutable over mutable.** Prefer `readonly` fields and value types.
7. **No external NuGet dependencies in `LogWatcher.Core`.** Tests may use xUnit and coverlet.

---

## Common Errors and Workarounds

### Format check fails in CI

**Symptom**: `dotnet format --verify-no-changes` exits non-zero; CI `build` job fails.

**Fix**: Run `dotnet format` locally before pushing. This auto-corrects whitespace, indentation, and brace style.

### `InvariantCoverageTests` fails after adding a new invariant

**Symptom**: Test named `AllInvariants_HaveAtLeastOneTestCovering` or similar fails.

**Fix**: Add at least one test tagged `[Invariant("NEW-ID")]` where `NEW-ID` matches the ID added to
`docs/invariants.md`.

### `dotnet test` passes on Ubuntu but fails on Windows (path separator)

**Symptom**: Tests using hardcoded `/` separators fail on Windows.

**Fix**: Use `Path.Combine` or `Path.DirectorySeparatorChar` instead of literal `/` in file path strings in tests.

### Docker e2e test fails with permission denied on `./temp`

**Symptom**: `docker compose up` fails writing to `./temp`.

**Fix**: The CI pre-creates `./temp` with `mkdir -p ./temp && chmod 777 ./temp`. Locally, create this directory before running `docker compose up`.

---

## Documentation Sources (Start Here for Requirements)

| File | Purpose |
|------|---------|
| `docs/project_specification.md` | Non-technical overview; scope, risks, success criteria |
| `docs/domain_boundaries.md` | Authoritative 12-domain map with responsibilities |
| `docs/invariants.md` | All behavioral guarantees; invariant IDs for test tagging |
| `docs/system_diagram.md` | Mermaid architecture diagrams |
| `.github/instructions/copilot-instructions.md` | Anti-patterns, good patterns, decision tree (separate file from this one) |
| `.github/instructions/dotnet.instructions.md` | .NET/C# coding standards for this repo |

When agent assumptions conflict with `docs/`, the documentation is authoritative.

---

## Useful Commands (Quick Reference)

```bash
# Format and fix all files in place
dotnet format

# Build only (skip restore)
dotnet build --no-restore -c Debug

# Run tests with detailed output
dotnet test --no-restore -c Release --logger "console;verbosity=detailed"

# Run a specific test class
dotnet test --no-restore -c Release --filter "FullyQualifiedName~BoundedEventBusTests"

# Run the app (watch ./logs, 4 workers, 1-second interval)
dotnet run --project LogWatcher.App -- ./logs --workers 4 --report-interval 1

# Docker Compose (app + log seeder)
mkdir -p ./temp && chmod 777 ./temp
docker compose up --build
```

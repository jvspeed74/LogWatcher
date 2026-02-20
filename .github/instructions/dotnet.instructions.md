# .NET / C# Instructions

These instructions apply to all C#/.NET changes in this repository. They are intended to constrain assumptions and keep
changes reliable, testable, and maintainable.

## Platform and Build

- Target the repository’s configured .NET SDK and target framework.
- Keep builds warning-clean. Do not introduce new warnings.
- Nullable reference types must be respected. Avoid widespread use of the null-forgiving operator (`!`).

## Permissions and Environment Assumptions

- Do not assume administrator privileges.
- Prefer APIs and behaviors that work under standard user permissions.
- Avoid privileged sinks or configuration (e.g., Windows Event Log, services, machine-wide installs) unless explicitly
  required.

## Configuration Discipline

- Treat configuration as authoritative; code should not encode environment-specific values or assumptions.
- Validate configuration early and fail fast with actionable error messages when required values are missing or invalid.
- Defaults may exist, but must not replace or obscure configurability.

## Design and Maintainability

- Prefer simple, explicit code paths over heavy abstraction.
- Keep types and methods focused on a single responsibility; avoid oversized or overly generic constructs.
- Avoid global mutable state.
- Avoid reflection and dynamic behavior unless explicitly justified.

## Error Handling

- Do not swallow exceptions silently.
- Fail fast on invalid inputs or invalid configuration with actionable error messages.
- Use consistent error semantics; avoid mixing exception-based control flow with silent continuation.

## Async and Concurrency

- Avoid blocking on async work (`.Result`, `.Wait()`).
- Avoid `async void` except for true event handlers.
- Ensure cancellation and timeouts are honored where asynchronous operations are used.

## Time and Determinism

- Avoid implicit reliance on system time in logic that is tested.
- Prefer isolating or injecting time to keep behavior deterministic.
- Be explicit about time zones and offsets; avoid mixing local and UTC implicitly.

## IO and Resource Lifetime

- Manage `IDisposable` and `IAsyncDisposable` lifetimes correctly; dispose deterministically when required.
- Prefer safe IO patterns that do not leave partially-written state on failure.
- Do not assume paths or resources are writable; surface actionable errors when they are not.

## Dependencies

- Prefer the .NET BCL (`System.*`) over third-party libraries unless there is a clear, documented need.
- Keep dependencies minimal. If adding a dependency, ensure it is mature and justify it in the PR description.

## Testing

- Any behavior change requires tests.
- Tests must be deterministic: no reliance on machine-specific state (network, installed software, local environment
  quirks), and no timing-based flakiness.
- Prefer validating behavior and contracts rather than internal structure.

## CLI and Output (if applicable)

- Keep console output concise and stable for automation.
- Avoid verbose output by default; add verbosity only behind an explicit flag if needed.
- Keep exit codes stable if used.

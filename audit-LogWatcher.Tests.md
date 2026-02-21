# Rule Audit — LogWatcher.Tests

Rules evaluated against: `.github/instructions/dotnet.instructions.md`

---

## Violations

### Language — Manual collection initialization → collection expressions

**Files:** `Integration/ProcessingCoordinatorTests.cs`, `Integration/ReporterTests.cs`, `Integration/HostLifecycleTests.cs`, `Unit/App/CommandConfigurationTests.cs`

Multiple test files use `new[]` where collection expressions should be used:

```csharp
// ProcessingCoordinatorTests.cs (multiple tests)
var workerStats = new[] { new WorkerStats() };
var workerStats = new[] { new WorkerStats(), new WorkerStats() };

// ReporterTests.cs
var workers = new[] { new WorkerStats() };
var workers = new[] { ws };

// HostLifecycleTests.cs
var workerStats = new[] { new WorkerStats() };

// CommandConfigurationTests.cs
var args = new[] { _tmpDir };
var args = new[] { _tmpDir, "--workers", "3", "-q", "500", "-r", "5", "-k", "7" };
```

Rule: *"Manual collection initialization → collection expressions"*  
Should be: `[new WorkerStats()]`, `[ws]`, `[_tmpDir]`, etc.

---

### Testing — Test asserts on trivially true construction state, not observable behaviour

**File:** `Integration/ProcessingCoordinatorTests.cs`

```csharp
[Fact]
[Invariant("PROC-007")]
public void ProcessingCoordinator_WorkerCount_IsFixedAtConstruction()
{
    // ...
    const int workerCount = 3;
    var workerStats = new WorkerStats[workerCount];
    // ...
    Assert.Equal(workerCount, workerStats.Length);  // asserts the array the test itself created
}
```

The sole assertion checks `workerStats.Length`, which is a value the test set itself. It does not exercise any observable behaviour of `ProcessingCoordinator`. The test passes vacuously regardless of the coordinator's implementation.

Rule: *"Tests must assert on observable outcomes, not implementation steps. Tests that break on behavior-preserving refactors are wrong."*

---

### Testing — Timing-dependent synchronisation via fixed `Thread.Sleep`

**Files:** `Integration/ReporterTests.cs`, `Integration/ProcessingCoordinatorTests.cs`, `Integration/HostLifecycleTests.cs`

Tests rely on fixed sleep durations to give background threads time to process before assertions are made:

```csharp
// ReporterTests.cs
Thread.Sleep(1500); // allow at least one interval report
Thread.Sleep(1500); // allow at least one interval with a forced ack timeout

// ProcessingCoordinatorTests.cs
Thread.Sleep(300);
Thread.Sleep(500);
Thread.Sleep(1000);

// HostLifecycleTests.cs
Thread.Sleep(100);
Thread.Sleep(200);
```

These create implicit timing dependencies: if the system is under load or the implementation is refactored to use a different concurrency model, the fixed waits may no longer allow enough time for the background work to complete, causing intermittent failures without any change to observable behaviour.

Rule: *"Tests must assert on observable outcomes, not implementation steps. Tests that break on behavior-preserving refactors are wrong."*

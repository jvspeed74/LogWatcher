# Rule Audit — LogWatcher.App

Rules evaluated against: `.github/instructions/dotnet.instructions.md`

---

## Violations

### Language — Manual collection initialization → collection expressions

**File:** `CommandConfiguration.cs`

Four option aliases are constructed with `new[]` instead of collection expressions:

```csharp
// Lines 38, 61, 84, 107
var workersOpt        = new Option<int>("--workers",                new[] { "--workers", "-w" })
var queueCapacityOpt  = new Option<int>("--queue-capacity",         new[] { "--queue-capacity", "-q" })
var reportIntervalOpt = new Option<int>("--report-interval-seconds",new[] { "--report-interval-seconds", "-r" })
var topKOpt           = new Option<int>("--topk",                   new[] { "--topk", "-k" })
```

Rule: *"Manual collection initialization → collection expressions"*  
Should be: `["--workers", "-w"]`, etc.

---

### Dependency Inversion — Hand-rolled Singleton / static mutable state

**File:** `ApplicationHost.cs`

```csharp
private static int _shutdownRequested = 0;
private static readonly ManualResetEventSlim _shutdownEvent = new(false);
```

`ApplicationHost` is a `static` class that holds mutable global state. Application-lifetime state should be registered via a DI container and injected, not stored in static fields.

Rule: *"NEVER hand-roll a Singleton — use DI lifetime registration."*  
Prohibited by Default table: *"Hand-rolled Singleton | Manual Singleton class | DI container lifetime"*

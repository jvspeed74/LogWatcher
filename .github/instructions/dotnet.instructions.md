---
applyTo: "**/*.cs, **/*.csproj"
---

# .NET / C# Instructions

Over-engineering is a defect. Speculative complexity will be maintained forever.

---

## Language

NEVER use these when modern equivalents exist:

- Mutable-field DTOs → `record`
- Null-check sprawl → `required` properties
- `if/else` type-dispatch → pattern matching
- Single-method interfaces for callbacks → `Func<T>` / `Action<T>`
- Manual collection initialization → collection expressions

Target latest stable .NET LTS unless the project explicitly pins a version.

---

## SOLID

**S — Single Responsibility**
The unit is a *reason to change*, not a method count. DO NOT split a class unless it has two genuinely independent
reasons to change.

**O — Open/Closed**
Extract an abstraction only when the *second* concrete implementation exists. NEVER design for imagined future
implementations. Apply via composition, not inheritance.

**L — Liskov**
Subtypes must honor the full behavioral contract of their base. NEVER override to throw `NotSupportedException`. NEVER
return `null` from a non-null return type. Violations are always wrong.

**I — Interface Segregation**
Define interfaces to describe what a *consumer needs*, not what a provider can do. NEVER create `IFoo`/`Foo` pairs for
single implementations without a test double need. Interface is justified only when:

- 2+ concrete implementations exist
- Outbound infrastructure boundary (DB, external API, messaging, filesystem)
- A meaningfully different test double is required

**D — Dependency Inversion**
DIP applies at architectural boundaries. NEVER inject `IServiceProvider` into business logic (service locator). NEVER
hand-roll a Singleton — use DI lifetime registration. DO NOT create interfaces between a class and its internal helpers.

Constructor dependency limit: ≤4. At 5+, split the class.

---

## Design Patterns

DO NOT re-implement patterns C# has absorbed:

| Pattern           | NEVER                        | INSTEAD                      |
|-------------------|------------------------------|------------------------------|
| Iterator          | Custom enumerator class      | `yield return`, LINQ         |
| Observer          | Manual subscription system   | `event`, `IObservable<T>`    |
| Strategy (simple) | Single-method interface      | `Func<T, TResult>` parameter |
| Command (simple)  | Command class hierarchy      | `record` + `Action<T>`       |
| Template Method   | Abstract base with overrides | Composition + delegates      |
| Visitor           | Double-dispatch hierarchy    | Pattern matching             |

NEVER use Service Locator. DO NOT add MediatR unless a cross-cutting pipeline across multiple handlers is genuinely
required and the project already uses it. DO NOT use Repository as a reflexive EF wrapper — only when abstracting
multiple storage backends or integration test isolation requires a real seam.

---

## Architecture

DO NOT apply Clean/Layered Architecture to CRUD services. DO NOT force Controller → Service → Repository for simple
reads. NEVER mix architectural conventions within a project.

Default for new API projects: Vertical Slice. Each feature owns endpoint through data access. NO shared service layers
across features without clear justification.

Apply Clean Architecture only when all three are true: domain logic is genuinely complex, project is long-lived, and
infrastructure independence is a real (not speculative) requirement.

---

## Domain Modeling

NEVER allow an entity to be constructed in an invalid state. NEVER expose `public set` on invariant properties. Business
logic belongs on the entity, not in service classes that mutate it externally.

```csharp
// ❌ Invalid state constructible; logic lives elsewhere
public class Order { public Guid Id { get; set; } public OrderStatus Status { get; set; } }

// ✅ Invariants enforced at boundary
public class Order
{
    public Guid Id { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public Order(Guid id) => Id = id;
    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only pending orders can be confirmed.");
        Status = OrderStatus.Confirmed;
    }
}

// ✅ Value object
public record Money(decimal Amount, string Currency);
```

---

## Error Handling

NEVER throw exceptions for expected business outcomes (not-found, validation failure, business rule violation). NEVER
return `null` to signal a missing result.

```csharp
// ❌
if (order is null) throw new NotFoundException($"Order {id} not found");

// ✅ Caller forced to handle both paths
public async Task<Result<Order>> GetOrderAsync(Guid id)
{
    var order = await _db.Orders.FindAsync(id);
    return order is null ? Result<Order>.Fail($"Order {id} not found") : Result<Order>.Ok(order);
}
```

Use ErrorOr, OneOf, FluentResults, or define `Result<T>` once per project — not once per feature.

---

## Dependency Injection

NEVER inject a `Scoped` service into a `Singleton`. `DbContext` is Scoped — this is a correctness bug under load. When a
Singleton needs scoped services, inject `IServiceScopeFactory` and create a scope explicitly.

Property injection only for genuinely optional dependencies where the class is fully functional without them.
Constructor injection is the default.

---

## Testing

DO NOT write tests that verify wiring — `Setup().Returns()` assertions test the mock, not behavior. Mock only at
outbound infrastructure boundaries where a meaningful fake differs from the real implementation.

DO NOT write unit tests for thin handlers with no domain logic — use integration tests covering the full slice from
request to database.

Tests must assert on observable outcomes, not implementation steps. Tests that break on behavior-preserving refactors
are wrong.

---

## BCL — Never Re-Implement What Exists

| Operation                       | NEVER                           | ALWAYS                                                                         |
|---------------------------------|---------------------------------|--------------------------------------------------------------------------------|
| Span byte search                | hand-rolled `for` loop          | `span.IndexOf(value)`                                                          |
| Span subsequence search         | hand-rolled O(n×m) loop         | `span.IndexOf(needle)` / `MemoryExtensions.IndexOf`                            |
| Case-insensitive span compare   | byte-by-byte compare            | `MemoryExtensions.Equals(span, "VALUE"u8, StringComparison.OrdinalIgnoreCase)` |
| UTF-8 integer parsing           | digit accumulation loop         | `Utf8Parser.TryParse`                                                          |
| UTF-8 byte literals             | `new byte[] { (byte)'a', ... }` | `"ab"u8`                                                                       |
| Bounded producer/consumer queue | `Queue<T>` + `Monitor`          | `Channel.CreateBounded<T>`                                                     |
| Periodic background work        | `Thread` + `Thread.Sleep` loop  | `PeriodicTimer`                                                                |
| Monotonic elapsed time          | `DateTime.UtcNow` subtraction   | `Stopwatch` / `Environment.TickCount64`                                        |

NEVER declare `static ReadOnlySpan<byte>` as a property — `=> new byte[]` allocates on every call. Use `"value"u8`
directly or `static readonly byte[]`.

```csharp
// ❌ Allocates on every access
static ReadOnlySpan<byte> Prefix => new byte[] { (byte)'f', (byte)'o', (byte)'o' };
// ✅ Zero allocation
static ReadOnlySpan<byte> Prefix => "foo"u8;
```

NEVER use `DateTime.UtcNow` for elapsed time or deadline calculations — not monotonic, drifts under NTP. Use
`Stopwatch.GetTimestamp()` or `Environment.TickCount64`.

---

## Concurrency Primitives

| Need                                                | Use                                              |
|-----------------------------------------------------|--------------------------------------------------|
| Mutual exclusion on shared mutable state            | `lock` / `Monitor.Enter`                         |
| Non-blocking flag visibility across threads         | `Volatile.Read` / `Volatile.Write`               |
| Atomic increment / decrement / swap on one variable | `Interlocked.Increment` / `Interlocked.Exchange` |
| One-shot or resettable signal between threads       | `ManualResetEventSlim`                           |
| Bounded producer/consumer queue                     | `Channel.CreateBounded<T>`                       |
| Per-resource exclusive access without blocking      | `Monitor.TryEnter` with `try/finally`            |

NEVER use `Interlocked` for multi-field atomic updates — use `lock`. ALWAYS release locks in `finally`.

```csharp
// ❌ Lock leaked if DoWork() throws
Monitor.Enter(gate); DoWork(); Monitor.Exit(gate);
// ✅
Monitor.Enter(gate);
try { DoWork(); } finally { Monitor.Exit(gate); }
```

DO NOT use `Task.Run` to parallelize waits in a background loop — allocates a `Task` per iteration. Use sequential
waits; `Parallel.ForEach` only when parallelism is justified.

---

## Pipeline and Systems-Level Code

### Interfaces

DO NOT create interfaces for stateless static utilities, pure functions, or internal pipeline stages. A real seam is an
I/O boundary or a point where a test double produces meaningfully different behavior.

```csharp
// ❌ Stateless parser — no meaningful test double
public interface IRecordParser { bool TryParse(ReadOnlySpan<byte> input, out ParsedRecord result); }
// ✅ I/O boundary — meaningful in-memory fake justifies interface
public interface IDataReader { ReadStatus ReadNext(ref long offset, Action<ReadOnlySpan<byte>> onChunk); }
```

### Nested Callbacks

NEVER nest processing logic 3+ levels deep — control flow becomes untraceable, inner logic untestable. Extract one named
private method per level.

```csharp
// ❌ Inner logic untestable without full outer stack
reader.Read(ref offset, chunk => {
    scanner.Scan(chunk, ref carry, line => {
        if (!parser.TryParse(line, out var record)) { stats.Malformed++; return; }
        stats.Processed++;
    });
});
// ✅
reader.Read(ref offset, chunk => ProcessChunk(chunk, stats));
private void ProcessChunk(ReadOnlySpan<byte> chunk, StatsBuffer stats)
    => scanner.Scan(chunk, ref _carry, line => ProcessLine(line, stats));
private void ProcessLine(ReadOnlySpan<byte> line, StatsBuffer stats) { ... }
```

### Hot-Path Struct Fields

DO NOT reflexively convert fields to properties on mutable structs and hot-path state objects — adds indirection, emits
false encapsulation signal on types explicitly mutable by design.

```csharp
// ❌ Wrong signal, no benefit
public struct LineBuffer { public int Length { get; private set; } }
// ✅
public struct LineBuffer { public int Length; }
```

### Allocation Coherence

NEVER introduce allocations in code whose design contract is zero/minimal allocation. In per-chunk, per-line, or
per-interval hot paths, NEVER:

- `new byte[]` construction
- LINQ enumeration
- `Task.Run` or `Task` allocation
- Boxing of value types
- Undocumented string allocation (if unavoidable, add a comment at the site)

---

## Prohibited by Default

| AI default                                      | NEVER                                       | Correct                                                         |
|-------------------------------------------------|---------------------------------------------|-----------------------------------------------------------------|
| `IXxx`/`Xxx` pair for every class               | Interface without real seam                 | Interface only at I/O boundary or meaningful test double        |
| 5+ constructor dependencies                     | More injection to fix design problem        | Split class; ≤4 deps each                                       |
| `throw NotFoundException` for business outcomes | Exceptions as flow control                  | `Result<T>`                                                     |
| All `public set` on entities                    | Setters on invariant properties             | Private setters; enforce at construction                        |
| Repository for every EF entity                  | Reflexive Repository                        | `DbContext` directly unless multiple stores or isolation needed |
| MediatR everywhere                              | MediatR without cross-cutting pipeline need | Add only when handlers share cross-cutting behavior             |
| `IServiceProvider` in logic                     | Service locator                             | Constructor injection; `IServiceScopeFactory` for scope         |
| Hand-rolled Singleton                           | Manual Singleton class                      | DI container lifetime                                           |
| Inheritance for behavior sharing                | Inherit to reuse behavior                   | Composition                                                     |
| Preemptive abstractions                         | Extension points before second use case     | Extract when second case is real                                |
| Clean Architecture on CRUD                      | Layered architecture on simple services     | Vertical slices or direct handlers                              |
| Hand-rolled span/byte search                    | Manual loops for BCL operations             | `span.IndexOf`, `MemoryExtensions`                              |
| Hand-rolled UTF-8 integer parse                 | Digit accumulation                          | `Utf8Parser.TryParse`                                           |
| `new byte[]` for UTF-8 literals                 | Byte array for constant sequences           | `"value"u8`                                                     |
| `static ReadOnlySpan<byte>` property            | `=> new byte[] { ... }`                     | `"value"u8` or `static readonly byte[]`                         |
| `Task.Run` in background loops                  | Task allocation in tight loops              | Sequential waits; `Parallel.ForEach` only when justified        |
| `DateTime.UtcNow` for elapsed time              | Non-monotonic time for deadlines            | `Stopwatch` / `Environment.TickCount64`                         |
| External package when spec says manual          | Package for specified manual work           | Implement what was specified                                    |
| Nested lambda chains 3+ levels                  | Callbacks beyond two levels                 | Named private method per level                                  |
| Properties on hot-path mutable structs          | Wrapping directly-mutated fields            | Fields where direct mutation is the design                      |
| `Queue<T>` + `Monitor` bus                      | Hand-rolled bounded queue                   | `Channel.CreateBounded<T>`                                      |

---

## Decision Thresholds

| Decision                 | Condition                                                                             |
|--------------------------|---------------------------------------------------------------------------------------|
| Create interface         | 2+ impls, OR outbound infra boundary, OR meaningfully different test double           |
| Use base class           | NEVER for behavior sharing — composition only                                         |
| Add MediatR              | Cross-cutting pipeline behavior across multiple handlers                              |
| Use Repository           | Multiple storage backends OR integration test isolation genuinely requires it         |
| Apply Clean Architecture | Complex domain + long-lived project + real infra independence requirement (all three) |
| Split a class            | 2+ independent reasons to change, OR 4+ constructor dependencies                      |
| Use `record`             | Anything immutable and data-shaped: DTOs, commands, queries, value objects, events    |
| Add `virtual`            | Concrete extension point known to be needed now                                       |
| Extract a method         | Logic nested 2+ levels in callback, OR logic needs independent testing                |
# Invariant Definition

This document defines what an invariant is, what properties every invariant must have, and what
rules govern its declaration. It applies to any software system regardless of language, platform,
or size.

---

## 1. Definition

An **invariant** is a typed, machine-enforced architectural guarantee about behavior at a module
boundary, or about a system-wide safety property that multiple modules depend on.

**Outward-facing.** An invariant is not a description of what a module does internally. It is a
commitment the module makes to the outside — what any module that interacts with this boundary can
always rely on, or a commitment to the health of the shared runtime environment that all modules
operate in.

**Machine-enforced.** An invariant is assigned a unique ID. Every test that protects its guarantee
is tagged with that ID. A coverage test fails the build when any invariant ID has no tagged test.
An architectural guarantee that cannot be machine-enforced is not an invariant — it is documentation.
Write documentation when you must; write invariants when the guarantee must be enforced.

**Typed.** The severity of a violation determines the invariant's type. See Section 3.

An invariant is **not** a test. A test exercises an invariant. The invariant is the property the
test is written to protect.

An invariant is **not** a contract entry. A module's Contracts section declares what crosses its
boundary — behavioral interfaces and data structures. A `contract`-type invariant states a guarantee
about those crossings — a precondition, postcondition, lifetime constraint, or ordering requirement.
Every `contract` invariant traces to a declared interaction in a module's Contracts section; the
Contracts section does not declare the guarantee itself.

---

## 2. Required Properties

Every invariant definition must include all of the following properties. A definition is incomplete
if any is absent or does not satisfy its constraint.

| Property | Constraint |
|---|---|
| **ID** | A unique identifier in the form `[MODULE_PREFIX]-[NNN]`, where `MODULE_PREFIX` is the short code for the owning module and `NNN` is a zero-padded sequential number within that module's set. IDs are stable: once published, an ID is never reassigned to a different guarantee. When an invariant is retired, its ID is retired with it. |
| **Type** | Exactly one of `strict`, `contract`, `resource`, or `behavioral`. See Section 3. |
| **Modules** | The module(s) responsible for upholding the guarantee. The owning module — the one at whose boundary the guarantee is declared — is named first. For `contract` invariants, all modules party to the shared assumption must be named; a `contract` invariant naming only one module is incomplete. For `strict` and `behavioral` invariants, additional modules may be named when the guarantee is upheld jointly — when no single module alone can fulfill or violate it. For `resource` invariants, only the owning module is named. |
| **Description** | One declarative sentence. States what is always true. Not a mechanism. Not an implementation detail. For `contract` invariants, written as a shared assumption both sides depend on — which side has the obligation (the caller, the callee, or both) is clear from the sentence itself. |

---

## 3. Types

The type is determined by what a violation causes. Pick the first row that matches.

| Type | A violation means | Violation severity |
|---|---|---|
| `strict` | Data loss, corruption, or a crash | The system cannot continue correctly |
| `contract` | A shared assumption at a module boundary is broken — one side expects a condition the other does not satisfy | The interaction between two modules is incorrect, regardless of system state |
| `resource` | The module's implementation consumes a shared runtime resource in a pattern, or omits a structural property, that degrades the ambient environment other modules operate in — while the module's functional output remains correct | The shared environment (CPU, memory, scheduling) is degraded for modules with no declared relationship to the violating module |
| `behavioral` | Observable behavior degrades while the system remains operational | The system survives but delivers reduced guarantees |

**Classification priority matters.** Apply in order: if a violation would cause data corruption,
classify `strict` even if it also breaks a boundary assumption. `contract` takes priority over
`resource` because a violated boundary assumption names a specific structural defect between two
parties. `resource` takes priority over `behavioral` because ambient environmental harm is
undirected and cannot be declared as a boundary assumption between named parties.

**Notes on each type:**

`strict` invariants state absolute properties — what can never happen. They are unconditional.
Violation means the system is in a state from which correct recovery is not guaranteed.

`contract` invariants state shared assumptions at a module boundary. A precondition places an
obligation on the caller. A postcondition places an obligation on the callee. A lifetime constraint
places an obligation on both regarding the validity window of a value. An ordering requirement places
an obligation on the sequence of operations. All of these are `contract` invariants.

`resource` invariants constrain how a module interacts with the shared runtime environment, not
what it produces. They cover two forms: prohibitions ("must not allocate heap objects per unit of
processed work") and requirements ("must include a yield point at each catch-up loop iteration").
Both protect the same thing — the ambient CPU, memory, or scheduling environment that all modules
operate in, regardless of declared relationships. The violation is always a pattern; a single
instance is never the harm. When ambient harm becomes directed enough that a specific named module's
correctness depends on its absence, the invariant escalates to `contract`.

`behavioral` invariants state what is true under normal operation. They do not promise what happens
under resource exhaustion, OS errors, or other degraded conditions — they state what the module
delivers when those conditions are absent.

---

## 4. Rules

The following rules are unconditional. There are no exceptions.

**Rule 1.** An invariant must cross a module boundary or be a system-wide safety property that
multiple modules depend on. Correct behavior that is self-contained within one module and not
relied on by any caller is not an invariant. Leave such tests untagged.

**Rule 2.** An invariant must be deterministically testable. A test can be written that reliably
fails when the guarantee is violated, without relying on OS behavior, timing, or probabilistic
conditions. If a guarantee cannot be stated in a form that is deterministically testable, it is not
an invariant. Either restate it as an observable condition, restructure the module boundary to make
it testable, or make no formal claim.

**Rule 3.** The description must state what is true, not how it is achieved. An invariant is
independent of implementation. If the implementation changes while the guarantee holds, the
invariant is unchanged. If the description would need to change when the implementation changes,
the description names a mechanism, not a guarantee.

**Rule 4.** The owning module is the module named in the invariant's ID prefix. For `strict` and
`behavioral` invariants, the owning module is the one at whose boundary the guarantee is declared.
Additional modules may be named for two valid reasons:

- **Joint responsibility.** Neither module alone can fulfill or violate the guarantee. Both must
  do their part, and either failing causes a violation. The guarantee is a system-level property
  that emerges from the correct behavior of all named modules. Joint responsibility is distinct
  from `contract`: there is no declared boundary assumption between the parties.

- **Direct integration.** The additional module's infrastructure, boundary, or protocol is what
  the guarantee is expressed in terms of — the guarantee cannot be fully understood without
  knowing that module is in the loop. The owning module is still the primary source of the
  guarantee; the additional module is named because it is mechanically load-bearing to how the
  guarantee is described, not because it can independently cause a violation.

A module that merely depends on this guarantee being true should not be listed. That relationship
is `contract`, not joint ownership. For `contract` invariants, the owning module is the one at whose boundary the
assumption is defined — typically the module being called, since it is the one defining the terms
of the interaction. For `resource` invariants, the owning module is the one whose implementation
must be constrained to prevent the ambient harm; only the owning module is named.

**Rule 5.** For `contract` invariants, all modules party to the shared assumption must be named
in the Modules field. "Party to the assumption" means: the module that makes the commitment, and
every module that relies on that commitment being true. A `contract` invariant naming only one
module is structurally incomplete — there is no contract without two parties.

**Rule 6.** Every `contract` invariant describes an assumption about an interaction that is already
declared in the Contracts section of at least one named module. A `contract` invariant that cannot
be traced to a declared interaction reveals either an incomplete module definition or an invariant
placed at a boundary that does not exist in the module definitions.

**Rule 7.** A module's resource consumption pattern is a `resource` invariant when a violation
degrades the ambient environment other modules operate in, regardless of whether those modules have
a declared relationship to the violating module. Per-line or per-chunk heap allocation in the hot
path qualifies: the module's output (parsed records, incremented counters) is correct throughout,
but the GC pressure harms all threads. Allocation during startup or reporting does not qualify —
it is self-contained and does not degrade the ambient environment in a sustained pattern. If a
resource consumption pattern eventually causes a crash, classify it `strict`, not `resource`.

**Rule 8.** A guarantee about a value that crosses a module boundary qualifies as an invariant
only if at least one specific downstream module depends on the guarantee being true. Dependency
takes one of two forms:

- **Discriminating value.** A specific return value (null, false, empty, a sentinel) that the
  downstream module branches on — it routes, filters, or handles differently based on that
  specific value, and would behave incorrectly if the value were absent or changed.

- **Property guarantee.** A claim about a value's format, ordering, or mathematical relationship
  (UTC, monotonically non-decreasing, non-negative) that the downstream module computes with —
  producing output that would be materially incorrect if the property were violated.

Name the specific downstream module and state which form applies before declaring the invariant.
If no specific module can be named, the guarantee is self-contained even though the value crosses
the boundary.

**Rule 9.** Once published, an invariant's ID is stable. If a guarantee changes in meaning,
retire the old invariant and declare a new one with a new ID. Reusing a retired ID for a different
guarantee invalidates any test that was tagged with it.

---

## 5. Validation Gauntlet

**Step 1 — Qualify.** These gates determine whether the candidate is an invariant at all.
A NO at any gate disqualifies the candidate.

Gate 1: Does this candidate describe behavior that crosses a module boundary, or a safety property
that multiple modules depend on being true?
→ YES → Sub-step (Rule 8): Name the specific downstream module — or, for candidates where the
  harm is to the shared runtime environment, name the shared resource (GC heap, OS scheduler,
  lock) — and state what becomes incorrect when this guarantee is violated. The dependency must
  take one of two forms: a downstream module that branches on a discriminating return value, or
  a downstream module that computes with a property guarantee and produces wrong output when the
  property is violated.
  → Named and stated → Gate 2
  → Cannot name one → NOT an invariant. The value may cross the boundary, but the specific
  guarantee is self-contained. Leave the test untagged.
→ NO → Not an invariant. Behavior self-contained within one module is not an architectural
guarantee. Leave the test untagged.

Gate 2: Is the effect observable — either directly at the module boundary, or through a specific,
directly measurable structural property of the module's implementation?
→ YES → Gate 3
→ NO → Not an invariant. Implementation details with no observable effect on callers or on the
shared environment do not qualify, even if essential to the module's correct operation.
Note: for `resource` candidates, the implementation act (an allocation, a spin, a signal, a lock
hold) is often invisible at the module boundary. What must be observable is the specific structural
property — allocation count per call, wakeup count per event, yield call count per iteration, lock
hold scope — not the downstream environmental effect. Gate 3's test will target this property
directly. A module that is generally slower or more resource-intensive — with no single isolatable
structural property a test can directly measure — does not qualify. A legitimate `resource`
candidate names a single implementation choice that, if made differently, would eliminate the
ambient harm entirely.

Gate 3: Can a deterministic test be written that fails when this guarantee is violated — without
relying on OS behavior, timing, or probabilistic conditions?
→ YES → Step 2
→ NO → Not an invariant as stated. Restate the guarantee as an observable condition, or restructure
the module boundary so that the guarantee becomes testable. If neither is possible, make no formal
claim.

**Step 2 — Describe.** These gates check whether the invariant is properly defined before
classifying it. By this point you will typically have a working hypothesis about the type — the
nature of the guarantee usually makes this apparent. Gate 5 uses that hypothesis. If you are
uncertain, treat the candidate as `contract` (the more restrictive case) and confirm in Step 3.

Gate 4: Is the description a single declarative statement of what is always true — not a description
of how the implementation achieves it?
→ YES → Gate 5
→ NO → Restate. The description must be independent of implementation. Ask: if the internal
mechanism changed while the guarantee held, would this sentence still be true? If no, it names
a mechanism.

Gate 5: Does the Modules field name the owning module first? For `contract` candidates: are all
modules party to the shared assumption named? For `strict` or `behavioral` candidates: are all
modules that meet either Rule 4 criterion — joint responsibility or direct integration — named?
→ YES → Step 3
→ NO → Add the missing module(s). A `contract` invariant without both parties named cannot be
evaluated for Rule 6 compliance. For non-`contract` invariants, apply the Rule 4 test to each
candidate module: joint responsibility (either party failing causes a violation) and direct
integration (the module's infrastructure is what the guarantee is expressed in terms of) both
warrant inclusion. Mere dependency on the guarantee does not — that is a `contract` relationship.

**Step 3 — Classify.** Apply gates in order. First match determines type.

Gate 6: Would a violation of this property cause data loss, corruption, or a crash?
→ YES → Type: `strict`. Proceed to Step 4.
→ NO → Gate 7

Gate 7: Does a violation break a shared assumption at a module boundary — a precondition,
postcondition, lifetime constraint, or ordering requirement that both sides depend on?
→ YES → Type: `contract`. Proceed to Step 4.
→ NO → Gate 8

Gate 8: Does a violation degrade the ambient environment — consuming a shared runtime resource,
or omitting a structural property, that harms modules with no declared relationship to this one —
while the violating module's functional output remains correct?
→ YES → Escalation check: Name all modules that experience this harm. For each: does it appear
  in the owning module's Contracts or Dependency Direction section?
  → Any do → NOT `resource`. The harm is directed at a module with a declared relationship.
    Return to Gate 7 — this is `contract`.
  → None do → Sub-step A: Is the harm structural — does it recur systematically as an inherent
    property of the module's implementation (per processed unit, per event, per iteration),
    rather than occurring as a single incidental instance?
    Pattern shapes: per-unit heap allocation, per-event spurious wakeup, per-iteration CPU spin
    without yield, shared lock held during IO. Any of these recurring inherently on every relevant
    operation is structural. The same behavior occurring once in isolation is not.
    → YES → Sub-step B: Does the Modules field name exactly one module (the owning module)?
      Rule 4 prohibits additional modules for `resource` invariants. If more than one module is
      named, remove the extras.
      → YES (single module confirmed) → Type: `resource`. Proceed to Step 4.
    → NO (Sub-step A) → Not `resource`. A single incidental occurrence is not an architectural
    guarantee. If the behavior degrades observable output, proceed to Gate 9. Otherwise revisit
    Gate 1 — it may not be an invariant at all.
→ NO → Gate 9

Gate 9: Does a violation degrade observable behavior while leaving the system operational?
→ YES → Type: `behavioral`. Proceed to Step 4.
→ NO → Cannot classify. Revisit Gate 1 — if no type fits, the behavior may be self-contained
after all, or the description may be naming an implementation detail rather than a guarantee.

**Step 4 — Enforce.**

Gate 10 [contract only]: Does at least one named module's Contracts section declare the interaction
this invariant describes?
→ YES → Gate 11
→ NO → Either update the module definition to declare the interaction, or re-examine whether this
invariant belongs at this boundary. An undeclared interaction is a gap in the module definition,
not just an invariant gap.

Gate 11: Is the invariant assigned a unique ID following the convention `[MODULE_PREFIX]-[NNN]`?
→ YES → Gate 12
→ NO → Assign a conforming ID. The ID is the machine-readable handle that connects the guarantee
to its tests and to the coverage enforcer.

Gate 12: Does at least one test exist, tagged with this invariant's ID, that fails when the
guarantee is violated?
→ YES → Gate 13
→ NO → Declared but not enforced. Write the test before considering this invariant active.

Gate 13: Is this invariant's ID tracked by the coverage test — the test that fails when any
declared invariant has no tagged test?
→ YES → Invariant is fully validated.
→ NO → Add the ID to the coverage test. An invariant not tracked by coverage can silently lose
its enforcement when a tagged test is deleted or renamed.

---

## 6. Examples

---

### Example A — Valid `strict` invariant

**ID:** `BUS-003`

**Type:** `strict`

**Modules:** Event Distribution

**Description:** The dropped event count never decreases.

**Validation — Step 1:**
- Gate 1: Reporting reads the dropped count from bus metrics. Sub-step: Reporting (discriminating
  value) — a decreasing count would mean previously-reported drops are silently reversed, corrupting
  the interval metrics it computes. ✓
- Gate 2: The dropped count is part of the bus's observable metrics — callers read it. ✓
- Gate 3: A test can increment the count by filling the bus, then verify the count only ever
  increases. Deterministic. ✓

**Validation — Step 2:**
- Gate 4: "Never decreases" — states a property, not a mechanism. ✓
- Gate 5: One module (Event Distribution owns it; no cross-module assumption is being made here). ✓

**Validation — Step 3:**
- Gate 6: A decreasing dropped count means a previous drop was either unrecorded or silently
  reversed — corrupted metrics. Type: `strict`. ✓

**Validation — Step 4:**
- Gate 10: N/A (not a contract invariant). ✓
- Gates 11–13: Assigned, tagged, tracked. ✓

---

### Example B — Valid `contract` invariant

**ID:** `BUS-006`

**Type:** `contract`

**Modules:** Event Distribution, Ingestion

**Description:** Publishers never block waiting for queue capacity.

**Validation — Step 1:**
- Gate 1: Ingestion depends on this guarantee. Sub-step: Ingestion (discriminating value) — it
  publishes from an OS callback thread that cannot block; a blocking Publish would stall OS event
  delivery and eventually drop events. ✓
- Gate 2: Whether `Publish` blocks is visible to Ingestion — it is the caller. ✓
- Gate 3: A test can fill the queue, then call `Publish` and verify it returns immediately. ✓

**Validation — Step 2:**
- Gate 4: States a behavioral guarantee (no blocking), not a mechanism. ✓
- Gate 5: Event Distribution (owner, named first) and Ingestion (the caller relying on it). Both
  named. ✓

**Validation — Step 3:**
- Gate 6: A blocking `Publish` would stall the OS event callback thread — potential deadlock and data
  loss. Type: `strict`? Let's apply in order: would it cause data corruption or a crash? A stall
  eventually causes dropped events due to OS buffer overflow, but the system does not immediately
  crash. → NO to Gate 6.
- Gate 7: Ingestion expects `Publish` to return immediately; Event Distribution's contract is
  non-blocking. Violation breaks a shared assumption. Type: `contract`. ✓

**Validation — Step 4:**
- Gate 10: Event Distribution's Contracts section declares `Publish(T item) → bool` as inbound
  behavioral; the non-blocking postcondition traces to this declaration. ✓
- Gates 11–13: Assigned, tagged, tracked. ✓

---

### Example C — Valid `behavioral` invariant

**ID:** `ING-003`

**Type:** `behavioral`

**Modules:** Ingestion

**Description:** Only `.log` and `.txt` files are marked processable. Other extensions are published
as non-processable events.

**Validation — Step 1:**
- Gate 1: Processing Coordination depends on this guarantee. Sub-step: Processing Coordination
  (discriminating value) — it branches on the Processable flag to decide whether to route an event
  for file reading or discard it as non-processable. ✓
- Gate 2: The `Processable` field on `FsEvent` is part of the published record — visible to any
  consumer. ✓
- Gate 3: A test can publish events with various extensions and verify the `Processable` field
  values. Deterministic. ✓

**Validation — Step 2:**
- Gate 4: States a filtering rule, not how it is implemented. ✓
- Gate 5: Ingestion owns the rule. ✓

**Validation — Step 3:**
- Gate 6: A misclassified extension does not cause data corruption — the file is either processed or
  skipped incorrectly, but no data is lost and the system does not crash. → NO.
- Gate 7: Is a shared assumption broken? Processing Coordination does not declare a contract based
  on the specific set of extensions — it trusts the `Processable` field, not the extension logic.
  → NO.
- Gate 8: Does a violation degrade the ambient environment? No — the module's only output is the
  `Processable` field on `FsEvent`. Incorrect classification harms its direct consumers (Processing
  Coordination), not the ambient environment. → NO.
- Gate 9: A violation degrades behavior — files that should be processed are skipped, or files that
  should not be processed are read. The system remains operational. Type: `behavioral`. ✓

---

### Example D — Invalid: self-contained behavior

**Candidate:** TopK sort order is stable when frequencies are equal.

**Problem:** The sort order of equal-frequency entries is determined entirely within the TopK
computation and is not relied upon by any downstream module. Reporting prints the entries in
whatever order they arrive — it does not branch based on tie-breaking order.

**Gate 1 failure:** No downstream module depends on this property being true. The guarantee is
self-contained within Statistics Collection. → Not an invariant. Leave the test untagged.

---

### Example E — Invalid: implementation detail

**Candidate:** The queue uses `Monitor.Wait` and `Monitor.Pulse` for thread synchronization.

**Problem:** The synchronization primitive is internal to Event Distribution. No other module
can observe whether `Monitor.Wait` or a different primitive is used — only the blocking and
non-blocking semantics are externally visible.

**Gate 2 failure:** The primitive is invisible to callers. → Not an invariant. If the non-blocking
guarantee matters, write it as a `contract` invariant on the `Publish` method's return behavior.

---

### Example F — Invalid: not deterministically testable

**Candidate:** Every byte appended to a watched file is eventually processed.

**Problem:** "Eventually" depends on OS event delivery, which is not deterministic. A test cannot
reliably fail when this guarantee is violated without introducing timing dependencies or mocks
that misrepresent OS behavior.

**Gate 3 failure:** Not deterministically testable as stated. → This is a `behavioral` claim worth
documenting, but it cannot be an enforced invariant unless restated in a testable form (e.g.,
scoped to "assuming events are not permanently suppressed by the OS" — which acknowledges the
non-determinism rather than ignoring it, and matches what a test can actually verify).

---

### Example G — Valid `resource` invariant

**ID:** `PROC-008`

**Type:** `resource`

**Modules:** Processing Coordination

**Description:** No heap objects are allocated per log line processed in the read-scan-parse cycle.

**Validation — Step 1:**
- Gate 1: GC pressure from per-line allocation harms all threads. Sub-step: shared runtime
  resource (GC heap) — all module threads are paused by collection; per-line allocation in the
  hot path creates sustained GC pressure that no single module absorbs alone. ✓
- Gate 2: The specific allocation act is invisible at the module boundary. The structural property —
  heap bytes allocated per processed line — is directly measurable with an allocation profiler
  (MemoryDiagnoser, AllocationMeasurer). Gate 2 passes on the structural property, not on
  downstream GC effects. ✓
- Gate 3: An allocation-tracking test (MemoryDiagnoser, or a custom AllocationMeasurer) can verify
  zero heap bytes allocated per processed line. Deterministic. ✓

**Validation — Step 2:**
- Gate 4: States a property (no allocation per line), not a mechanism. ✓
- Gate 5: Processing Coordination owns the obligation; no partner module — correct for `resource`
  type. ✓

**Validation — Step 3:**
- Gate 6: Per-line allocation does not cause data loss or a crash. (Resource exhaustion that
  eventually causes a crash is a separate `strict` invariant; this is about the consumption
  pattern.) → NO.
- Gate 7: Is a shared assumption at a boundary broken? No named module has a declared contract
  with Processing Coordination about its allocation behavior. → NO.
- Gate 8: Does a violation degrade the ambient environment? Yes — per-line allocation causes
  sustained GC pressure across all threads, harming every module that runs concurrently, without
  any declared relationship to Processing Coordination. The module's functional output (parsed
  records, updated statistics) is correct throughout. Type: `resource`. ✓

**Validation — Step 4:**
- Gate 10: N/A (not a contract invariant). ✓
- Gates 11–13: Assigned, tagged, tracked. ✓

---

### Example H — Valid `resource` invariant (CPU axis, omission form)

**ID:** `PROC-009`

**Type:** `resource`

**Modules:** Processing Coordination

**Description:** Workers yield to the OS scheduler between consecutive event dequeues during a
backlog drain.

**Validation — Step 1:**
- Gate 1: All module threads depend on fair scheduling. Sub-step: shared runtime resource (OS
  scheduler) — Reporting and Ingestion threads are starved of CPU time during backlog drains
  when no yield point exists between consecutive dequeues. ✓
- Gate 2: Whether a yield occurs is invisible at the dequeue boundary. The structural property —
  yield call count per dequeue iteration — is directly measurable by injecting a scheduling
  observer that counts calls per iteration. Gate 2 passes on the structural property, not on
  downstream scheduling latency. ✓
- Gate 3: A test can inject a scheduling observer at each dequeue iteration and verify the yield
  call count equals the event count processed. Deterministic. ✓

**Validation — Step 2:**
- Gate 4: States a structural requirement (yield at each dequeue), not a mechanism. Does not
  specify `Thread.Yield()`, `Task.Yield()`, or any particular primitive. ✓
- Gate 5: One module named. ✓

**Validation — Step 3:**
- Gate 6: A tight drain loop does not directly cause data loss or a crash — all events are
  processed correctly. → NO.
- Gate 7: No named module has declared a contract with Processing Coordination about CPU
  scheduling fairness during backlog drains. → NO.
- Gate 8: Omitting the yield point degrades the ambient scheduling environment — threads in
  modules with no declared relationship to Processing Coordination are starved of CPU while
  the coordinator's event output is functionally correct. This is a structural absence (what
  the module does not do), not an incorrect output. Type: `resource`. ✓

**Validation — Step 4:**
- Gate 10: N/A (not a contract invariant). ✓
- Gates 11–13: Assigned, tagged, tracked. ✓

---

### Example I — Valid `resource` invariant (scheduling axis, action form)

**ID:** `BUS-007`

**Type:** `resource`

**Modules:** Event Distribution

**Description:** Each `Publish` call wakes at most one blocked consumer.

**Validation — Step 1:**
- Gate 1: Processing Coordination worker threads depend on fair scheduling. Sub-step: shared
  runtime resource (OS scheduler / context switches) — N-1 futile wakeups per published event
  consume CPU cycles across all worker threads. ✓
- Gate 2: The wakeup count is not part of the `Publish` return value. The structural property —
  number of consumers woken per published event — is directly measurable with instrumented dequeue
  counters. Gate 2 passes on the structural property, not on downstream context-switch rate. ✓
- Gate 3: A test can configure N=3 consumers behind a barrier, publish 1 event, and use
  instrumented dequeue counters to verify exactly 1 consumer found work and the others
  returned to waiting without a dequeue. Deterministic with synchronization. ✓

**Validation — Step 2:**
- Gate 4: States a wakeup count guarantee, not a mechanism. Does not prescribe `Pulse` vs
  `PulseAll` or any other synchronization primitive. ✓
- Gate 5: One module named (Event Distribution owns the obligation). ✓

**Validation — Step 3:**
- Gate 6: Spurious wakeups do not cause data loss or a crash — events are delivered correctly
  to exactly one consumer. → NO.
- Gate 7: Processing Coordination does not declare a contract assumption about how Event
  Distribution signals consumers. Workers are written to handle spurious wakeups correctly;
  the harm is wasted CPU, not incorrect behavior. → NO.
- Gate 8: Each extra wakeup consumes a thread context switch and a futile queue check in a
  thread with no declared relationship to Event Distribution. The event is delivered correctly.
  The violation is a consumption pattern (one signal per event escalated to N signals), not an
  output error. Type: `resource`. ✓

**Validation — Step 4:**
- Gate 10: N/A (not a contract invariant). ✓
- Gates 11–13: Assigned, tagged, tracked. ✓

---

### Example J — Valid `resource` invariant (synchronization axis, lock scope)

**ID:** `FM-010`

**Type:** `resource`

**Modules:** File Management

**Description:** No blocking IO is performed while the file state registry lock is held.

**Validation — Step 1:**
- Gate 1: All file-state callers depend on the registry lock being available promptly. Sub-step:
  shared runtime resource (registry lock) — Processing Coordination, Scanning, and Tailing are
  blocked for the full IO duration when the lock is held during an IO call. ✓
- Gate 2: Whether the registry lock is held during IO is invisible at any module boundary.
  The structural property — whether a concurrent caller can acquire the lock while IO is in
  progress — is directly measurable with a concurrency test (mock IO to block, verify concurrent
  acquirer returns before IO completes). Gate 2 passes on the structural property, not on
  downstream lock-wait time. ✓
- Gate 3: A test can mock IO to block for a controlled duration, then verify a concurrent
  caller acquires the registry lock and returns before the IO mock releases. If the lock is
  held during IO, the concurrent caller must wait; if not, it completes immediately.
  Deterministic with a mock and a synchronization gate. ✓

**Validation — Step 2:**
- Gate 4: States a mutual-exclusion constraint (registry lock and blocking IO must not
  overlap), not a mechanism. Does not specify lock type or IO primitive. ✓
- Gate 5: One module named. ✓

**Validation — Step 3:**
- Gate 6: Holding the lock during IO causes contention and latency but not data loss or a
  crash. → NO.
- Gate 7: Processing Coordination and Scanning do not declare a contract assumption about
  the scope of File Management's internal locks. They depend on `GetOrCreate` completing,
  not on it completing within a specific lock scope. → NO.
- Gate 8: Holding the registry lock during IO degrades the ambient lock environment —
  every module that needs file state access is blocked for the IO duration regardless of
  whether it has any declared relationship to the blocking IO operation. File Management's
  output (the returned state object) is correct. The violation is a lock-scope pattern, not
  an output error. Type: `resource`. ✓

**Validation — Step 4:**
- Gate 10: N/A (not a contract invariant). ✓
- Gates 11–13: Assigned, tagged, tracked. ✓

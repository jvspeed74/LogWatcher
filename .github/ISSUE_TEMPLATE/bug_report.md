---
name: Bug Report
about: Report incorrect or unexpected behavior
labels: bug
---

## Summary

<!--
One sentence naming the broken behavior. This is a headline, not an explanation — details
go in Behavior below. Name the domain and the violated guarantee if you already know them.
Example: "File Tailing resets the read offset on rename events, causing lines to be
processed twice."
-->

## Behavior

<!--
Expected: State the behavior as it should work, ideally in terms of the domain's declared
Responsibility or a specific invariant from invariants.md. Anchor to the spec, not intuition.

Actual: Describe exactly what happens instead. Be specific — include observable symptoms,
not internal hypotheses. If you have a stack trace, error message, or log output, include it.
-->

**Expected:**

**Actual:**

## Steps to Reproduce

<!--
Minimal steps to observe the actual behavior. Include:
  - Relevant command-line arguments or configuration values
  - File structure or content that triggers the bug
  - Whether the bug is deterministic or timing-dependent
  - How frequently it reproduces (always, occasionally, only under load)
-->

1.
2.
3.

## Domain Diagnosis

<!--
Identify the domain whose Responsibility or In Scope is being violated. This is the domain
that *should* own correct behavior here — not necessarily the domain where the symptom first
appears, which may be a downstream consumer observing the consequence of an upstream failure.

If you traced the defect to a specific code location, name the namespace too, but lead with
the domain name. If multiple domains share blame, list the one whose guarantee is ultimately
broken as the primary row and note the others below it.
-->

Use names from [`docs/domain_boundaries.md`](../docs/domain_boundaries.md).

| Domain | Responsibility Violated |
|--------|------------------------|
|        |                        |

## Invariant Analysis

<!--
Which invariant from invariants.md does this bug violate? Use the exact invariant name or
description from that document. If no named invariant covers it, that may mean the guarantee
is implied but undocumented — note that here, as it may need to be added to invariants.md.

Types:
  strict       — Causes data loss, corruption, or a crash.
  behavioral   — Degrades observable behavior but the system keeps running.
  contract     — Breaks an assumption both sides of a domain boundary rely on.
  operational  — Only occurs under resource exhaustion or OS failure.

If the type is strict, note whether the bug has been observed in production.
-->

| Invariant | Type |
|-----------|------|
|           |      |

## Fix Scope

<!--
List every domain that must change to fix this bug. This often — but not always — matches
the domain in Domain Diagnosis. It may differ when:
  - The root cause is in a dependency that feeds the diagnosed domain
  - A contract between two domains is wrong and both sides must be corrected
  - A data structure's schema must be fixed at its owning domain, not at the consumer

Keep scope as narrow as possible. A bug fix must not expand a domain's In Scope unless
that expansion is the fix itself. If scope creep is tempting, open a separate feature issue.
-->

| Domain | Change Type | Description |
|--------|-------------|-------------|
|        |             |             |

**Change types:** `In Scope` · `Contract` · `Data Ownership` · `Dependency Direction`

> Any domain not listed here must not be modified to fix this issue. Update this table before touching any unlisted domain's namespace.

## Requirements

<!--
Express what correct behavior looks like using EARS syntax. These requirements describe
the fixed system — not the current broken behavior and not how to implement the fix, but
what "correct" means when the fix is complete.

  Event-driven: When <trigger>, the <domain> shall <response>.
  Unwanted:     If <unwanted condition>, then the <domain> shall <response>.
  State-driven: While <state>, the <domain> shall <response>.

If the fix is restoring a broken invariant, the requirement should restate that invariant
directly. Each requirement here must correspond to at least one acceptance criterion below.
-->

-

## Edge Cases

<!--
Related conditions that the same fix should address. Think through:
  - Other inputs or states that trigger the same root cause
  - Conditions where the fix itself could fail or produce incorrect behavior
  - Variants of the bug that may exist in sibling domains

If an edge case is out of scope for this fix, note it here and file a separate issue.
Do not leave related edge cases undocumented just because they are deferred.
-->

-

## Acceptance Criteria

<!--
Each criterion must be independently verifiable. At minimum, one criterion should directly
confirm the invariant identified in Invariant Analysis is restored. Do not restate the
implementation ("the offset is no longer reset") — state the observable outcome
("lines are processed exactly once per file modification event").
-->

- [ ]
- [ ]

## Test Plan

<!--
List the tests that will verify the fix. For each test note:
  - Which invariant it verifies
  - Which domain(s) it exercises
  - Whether it is a regression test for this specific bug — mark these clearly so they
    are never removed

Regression tests should reproduce the exact conditions of the bug before asserting the fix.
If the bug was timing-dependent, note how the test makes the race deterministic.
-->

-

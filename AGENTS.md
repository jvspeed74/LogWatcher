# AGENTS.md

## Purpose

This repository contains a .NET/C# application. Automated agents must make changes that are correct, minimal, testable,
and aligned with repository documentation.

## Sources of Truth

- Project requirements and contracts are defined in `docs/`.
- If agent assumptions conflict with `docs/`, the documentation is correct.
- Do not invent requirements. When uncertain, make the smallest reasonable assumption and explain it in the PR.

## Working Guidelines

- Prefer the smallest change that satisfies the task while maintaining quality and correctness.
- Avoid speculative features or refactors unless explicitly requested.
- Treat configuration as authoritative; avoid hardcoding environment-specific values.
- Do not assume administrator privileges or machine-wide access.

## Change Boundaries

- No external services, telemetry, or network dependencies unless explicitly required by documented behavior.
- Do not introduce privileged operations (services, machine-level installs, event logs) without explicit direction.
- Do not break published data/CLI contracts without a compatibility plan (migration or backward compatibility).

## Pull Request Expectations

Every PR must include:

- Summary of what changed and why (tie back to docs or issue).
- Verification steps (commands run and results).
- Notes on risks, edge cases, and any follow-up work (if applicable).
- Tests added/updated when behavior changes.

## Documentation Expectations

- Update docs only when user-facing behavior, contracts, or configuration rules change.

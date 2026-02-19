# Domain Boundaries Reference

This document defines the architectural domains of LogWatcher and their responsibilities. Use this as a reference when
modifying code, adding features, or debugging issues.

---

## **Quick Reference: 12 Domains → 7 Namespaces**

| #  | Domain                  | Namespace                             | Responsibility                               | Reason to Change                   |
|----|-------------------------|---------------------------------------|----------------------------------------------|------------------------------------|
| 1  | Ingestion               | `LogWatcher.Core.Ingestion`           | OS events → FsEvent                          | FileSystemWatcher behavior changes |
| 2  | Event Distribution      | `LogWatcher.Core.Events`              | Bounded async queue                          | Queue semantics change             |
| 3  | File State Management   | `LogWatcher.Core.FileManagement`      | Per-file state + offset + lock               | File state contract changes        |
| 4  | File Tailing            | `LogWatcher.Core.Processing.Tailing`  | Incremental file reads                       | File IO pattern changes            |
| 5  | Line Scanning           | `LogWatcher.Core.Processing.Scanning` | Chunked bytes → lines                        | Line delimiter changes             |
| 6  | Log Parsing             | `LogWatcher.Core.Processing.Parsing`  | Bytes → structured records                   | Log format changes                 |
| 7  | File Processing         | `LogWatcher.Core.Processing`          | Orchestrate tailer+scanner+parser            | Processing pipeline changes        |
| 8  | Processing Coordination | `LogWatcher.Core.Processing`          | Route events, enforce per-file serialization | Worker/routing policy changes      |
| 9  | Statistics Collection   | `LogWatcher.Core.Statistics`          | Per-worker metrics accumulation              | Metrics contract changes           |
| 10 | Worker Coordination     | `LogWatcher.Core.Coordination`        | Double-buffer swap protocol                  | Synchronization strategy changes   |
| 11 | Reporting               | `LogWatcher.Core.Reporting`           | Merge stats, print reports                   | Reporting policy changes           |
| 12 | CLI & Host              | `LogWatcher.App`                      | Argument parsing, dependency injection       | Deployment contract changes        |

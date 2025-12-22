# Project Specification (Non-Technical Overview)

## Local Log Monitoring & Statistics Tool

### Status

Proposed / Ready for development

### Audience

* Product managers
* Engineering managers
* Technical program managers
* Stakeholders evaluating scope, risk, and value

---

## 1. Purpose

Modern applications write large volumes of log data to local files. Understanding what those logs contain—how fast
events occur, what messages dominate, and how long operations take—often requires heavyweight tooling or external
services.

This project provides a **lightweight, self-contained tool** that:

* Watches a local folder for log changes
* Reads new log entries as they are written
* Computes rolling statistics in real time
* Reports those statistics to the console every few seconds

It runs as a **single application on one machine** and does not rely on external services.

---

## 2. What the System Does

### In Plain Terms

* Watches one folder on a machine for log file changes
* Reads new log entries as files grow
* Counts how many log lines appear
* Identifies the most common types of messages
* Tracks how long operations take (when latency is logged)
* Prints a summary report every few seconds

---

## 3. Key Capabilities

### 3.1 Live Log Monitoring

* Automatically reacts when log files are created, changed, renamed, or deleted
* Only looks at `.log` and `.txt` files
* Does not scan subfolders

### 3.2 Incremental Processing

* Reads **only new content** added to a log file
* Avoids reprocessing old data
* Resets safely if files are deleted or recreated

### 3.3 Parallel Processing

* Multiple files can be processed at the same time
* Each individual file is handled safely by one worker at a time
* Prevents conflicts or duplicated processing

### 3.4 Rolling Statistics

Every reporting interval (default: 2 seconds), the system prints:

* Number of log lines processed per second
* Number of malformed or unreadable lines
* Most frequent message types (“Top K”)
* Latency statistics:

    * Median
    * 95th percentile
    * 99th percentile
* Internal health metrics (queue pressure, dropped events)
* Memory activity (for diagnostics)

---

## 4. What the System Does *Not* Do

To keep scope focused and risk low, the system intentionally does **not**:

* Send data to external services
* Store results permanently
* Provide dashboards or APIs
* Guarantee that *every* filesystem change is captured
* Coordinate across multiple machines

---

## 5. Performance & Reliability Characteristics

### Designed For

* High log throughput
* Long-running operation
* Graceful handling of overload
* Predictable memory usage

### Behavior Under Load

If logs change faster than the system can process:

* The system **drops excess events**
* Dropped events are counted and reported
* The system remains responsive instead of crashing

---

## 6. Reporting Model

### Reporting Interval

* Reports are generated approximately every 2 seconds
* If the system is briefly delayed (e.g., due to system pauses), reports adjust automatically
* Rates are always calculated using actual elapsed time

### Transparency

* All delays, drops, and errors are visible in reports
* No silent data loss

---

## 7. Configuration Options

The following can be adjusted when starting the tool:

* Folder to watch
* Number of worker threads
* Maximum internal queue size
* Reporting interval
* Number of top messages to show

---

## 8. Risks and Mitigations

### Risk 1: Missed Filesystem Events

**Description:**
Operating systems do not guarantee delivery of every file change notification, especially under heavy load.

**Impact:**
Some changes may not trigger immediate processing.

**Mitigation:**

* The system relies on file content (not events alone)
* If events are missed but files grow, data is still read on the next event
* Dropped events are tracked and reported

---

### Risk 2: High Log Volume Overwhelms the System

**Description:**
Very high log write rates may exceed processing capacity.

**Impact:**
Some events may be skipped.

**Mitigation:**

* Bounded internal queue prevents memory exhaustion
* Drop-on-overload behavior keeps the system alive
* Drop counts are visible in reports
* Core guarantee: **eventual processing of file content**, not events

---

### Risk 3: Files Deleted or Renamed While Being Read

**Description:**
Log files may be deleted or renamed while processing is in progress.

**Impact:**
Could lead to read errors or partial reads.

**Mitigation:**

* File operations are treated as expected race conditions
* The system handles these safely without crashing
* Internal state is cleaned up automatically

---

### Risk 4: System Pauses (Garbage Collection)

**Description:**
The runtime may occasionally pause execution to manage memory.

**Impact:**
Temporary slowdowns in processing and reporting.

**Mitigation:**

* Reporting adjusts to actual elapsed time
* Processing catches up after pauses
* No corruption or data inconsistency occurs

---

### Risk 5: Memory Growth Over Time

**Description:**
Unbounded memory growth could degrade performance.

**Impact:**
Long-running instability.

**Mitigation:**

* All queues and buffers are size-limited
* Deleted files are removed from tracking
* Memory usage is observable via reports

---

## 9. Success Criteria

The project is considered successful if:

* It runs continuously without crashing
* It processes new log data incrementally
* It provides meaningful real-time statistics
* It behaves predictably under heavy load
* It clearly reports when it is overloaded or delayed

---

## 10. Summary

This project delivers a **focused, reliable, and transparent log monitoring tool** designed for:

* Local diagnostics
* Performance investigations
* Development and testing environments
* Systems learning and experimentation

It prioritizes **predictable behavior, visibility, and safety** over complexity, making it suitable for both operational
use and technical exploration.

---

If you’d like, I can next:

* Create a **one-page executive summary**
* Produce a **risk vs. value matrix**
* Write a **README suitable for open-source release**
* Translate this into a **project proposal or pitch deck**

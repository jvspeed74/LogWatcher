# Migration Plan: System.CommandLine 3.0.0-preview

## Overview
This document outlines the complete migration plan from the custom `CliParser` to Microsoft's `System.CommandLine` library (version 3.0.0-preview), which is compatible with .NET 10. **This migration is designed with SOLID principles as the primary architectural driver.**

---

## Design Principles & Goals

This migration addresses key issues in the current implementation while avoiding overengineering:

### Current Issues ❌

**Code Organization:**
- ❌ 171-line `CliParser.TryParse()` method (God Method)
- ❌ `Program.cs` has `Environment.Exit(2)` bypassing finally block - **CRITICAL BUG**
- ❌ Hard-coded help text must be manually kept in sync
- ❌ Duplicate parsing logic for each option

**Testability:**
- ❌ Cannot test CLI parsing without process exit
- ❌ Cannot test application startup in isolation
- ❌ No way to mock dependencies

**Maintainability:**
- ❌ Adding options requires modifying switch statement in multiple places
- ❌ Error messages are inconsistent and unhelpful

### Target State ✅

**Simplified Architecture:**
- ✅ Use `System.CommandLine` (battle-tested library)
- ✅ Single static class for CLI setup
- ✅ Extract application logic from `Program.cs` to testable method
- ✅ Auto-generated help text
- ✅ Fix `Environment.Exit(2)` bug

**Key Principles (Pragmatic, Not Dogmatic):**
- ✅ **Separation of Concerns**: CLI parsing separate from application logic
- ✅ **Don't Repeat Yourself**: System.CommandLine handles repetitive parsing
- ✅ **Testability**: Extract logic from Program.cs so it can be tested
- ✅ **Simplicity**: Use library features, don't reinvent validation framework

### Benefits Summary

| Aspect | Before | After | Improvement |
|--------|--------|-------|-------------|
| **Lines of Code** | 171 (CliParser) | ~80 (CliConfiguration) | 50% reduction |
| **Testability** | Cannot test (Environment.Exit) | Fully testable | ✅ Critical fix |
| **Help Text** | Hard-coded string | Auto-generated | ✅ Maintenance free |
| **Adding Options** | Modify switch + help text | Add option definition | ✅ Simpler |
| **Error Messages** | Generic | Descriptive | ✅ Better UX |
| **Critical Bug** | Environment.Exit(2) bypasses finally | Fixed | ✅ Resource cleanup works |

---

## Executive Summary

**Current State:**
- Custom `CliParser` class (171 lines) with manual argument parsing
- `Program.cs` with **CRITICAL BUG**: `Environment.Exit(2)` bypasses finally block
- Hard-coded help text
- Limited test coverage
- Difficult to extend

**Target State:**
- `System.CommandLine` 3.0.0-preview for CLI parsing
- Auto-generated help text with descriptions
- **Fix critical Environment.Exit bug**
- Extract application logic for testability
- Professional CLI experience

**Key Benefits:**
1. **Fix Critical Bug:** `Environment.Exit(2)` currently bypasses cleanup code
2. **Reduce Custom Code:** 171 lines → ~80 lines (57% reduction)
3. **Auto-Generated Help:** No manual maintenance
4. **Better Errors:** System.CommandLine provides clear, actionable messages
5. **Testability:** Can test CLI and application separately
6. **Industry Standard:** Battle-tested library used by Microsoft

**Effort:** 2-4 hours

**Risk:** Low - System.CommandLine is well-tested, preview version stable for .NET 10

---

## Phase 1: Preparation & Research (30 minutes)

### 1.1 Verify .NET 10 Compatibility
- ✅ Project currently targets `net10.0`
- ✅ `System.CommandLine` 3.0.0-preview versions support .NET 10
- Check latest 3.0.0-preview version available

### 1.2 Review System.CommandLine 3.0.0 API Changes
Key changes from 2.x to 3.0.0-preview:
- New `CliCommand`, `CliOption`, `CliArgument` classes (replaces `Command`, `Option`, `Argument`)
- New `CliConfiguration` for configuration
- Simplified handler registration with `SetAction` instead of `SetHandler`
- Better async support
- Improved model binding

### 1.3 Identify Dependencies
Files requiring changes:
- `LogWatcher.App.csproj` - Add package reference
- `CliParser.cs` - Replace with System.CommandLine code
- `CliConfig.cs` - May need minor adjustments
- `Program.cs` - Refactor startup logic
- `CliParserTests.cs` - Update tests for new API

---

## Phase 2: Install Dependencies (15 minutes)

### 2.1 Add NuGet Package
```xml
<PackageReference Include="System.CommandLine" Version="3.0.0-preview.1.24312.3" />
```
Note: Use the latest 3.0.0-preview version available. As of preview knowledge, the latest stable preview is around `3.0.0-preview.1.24312.3`.

### 2.2 Verify Installation
- Build project to ensure package restore works
- Check for any compatibility warnings
- Review package dependencies

---

## Phase 3: Design New CLI Structure (30 minutes)

### 3.1 Define Root Command Structure

**Root Command:** `logwatcher`
- Description: "High-performance log file watcher with real-time statistics"

**Arguments:**
- `watchPath` (required)
  - Description: "Directory path to watch for log file changes"
  - Validation: Must be an existing directory

**Options:**
- `--workers` / `-w` (optional, default: `Environment.ProcessorCount`)
  - Description: "Number of worker threads for processing log files"
  - Validation: Must be >= 1
  
- `--queue-capacity` / `-q` (optional, default: 10000)
  - Description: "Maximum capacity of the filesystem event queue"
  - Validation: Must be >= 1
  
- `--report-interval-seconds` / `-r` (optional, default: 2)
  - Description: "Interval in seconds between statistics reports"
  - Validation: Must be >= 1
  
- `--topk` / `-k` (optional, default: 10)
  - Description: "Number of top URLs to include in reports"
  - Validation: Must be >= 1

### 3.2 Design Approach (Simplified)

**Principle: Use the library's features, don't reinvent the wheel**

System.CommandLine already provides:
- ✅ Argument/option parsing
- ✅ Type conversion and validation
- ✅ Help text generation
- ✅ Error messaging

**Our code should:**
- Define CLI structure (arguments, options, defaults)
- Use built-in validators where possible
- Extract application startup logic to testable method
- Wire everything together in Program.cs

**What we DON'T need:**
- ❌ Custom validator framework (`ICliValidator<T>`)
- ❌ Multiple abstraction layers (`CliHost`, `CommandHandler`)
- ❌ Dependency injection containers
- ❌ Interface for every class

**Simple Architecture:**
```
Program.cs (15 lines)
    ↓ calls
CommandConfiguration.CreateRootCommand() (static method)
    ↓ returns
CliRootCommand (System.CommandLine)
    ↓ on parse success, action handler calls
ApplicationHost.Run(CliConfig) (static method)
    ↓ returns
Exit code
```

**Benefits:**
- **3 focused classes** with clear responsibilities
- **CommandConfiguration:** Knows about CLI structure only
- **ApplicationHost:** Knows about application execution only
- **Program.cs:** Wires them together
- Each testable independently
- No over-abstraction

### 3.3 Balanced Class Design

**Three focused classes (appropriate separation of concerns):**

1. **CommandConfiguration** (static class, ~60 lines)
   - `CreateRootCommand()` - Builds System.CommandLine structure
   - Defines arguments, options, validators, and defaults
   - Returns configured `CliRootCommand`

2. **ApplicationHost** (static class, ~60 lines)
   - `Run(CliConfig)` - Executes application logic
   - Contains startup/shutdown sequence (extracted from Program.cs)
   - Testable in isolation

3. **Program.cs** (~15 lines)
   - Wires CommandConfiguration and ApplicationHost
   - Invokes command and returns exit code

**Why 3 classes instead of 1?**
- ✅ **Separation of Concerns:** CLI structure separate from application execution
- ✅ **Testability:** Can test command building and app execution independently
- ✅ **Single Responsibility:** Each class has one clear purpose
- ✅ **Not Overengineered:** No interfaces, no DI, just logical separation

**Why not 8 classes?**
- ❌ System.CommandLine already provides validation (no custom validator framework needed)
- ❌ Only one implementation (no need for interfaces)
- ❌ No complex orchestration needed (no need for Host/Handler/Builder pattern)

### 3.1 Define Root Command Structure

**Root Command:** `logwatcher`
- Description: "High-performance log file watcher with real-time statistics"

**Arguments:**
- `watchPath` (required)
  - Description: "Directory path to watch for log file changes"
  - Validation: Must be an existing directory

**Options:**
- `--workers` / `-w` (optional, default: `Environment.ProcessorCount`)
  - Description: "Number of worker threads for processing log files"
  - Validation: Must be >= 1
  
- `--queue-capacity` / `-q` (optional, default: 10000)
  - Description: "Maximum capacity of the filesystem event queue"
  - Validation: Must be >= 1
  
- `--report-interval-seconds` / `-r` (optional, default: 2)
  - Description: "Interval in seconds between statistics reports"
  - Validation: Must be >= 1
  
- `--topk` / `-k` (optional, default: 10)
  - Description: "Number of top URLs to include in reports"
  - Validation: Must be >= 1

### 3.4 Program.cs Refactoring Strategy

**Current Problem:**
```csharp
if (!CliParser.TryParse(args, out var config, out var parseError))
{
    if (parseError == "help") {
        Console.WriteLine("Usage: ...");
        return;  // No exit code set
    }
    Console.Error.WriteLine("Invalid arguments: " + parseError);
    Environment.Exit(2);  // ❌ BYPASSES FINALLY BLOCK!
}
// ... 60+ lines of application logic
```

**Simple Solution:**
```csharp
// Program.cs becomes minimal
var command = CliConfiguration.CreateRootCommand();
var configuration = new CliConfiguration(command);
return configuration.Invoke(args);  // Returns exit code properly
```

---

## Phase 4: Implementation (2-3 hours)

### 4.1 Create CommandConfiguration Class

**File:** `LogWatcher.App/CommandConfiguration.cs` (new file, ~60 lines)

Responsible for building the CLI command structure only.

**Structure:**
```csharp
namespace LogWatcher.App;

/// <summary>
/// Builds the CLI command structure using System.CommandLine.
/// </summary>
public static class CommandConfiguration
{
    public static CliRootCommand CreateRootCommand()
    {
        // Define watchPath argument
        var watchPathArg = new CliArgument<string>("watchPath")
        {
            Description = "Directory path to watch for log file changes"
        };
        
        // Add validation inline
        watchPathArg.Validators.Add(result =>
        {
            var path = result.GetValue(watchPathArg);
            if (string.IsNullOrWhiteSpace(path))
                result.AddError("watchPath cannot be empty");
            else if (!Directory.Exists(path))
                result.AddError($"watchPath does not exist: {path}");
        });
        
        // Define options with defaults
        var workersOpt = new CliOption<int>("--workers", "-w")
        {
            Description = "Number of worker threads",
            DefaultValueFactory = _ => Environment.ProcessorCount
        };
        workersOpt.Validators.Add(result =>
        {
            if (result.GetValue(workersOpt) < 1)
                result.AddError("--workers must be at least 1");
        });
        
        var queueCapacityOpt = new CliOption<int>("--queue-capacity", "-q")
        {
            Description = "Maximum capacity of the filesystem event queue",
            DefaultValueFactory = _ => 10000
        };
        queueCapacityOpt.Validators.Add(result =>
        {
            if (result.GetValue(queueCapacityOpt) < 1)
                result.AddError("--queue-capacity must be at least 1");
        });
        
        var reportIntervalOpt = new CliOption<int>("--report-interval-seconds", "-r")
        {
            Description = "Interval in seconds between statistics reports",
            DefaultValueFactory = _ => 2
        };
        reportIntervalOpt.Validators.Add(result =>
        {
            if (result.GetValue(reportIntervalOpt) < 1)
                result.AddError("--report-interval-seconds must be at least 1");
        });
        
        var topKOpt = new CliOption<int>("--topk", "-k")
        {
            Description = "Number of top URLs to include in reports",
            DefaultValueFactory = _ => 10
        };
        topKOpt.Validators.Add(result =>
        {
            if (result.GetValue(topKOpt) < 1)
                result.AddError("--topk must be at least 1");
        });
        
        // Build root command
        var rootCommand = new CliRootCommand(
            "High-performance log file watcher with real-time statistics")
        {
            watchPathArg,
            workersOpt,
            queueCapacityOpt,
            reportIntervalOpt,
            topKOpt
        };
        
        // Register action handler
        rootCommand.SetAction(parseResult =>
        {
            // Extract values
            var watchPath = parseResult.GetValue(watchPathArg);
            var workers = parseResult.GetValue(workersOpt);
            var queueCapacity = parseResult.GetValue(queueCapacityOpt);
            var reportInterval = parseResult.GetValue(reportIntervalOpt);
            var topK = parseResult.GetValue(topKOpt);
            
            // Create config (CliConfig validation runs here)
            CliConfig config;
            try
            {
                config = new CliConfig(watchPath!, workers, queueCapacity, 
                    reportInterval, topK);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return 2;
            }
            
            // Delegate to ApplicationHost
            return ApplicationHost.Run(config);
        });
        
        return rootCommand;
    }
}
```

**Key Points:**
- ✅ Only responsible for CLI structure
- ✅ Uses System.CommandLine's built-in validation (inline lambdas)
- ✅ Delegates application execution to ApplicationHost
- ✅ ~60 lines, focused and clear

### 4.2 Create ApplicationHost Class

**File:** `LogWatcher.App/ApplicationHost.cs` (new file, ~60 lines)

Responsible for running the application with validated configuration.

**Structure:**
```csharp
namespace LogWatcher.App;

/// <summary>
/// Hosts and executes the LogWatcher application with validated configuration.
/// </summary>
public static class ApplicationHost
{
    public static int Run(CliConfig config)
    {
        // All application logic from current Program.cs lines 26-80
        BoundedEventBus<FsEvent>? bus = null;
        FileStateRegistry? registry = null;
        FileTailer? tailer = null;
        FileProcessor? processor = null;
        WorkerStats[]? workerStats = null;
        ProcessingCoordinator? coordinator = null;
        Reporter? reporter = null;
        FilesystemWatcherAdapter? watcher = null;

        try
        {
            // Construct components
            bus = new BoundedEventBus<FsEvent>(config.QueueCapacity);
            registry = new FileStateRegistry();
            tailer = new FileTailer();
            processor = new FileProcessor(tailer);

            workerStats = new WorkerStats[config.Workers];
            for (int i = 0; i < workerStats.Length; i++)
            {
                workerStats[i] = new WorkerStats();
            }

            coordinator = new ProcessingCoordinator(bus, registry, processor, workerStats, 
                workerCount: config.Workers);
            reporter = new Reporter(workerStats, bus, config.TopK, config.ReportIntervalSeconds);
            watcher = new FilesystemWatcherAdapter(config.WatchPath, bus);

            // Register shutdown handlers
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                HostWiring.TriggerShutdown(bus, watcher, coordinator, reporter);
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => 
                HostWiring.TriggerShutdown(bus, watcher, coordinator, reporter);

            // Start components in order
            coordinator.Start();
            reporter.Start();
            watcher.Start();

            Console.WriteLine("Started: " + config);

            // Wait until shutdown is requested
            HostWiring.WaitForShutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex}");
            return 1;
        }
        finally
        {
            // Ensure final cleanup
            HostWiring.TriggerShutdown(bus, watcher, coordinator, reporter);
        }
    }
}
```

**Key Points:**
- ✅ Only responsible for application execution
- ✅ Extracted from Program.cs for testability
- ✅ No knowledge of CLI parsing
- ✅ ~60 lines, focused and clear

### 4.3 Simplify Program.cs

**File:** `LogWatcher.App/Program.cs` (modify, reduce to ~10 lines)

**Before (83 lines):**
```csharp
using LogWatcher.App;
// ... many usings

int exitCode;

if (!CliParser.TryParse(args, out var config, out var parseError))
{
    if (parseError == "help")
    {
        Console.WriteLine("Usage: ...");
        return;
    }
    Console.Error.WriteLine("Invalid arguments: " + parseError);
    Environment.Exit(2);  // ❌ BUG!
}

BoundedEventBus<FsEvent>? bus = null;
// ... 60+ lines of application logic
```

**After (10 lines):**
```csharp
using LogWatcher.App;
using System.CommandLine;

var command = CommandConfiguration.CreateRootCommand();
var configuration = new CliConfiguration(command);
var exitCode = configuration.Invoke(args);

Environment.Exit(exitCode);  // ✅ Single exit point, after all cleanup
```

**Benefits:**
- ✅ No more `Environment.Exit(2)` in the middle
- ✅ Clean separation: CommandConfiguration (CLI) + ApplicationHost (app logic)
- ✅ Finally blocks execute properly
- ✅ Easy to test both classes independently

### 4.4 Keep CliConfig Unchanged

`CliConfig.cs` already does validation correctly. No changes needed.

### 4.4 Delete CliParser.cs

Once implementation is complete:
- Delete `LogWatcher.App/CliParser.cs` (171 lines eliminated!)

---

## Phase 5: Update Tests (1 hour)

### 5.1 Update CliParserTests.cs

**Rename:** `CliParserTests.cs` → `CliConfigurationTests.cs`

**Testing Strategy:**
- Test CLI parsing using System.CommandLine's Parse method
- Test application logic by calling `CliConfiguration.RunApplication()` directly
- Keep tests simple and focused

**Example Tests:**

```csharp
public class CliConfigurationTests : IDisposable
{
    private readonly string _tmpDir;

    public CliConfigurationTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "logwatcher_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, true); } catch { }
    }

    [Fact]
    public void Parse_DefaultsAndPositional_Works()
    {
        var args = new[] { _tmpDir };
        var command = CliConfiguration.CreateRootCommand();
        var config = new CliConfiguration(command);
        
        var result = command.Parse(args);
        
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_AllOptions_Works()
    {
        var args = new[] { _tmpDir, "--workers", "3", "-q", "500", "-r", "5", "-k", "7" };
        var command = CliConfiguration.CreateRootCommand();
        var result = command.Parse(args);
        
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_MissingPath_Fails()
    {
        var args = Array.Empty<string>();
        var command = CliConfiguration.CreateRootCommand();
        var result = command.Parse(args);
        
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_InvalidNumber_Fails()
    {
        var args = new[] { _tmpDir, "--workers", "notanumber" };
        var command = CliConfiguration.CreateRootCommand();
        var result = command.Parse(args);
        
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_HelpRequested_ShowsHelp()
    {
        var args = new[] { "--help" };
        var command = CliConfiguration.CreateRootCommand();
        var result = command.Parse(args);
        
        // System.CommandLine handles help automatically
        Assert.True(result.Action == null || result.Errors.Any());
    }
}
```

**What Changed:**
- Tests now use `command.Parse()` instead of `CliParser.TryParse()`
- Simpler assertions (check for errors vs checking out parameters)
- Can test help functionality without special "help" string sentinel

**Note:** Testing `RunApplication()` requires more setup (mocking infrastructure components), so integration tests are recommended for that path.

---

## Phase 6: Enhanced Error Handling (30 minutes)

### 6.1 Leverage System.CommandLine Validators

System.CommandLine validators automatically provide good error messages. Our inline validators already provide context:

```csharp
workersOpt.Validators.Add(result =>
{
    var value = result.GetValue(workersOpt);
    if (value < 1)
        result.AddError($"--workers must be at least 1, got {value}");
});
```

### 6.2 Exit Code Strategy

Consistent exit codes:
- `0` - Success
- `1` - Runtime error (exception during execution)
- `2` - Configuration error (invalid CLI arguments or CliConfig validation)

System.CommandLine returns `1` for parse errors by default, which we override with `2` for configuration errors.

---

## Phase 7: Documentation & Help Text (15 minutes)

### 7.1 Verify Help Text Quality

Run and verify:
```bash
dotnet run -- --help
```

Expected output (auto-generated):
```
Description:
  High-performance log file watcher with real-time statistics

Usage:
  logwatcher <watchPath> [options]

Arguments:
  <watchPath>  Directory path to watch for log file changes

Options:
  -w, --workers <workers>                                     Number of worker threads [default: <CPU count>]
  -q, --queue-capacity <queue-capacity>                       Maximum capacity of the filesystem event queue [default: 10000]
  -r, --report-interval-seconds <report-interval-seconds>     Interval in seconds between statistics reports [default: 2]
  -k, --topk <topk>                                           Number of top URLs to include in reports [default: 10]
  -?, -h, --help                                              Show help and usage information
```

### 7.2 Update Documentation

Update any documentation referencing CLI usage with the new help output.

---

## Phase 8: Testing & Validation (30 minutes)

### 8.1 Manual Testing Checklist

- [ ] Run with minimal arguments: `dotnet run -- /path/to/logs`
- [ ] Run with all options: `dotnet run -- /path/to/logs -w 4 -q 5000 -r 5 -k 20`
- [ ] Test `--help` output
- [ ] Test invalid path: `dotnet run -- /nonexistent`
- [ ] Test invalid option value: `dotnet run -- /tmp --workers abc`
- [ ] Test invalid range: `dotnet run -- /tmp --workers 0`
- [ ] Test unknown option: `dotnet run -- /tmp --unknown-option`
- [ ] Verify Ctrl+C shutdown still works
- [ ] Verify finally block executes (no `Environment.Exit(2)` bypass)

### 8.2 Automated Test Execution

```bash
dotnet test --filter "FullyQualifiedName~CliConfiguration"
```

Verify all tests pass.

---

## Phase 9: Cleanup & Final Review (15 minutes)

### 9.1 Code Cleanup
- [ ] Delete `CliParser.cs`
- [ ] Remove unused usings
- [ ] Run code formatter
- [ ] Update XML documentation comments

### 9.2 Review Checklist
- [ ] No `Environment.Exit()` calls in mid-execution
- [ ] Exit codes are consistent
- [ ] Help text is comprehensive
- [ ] All tests pass
- [ ] Error messages are clear
- [ ] Documentation is updated

### 9.3 Git Commit Strategy

Recommended commits:
1. `feat: add System.CommandLine package`
2. `refactor: create CliConfiguration with System.CommandLine`
3. `refactor: simplify Program.cs and fix Environment.Exit bug`
4. `test: update tests for CliConfiguration`
5. `refactor: remove CliParser class`
6. `docs: update CLI usage documentation`

---

## Phase 10: Optional Enhancements (Future)

### 10.1 Tab Completion Support

System.CommandLine supports shell completion. This can be added later without modifying existing code.

### 10.2 Subcommands (If Needed)

If future requirements need subcommands (e.g., `logwatcher watch`, `logwatcher analyze`):
```csharp
var watchCommand = new CliCommand("watch", "Watch a directory for changes");
var analyzeCommand = new CliCommand("analyze", "Analyze existing log files");
rootCommand.Subcommands.Add(watchCommand);
rootCommand.Subcommands.Add(analyzeCommand);
```

### 10.3 Configuration File Support

Combine CLI args with config file (future enhancement if needed).

### 10.4 Environment Variable Support

Add environment variable defaults:
```csharp
DefaultValueFactory = _ => 
{
    var envValue = Environment.GetEnvironmentVariable("LOGWATCHER_WORKERS");
    return envValue != null ? int.Parse(envValue) : Environment.ProcessorCount;
}
```

---

## Risk Assessment & Mitigation

### Risk 1: Breaking Changes in Preview Version
**Impact:** Medium  
**Probability:** Low  
**Mitigation:**
- Test thoroughly before production
- Pin exact preview version in csproj
- Preview versions are stable for .NET 10

### Risk 2: Learning Curve
**Impact:** Low  
**Probability:** Low  
**Mitigation:**
- System.CommandLine is intuitive
- Good documentation available
- Simple implementation (1 static class)

---

## Success Criteria

✅ **Functionality:**
- [ ] All existing CLI scenarios work identically
- [ ] Help text is auto-generated and comprehensive
- [ ] Error messages are clear

✅ **Code Quality:**
- [ ] CliParser.cs removed (~171 lines deleted)
- [ ] No `Environment.Exit(2)` mid-execution
- [ ] Program.cs simplified to ~10 lines
- [ ] Code is maintainable

✅ **Testing:**
- [ ] All unit tests pass
- [ ] Manual testing checklist complete

✅ **Documentation:**
- [ ] README updated with new CLI usage
- [ ] Help text reviewed

---

## Implementation Files Summary

### Files to Create:

1. **`LogWatcher.App/CommandConfiguration.cs`** (~60 lines)
   - Static class with `CreateRootCommand()` method
   - Defines CLI structure (arguments, options, validators, defaults)
   - Uses System.CommandLine's built-in validation

2. **`LogWatcher.App/ApplicationHost.cs`** (~60 lines)
   - Static class with `Run(CliConfig)` method
   - Contains application startup/shutdown logic
   - Extracted from Program.cs for testability

### Files to Modify:

1. **`LogWatcher.App/LogWatcher.App.csproj`** (+1 line)
   - Add: `<PackageReference Include="System.CommandLine" Version="3.0.0-preview.1.24312.3" />`

2. **`LogWatcher.App/Program.cs`** (Reduce from 83 to ~10 lines)
   - Simplify to: Create command, invoke, return exit code
   - **Fixes critical Environment.Exit(2) bug**

3. **`LogWatcher.App/CliConfig.cs`** (No changes)
   - Already has good validation

4. **`LogWatcher.Tests/Unit/Cli/CliParserTests.cs`** (Refactor ~69 lines)
   - Rename to `CommandConfigurationTests.cs`
   - Update test methods to use System.CommandLine

### Files to Delete:

1. **`LogWatcher.App/CliParser.cs`** (171 lines removed ✨)

**Net Impact:**
- Lines removed: 171 (CliParser) + 73 (Program.cs logic moved)
- Lines added: 60 (CommandConfiguration) + 60 (ApplicationHost)
- **Net: -124 lines**
- **Benefit:** Better structured, testable, critical bug fixed

**Why 3 Classes is Right:**
- ✅ **CommandConfiguration:** Clear single responsibility (CLI structure)
- ✅ **ApplicationHost:** Clear single responsibility (app execution)
- ✅ **Program.cs:** Simple wiring
- ✅ **Each testable independently**
- ✅ **No overengineering:** No interfaces, no DI container, just logical separation

**Why not 1 class:**
- ❌ Mixing CLI structure with application execution blurs responsibilities
- ❌ Harder to test independently
- ❌ ~120 line class vs two ~60 line classes

**Why not 8 classes:**
- ❌ System.CommandLine provides validation (no custom validator framework needed)
- ❌ Only one implementation (no need for IApplicationRunner interface)
- ❌ Simple app (no need for CommandBuilder/CommandHandler/CliHost orchestration)

---

## Timeline

| Phase | Duration | Key Activities |
|-------|----------|----------------|
| 1. Preparation | 30 min | Research System.CommandLine 3.0 API |
| 2. Install Dependencies | 15 min | Add NuGet package |
| 3. Design | 30 min | Plan simplified structure |
| 4. Implementation | 2 hours | Create CliConfiguration, update Program.cs |
| 5. Update Tests | 1 hour | Refactor tests |
| 6-8. Testing & Docs | 1 hour | Manual testing, documentation |
| 9. Cleanup | 15 min | Delete CliParser, final review |
| **Total** | **4-5 hours** | |

---

## Rollback Plan

If migration encounters issues:

1. **Revert commits** in reverse order
2. **Restore CliParser.cs** from git history
3. **Remove System.CommandLine** package

Git command:
```bash
git revert HEAD~4..HEAD  # Revert last 4 commits
```

Or use branch strategy:
- Work in `feature/system-commandline` branch
- Test thoroughly before merging
- Easy to abandon if needed

---

## Conclusion

This migration will:
- ✅ **Fix critical bug:** Environment.Exit(2) bypassing finally block
- ✅ **Reduce code:** 171 lines → 80 lines (53% reduction)
- ✅ **Improve maintainability:** Use battle-tested library instead of custom parser
- ✅ **Better UX:** Auto-generated help text with clear error messages
- ✅ **Testability:** Extract application logic for testing
- ✅ **Industry standard:** Professional CLI patterns

### Key Success Factor: Balance

**Avoided Overengineering:**
- No custom validator framework (use System.CommandLine's features)
- No unnecessary interfaces (YAGNI principle)
- No excessive abstraction layers (no CommandBuilder/Handler/CliHost orchestration)

**Applied Appropriate Separation:**
- ✅ **3 focused classes** instead of 1 monolithic or 8 overengineered
- ✅ **CommandConfiguration** (~60 lines): CLI structure definition
- ✅ **ApplicationHost** (~60 lines): Application execution logic
- ✅ **Program.cs** (~10 lines): Simple wiring

**Result:**
- Easier to understand (each class has clear, single purpose)
- Easier to maintain (logical separation, ~60 lines each)
- Testable (can test CLI and app logic independently)
- Solves actual problems (bug fix, better UX, maintainability)
- Right-sized for the problem (CLI tool with ~500 LOC)

### Pragmatic Design Principles

- ✅ **Separation of Concerns:** CLI structure separate from application execution
- ✅ **Single Responsibility:** Each class has one clear purpose
- ✅ **Don't Repeat Yourself:** System.CommandLine handles parsing/validation
- ✅ **Testability:** Each component testable in isolation
- ✅ **YAGNI:** No interfaces, no DI, just logical separation

**Not dogmatic about:**
- Creating interfaces for single implementations
- Perfect adherence to every SOLID principle
- Using design patterns for pattern's sake

**Focused on:**
- Clear code organization
- Appropriate separation of concerns
- Practical testability
- Solving real problems simply

### Recommendation

**STRONGLY RECOMMEND** proceeding with balanced migration:
- Fixes critical bug
- Appropriate separation (3 focused classes)
- Uses proven library
- Testable components
- 4-5 hours well spent

**Balanced Approach:**
- 3 classes with clear responsibilities
- CommandConfiguration (~60 lines)
- ApplicationHost (~60 lines)
- Program.cs (~10 lines)

**Avoids Both Extremes:**
- ❌ Not 1 giant class mixing concerns
- ❌ Not 8 overengineered classes with interfaces
- ✅ Right-sized for the problem

**Focus on:**
- Solving real problems
- Clear organization
- Practical testability
- Delivering value quickly

**Next Step:** Begin Phase 1 and verify System.CommandLine 3.0.0-preview compatibility with .NET 10.

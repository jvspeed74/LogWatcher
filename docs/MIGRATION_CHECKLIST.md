# System.CommandLine Migration - Quick Checklist

## Pre-Flight (5 min)
- [ ] On feature branch: `git checkout -b feature/system-commandline`
- [ ] Tests pass: `dotnet test`
- [ ] Build succeeds: `dotnet build`

## Step 1: Add Package (5 min)
- [ ] Edit `LogWatcher.App/LogWatcher.App.csproj`
- [ ] Add: `<PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />`
- [ ] Run: `dotnet restore && dotnet build`
- [ ] Commit: `git commit -m "feat: add System.CommandLine package"`

## Step 2: CommandConfiguration.cs (30 min)
- [ ] Create: `LogWatcher.App/CommandConfiguration.cs`
- [ ] Copy complete code from implementation guide (126 lines)
- [ ] Verify: 5 options with validators, 1 argument
- [ ] Build: `dotnet build`
- [ ] Commit: `git commit -m "refactor: create CommandConfiguration with System.CommandLine"`

## Step 3: ApplicationHost.cs (20 min)
- [ ] Create: `LogWatcher.App/ApplicationHost.cs`
- [ ] Copy application logic from old Program.cs (82 lines)
- [ ] Verify: try/catch/finally with HostWiring calls
- [ ] Build: `dotnet build`
- [ ] Commit: `git commit -m "refactor: extract application logic to ApplicationHost"`

## Step 4: Program.cs (10 min)
- [ ] Replace entire content with 4 lines from guide
- [ ] Verify: Only calls CommandConfiguration and returns
- [ ] Build: `dotnet build`
- [ ] Commit: `git commit -m "refactor: simplify Program.cs and fix Environment.Exit bug"`

## Step 5: Update Tests (30 min)
- [ ] Rename: `CliParserTests.cs` → `CommandConfigurationTests.cs`
- [ ] Update class name and constructor
- [ ] Update all 4 test methods to use `command.Parse()`
- [ ] Add new test: `Parse_HelpRequested_ShowsHelp`
- [ ] Add using: `using System.CommandLine;`
- [ ] Test: `dotnet test --filter "CommandConfiguration"`
- [ ] Commit: `git commit -m "test: update CLI tests for System.CommandLine"`

## Step 6: Delete Old Code (2 min)
- [ ] Delete: `rm LogWatcher.App/CliParser.cs`
- [ ] Build: `dotnet build`
- [ ] Test: `dotnet test`
- [ ] Commit: `git commit -m "refactor: remove CliParser class"`

## Step 7: Manual Testing (15 min)
- [ ] Help: `dotnet run -- --help` (comprehensive output)
- [ ] Run: `dotnet run -- /tmp` (basic usage)
- [ ] Options: `dotnet run -- /tmp -w 4 -q 5000 -r 5 -k 20`
- [ ] Errors: `dotnet run -- /nonexistent` (clear error)
- [ ] Invalid: `dotnet run -- /tmp --workers abc` (validation)
- [ ] Ctrl+C: Verify clean shutdown

## Step 8: Verification
- [ ] Build: No errors
- [ ] Tests: All pass
- [ ] Files: 2 new, 1 modified, 1 deleted
- [ ] Commits: 6 total
- [ ] Help text: Auto-generated and complete
- [ ] Bug fixed: No Environment.Exit(2) in middle

## Step 9: Merge
- [ ] `git checkout main`
- [ ] `git merge --no-ff feature/system-commandline`
- [ ] `git branch -d feature/system-commandline`

---

## Quick File Reference

### CommandConfiguration.cs Structure
```
- CreateRootCommand() returns RootCommand
  - watchPathArg (required, validated)
  - workersOpt (-w, default: CPU count)
  - queueCapacityOpt (-q, default: 10000)
  - reportIntervalOpt (-r, default: 2)
  - topKOpt (-k, default: 10)
  - SetHandler: creates CliConfig, calls ApplicationHost.Run()
```

### ApplicationHost.cs Structure
```
- Run(CliConfig) returns int
  - Declares all component variables
  - try: construct, wire, start, wait
  - catch: log error, return 1
  - finally: cleanup
```

### Program.cs (Complete)
```csharp
using LogWatcher.App;

var rootCommand = CommandConfiguration.CreateRootCommand();
return rootCommand.Invoke(args);
```

---

## Troubleshooting Quick Fixes

**Build fails on System.CommandLine:**
→ Check package version, run `dotnet restore`

**Tests fail:**
→ Use `command.Parse()` not `CliParser.TryParse()`

**Help doesn't work:**
→ System.CommandLine handles it automatically

**App doesn't exit:**
→ Check Environment.Exit() in SetHandler

---

## Success = 6 Commits
1. feat: add System.CommandLine package
2. refactor: create CommandConfiguration with System.CommandLine
3. refactor: extract application logic to ApplicationHost
4. refactor: simplify Program.cs and fix Environment.Exit bug
5. test: update CLI tests for System.CommandLine
6. refactor: remove CliParser class

**Estimated Time: 2-3 hours**


// Replace the trivial template with the continuous seed writer implementation
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace WatchStats.Seed
{
    internal static class Program
    {
        private static readonly string[] Levels = new[] { "INFO", "WARN", "ERROR", "DEBUG" };
        private static readonly string[] Messages = new[]
        {
            "Processed request",
            "Handled connection",
            "Completed job",
            "Timeout occurred",
            "User action",
            "Background task"
        };

        private static long _totalLinesWritten;
        private static long _createdFiles;
        private static long _appendedFiles;
        private static long _deletedFiles;
        private static long _failures;
        private static long _iterations;
        private static long _activeWorkers;

        public static async Task<int> Main(string[] args)
        {
            // load configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            var configuration = builder.Build();
            var config = configuration.Get<SeedConfig>() ?? new SeedConfig();

            try
            {
                config.Validate();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Invalid configuration: " + ex.Message);
                return 2;
            }

            var tempPath = Path.GetFullPath(config.TempPath);
            Directory.CreateDirectory(tempPath);

            if (config.ClearTempOnStart)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(tempPath))
                    {
                        File.Delete(f);
                    }
                    Console.WriteLine($"Cleared directory: {tempPath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed clearing temp dir: " + ex);
                }
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var startTime = DateTime.UtcNow;

            Console.WriteLine("Starting seed writer. TempPath={0} MaxTotalFileOperations={1} ConcurrentWorkers={2}", 
                tempPath, config.MaxTotalFileOperations, config.ConcurrentWorkers);

            // Configure ThreadPool for maximum throughput
            ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
            ThreadPool.SetMinThreads(Math.Max(minWorkerThreads, config.ConcurrentWorkers * 2), minCompletionPortThreads);

            // Start summary reporter task
            var summaryTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(config.SummaryIntervalSeconds), cts.Token).ConfigureAwait(false);
                        PrintSummary(startTime);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

            // Launch concurrent workers
            var workerTasks = new Task[config.ConcurrentWorkers];
            for (int i = 0; i < config.ConcurrentWorkers; i++)
            {
                int workerId = i;
                workerTasks[i] = Task.Run(() => WorkerLoop(workerId, config, tempPath, cts.Token));
            }

            // Wait for all workers to complete
            await Task.WhenAll(workerTasks).ConfigureAwait(false);

            // Stop summary task
            cts.Cancel();
            try
            {
                await summaryTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            Console.WriteLine("Shutting down. Final summary:");
            PrintSummary(startTime);

            return 0;
        }

        private static void WorkerLoop(int workerId, SeedConfig config, string tempPath, CancellationToken ct)
        {
            Interlocked.Increment(ref _activeWorkers);
            var rnd = new Random(Guid.NewGuid().GetHashCode()); // Thread-local random with unique seed

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Check fail-safe
                    if (config.MaxTotalFileOperations > 0)
                    {
                        var totalOps = Interlocked.Read(ref _createdFiles) + 
                                      Interlocked.Read(ref _appendedFiles) + 
                                      Interlocked.Read(ref _deletedFiles);
                        if (totalOps >= config.MaxTotalFileOperations)
                        {
                            break;
                        }
                    }

                    Interlocked.Increment(ref _iterations);

                    try
                    {
                        var fileId = rnd.Next(config.FileNameMin, config.FileNameMax + 1);
                        var extChoices = new System.Collections.Generic.List<string>();
                        if (config.EnableTxt) extChoices.Add(".txt");
                        if (config.EnableLog) extChoices.Add(".log");

                        var ext = extChoices[rnd.Next(extChoices.Count)];
                        var filePath = Path.Combine(tempPath, fileId + ext);

                        if (File.Exists(filePath))
                        {
                            var r = rnd.NextDouble();
                            if (r < config.DeleteExistingProbability)
                            {
                                try
                                {
                                    File.Delete(filePath);
                                    Interlocked.Increment(ref _deletedFiles);
                                    if ((Interlocked.Read(ref _iterations) & 0xFF) == 0)
                                    {
                                        Console.WriteLine("[Worker {0}] Deleted: {1}", workerId, filePath);
                                    }
                                    // Apply delay and continue to next iteration
                                    if (config.DelayMsBetweenIterations > 0)
                                    {
                                        Thread.Sleep(config.DelayMsBetweenIterations);
                                    }
                                    continue;
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref _failures);
                                    if ((Interlocked.Read(ref _failures) & 0x1F) == 1)
                                    {
                                        Console.Error.WriteLine("[Worker {0}] Failed to delete {1}: {2}", workerId, filePath, ex.Message);
                                    }
                                }
                            }
                        }

                        var linesToAdd = rnd.Next(config.LinesPerFileMin, config.LinesPerFileMax + 1);

                        // append or create
                        try
                        {
                            var isNew = !File.Exists(filePath);
                            using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                            using (var sw = new StreamWriter(fs, Encoding.UTF8))
                            {
                                for (int i = 0; i < linesToAdd; i++)
                                {
                                    var timestamp = DateTime.UtcNow.ToString("o");
                                    var level = Levels[rnd.Next(Levels.Length)];
                                    var message = Messages[rnd.Next(Messages.Length)];
                                    var latency = rnd.Next(1, 1000);  // todo: was accidentally unbound, consider making configurable
                                    sw.WriteLine($"{timestamp} {level} {message} latency_ms={latency}");
                                }
                            }

                            Interlocked.Add(ref _totalLinesWritten, linesToAdd);
                            if (isNew)
                            {
                                Interlocked.Increment(ref _createdFiles);
                            }
                            else
                            {
                                Interlocked.Increment(ref _appendedFiles);
                            }

                            // occasionally print a small log line
                            if ((Interlocked.Read(ref _iterations) & 0xFF) == 0)
                            {
                                Console.WriteLine("[Worker {0}] Wrote {1} lines to {2} (totalWrites={3})", 
                                    workerId, linesToAdd, Path.GetFileName(filePath), Interlocked.Read(ref _totalLinesWritten));
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref _failures);
                            if ((Interlocked.Read(ref _failures) & 0x1F) == 1)
                            {
                                Console.Error.WriteLine("[Worker {0}] Failed to write to {1}: {2}", workerId, filePath, ex.Message);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _failures);
                        if ((Interlocked.Read(ref _failures) & 0x1F) == 1)
                        {
                            Console.Error.WriteLine("[Worker {0}] Iteration failure: {1}", workerId, ex.Message);
                        }
                    }

                    // delay between iterations
                    if (config.DelayMsBetweenIterations > 0)
                    {
                        try
                        {
                            Thread.Sleep(config.DelayMsBetweenIterations);
                        }
                        catch (ThreadInterruptedException)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkers);
            }
        }

        private static void PrintSummary(DateTime startTime)
        {
            var elapsed = DateTime.UtcNow - startTime;
            Console.WriteLine("--- Summary ---");
            Console.WriteLine("Elapsed: {0}", elapsed);
            Console.WriteLine("Active workers: {0}", Interlocked.Read(ref _activeWorkers));
            Console.WriteLine("Iterations: {0}", Interlocked.Read(ref _iterations));
            Console.WriteLine("Total lines written: {0}", Interlocked.Read(ref _totalLinesWritten));
            Console.WriteLine("Created files: {0}", Interlocked.Read(ref _createdFiles));
            Console.WriteLine("Appended files: {0}", Interlocked.Read(ref _appendedFiles));
            Console.WriteLine("Deleted files: {0}", Interlocked.Read(ref _deletedFiles));
            Console.WriteLine("Failures: {0}", Interlocked.Read(ref _failures));
            Console.WriteLine("---------------");
        }
    }
}

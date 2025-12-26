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

        public static async Task<int> Main(string[] args)
        {
            // load configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
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

            var rnd = new Random();

            long totalWrites = 0;
            long createdFiles = 0;
            long appendedFiles = 0;
            long deletedFiles = 0;
            long failures = 0;
            long iterations = 0;

            var lastSummary = DateTime.UtcNow;
            var startTime = DateTime.UtcNow;

            Console.WriteLine("Starting seed writer. TempPath={0} MaxTotalFilesWritten={1}", tempPath, config.MaxTotalFilesWritten);

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    iterations++;

                    // check fail-safe
                    if (config.MaxTotalFilesWritten > 0 && totalWrites >= config.MaxTotalFilesWritten)
                    {
                        Console.WriteLine("Reached MaxTotalFilesWritten ({0}), stopping.", config.MaxTotalFilesWritten);
                        break;
                    }

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
                                    deletedFiles++;
                                    Console.WriteLine("Deleted: {0}", filePath);
                                    // skip writing for this iteration; continue to next random draw
                                    await Task.Delay(config.DelayMsBetweenIterations, cts.Token).ConfigureAwait(false);
                                    continue;
                                }
                                catch (Exception ex)
                                {
                                    failures++;
                                    Console.Error.WriteLine("Failed to delete {0}: {1}", filePath, ex.Message);
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
                                    var latency = rnd.Next();
                                    sw.WriteLine($"{timestamp} {level} {message} latency_ms={latency}");
                                }
                            }

                            totalWrites += linesToAdd;
                            if (isNew) createdFiles++; else appendedFiles++;

                            // occasionally print a small log line
                            if ((iterations & 0xF) == 0)
                            {
                                Console.WriteLine("Wrote {0} lines to {1} (totalWrites={2})", linesToAdd, filePath, totalWrites);
                            }
                        }
                        catch (Exception ex)
                        {
                            failures++;
                            Console.Error.WriteLine("Failed to write to {0}: {1}", filePath, ex.Message);
                        }
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.Error.WriteLine("Iteration failure: " + ex);
                    }

                    // summary
                    var now = DateTime.UtcNow;
                    if ((now - lastSummary).TotalSeconds >= config.SummaryIntervalSeconds)
                    {
                        lastSummary = now;
                        var elapsed = now - startTime;
                        Console.WriteLine("--- Summary ---");
                        Console.WriteLine("Elapsed: {0}", elapsed);
                        Console.WriteLine("Iterations: {0}", iterations);
                        Console.WriteLine("Total lines written: {0}", totalWrites);
                        Console.WriteLine("Created files: {0}", createdFiles);
                        Console.WriteLine("Appended files: {0}", appendedFiles);
                        Console.WriteLine("Deleted files: {0}", deletedFiles);
                        Console.WriteLine("Failures: {0}", failures);
                        Console.WriteLine("---------------");
                    }

                    // delay between iterations
                    try
                    {
                        await Task.Delay(config.DelayMsBetweenIterations, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Console.WriteLine("Shutting down. Final summary:");
                var elapsed = DateTime.UtcNow - startTime;
                Console.WriteLine("Elapsed: {0}", elapsed);
                Console.WriteLine("Iterations: {0}", iterations);
                Console.WriteLine("Total lines written: {0}", totalWrites);
                Console.WriteLine("Created files: {0}", createdFiles);
                Console.WriteLine("Appended files: {0}", appendedFiles);
                Console.WriteLine("Deleted files: {0}", deletedFiles);
                Console.WriteLine("Failures: {0}", failures);
            }

            return 0;
        }
    }
}

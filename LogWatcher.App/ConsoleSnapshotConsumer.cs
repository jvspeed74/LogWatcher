using LogWatcher.Core.Reporting;

namespace LogWatcher.App;

/// <summary>
/// Writes each <see cref="GlobalSnapshot"/> to <see cref="Console"/> in the existing log-watcher report format.
/// Extracted verbatim from the former <c>Reporter.PrintReportFrame</c> method.
/// </summary>
public sealed class ConsoleSnapshotConsumer : ISnapshotConsumer
{
    /// <inheritdoc/>
    public void OnSnapshot(GlobalSnapshot snapshot, TimeSpan elapsed)
    {
        double elapsedSeconds = elapsed.TotalSeconds;

        double fsEventsTotal = snapshot.FsCreated + snapshot.FsModified + snapshot.FsDeleted + snapshot.FsRenamed;
        double fsRate = elapsedSeconds > 0 ? fsEventsTotal / elapsedSeconds : 0.0;
        double linesRate = elapsedSeconds > 0 ? snapshot.LinesProcessed / elapsedSeconds : 0.0;

        Console.WriteLine(
            $"[REPORT] elapsed={elapsedSeconds:0.00}s lines={snapshot.LinesProcessed} lines/s={linesRate:0.00} malformed={snapshot.MalformedLines} fs-events={fsEventsTotal} fs/s={fsRate:0.00} busDropped={snapshot.BusDropped} busPublished={snapshot.BusPublished} busDepth={snapshot.BusDepth} allocatedDelta={snapshot.AllocatedBytesDelta} allocatedLifetime={snapshot.AllocatedBytesTotal} gen0Delta={snapshot.Gen0Delta} gen1Delta={snapshot.Gen1Delta} gen2Delta={snapshot.Gen2Delta}");

        if (snapshot.TopKMessages.Count > 0)
        {
            Console.WriteLine("TopK:");
            foreach (var kv in snapshot.TopKMessages)
                Console.WriteLine($"  {kv.Key}: {kv.Count}");
        }
    }
}
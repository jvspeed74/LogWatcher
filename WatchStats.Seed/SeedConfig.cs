using System;
using System.IO;

namespace WatchStats.Seed
{
    public sealed class SeedConfig
    {
        public string TempPath { get; init; } = "./temp";
        public bool ClearTempOnStart { get; init; } = false;
        public bool EnableTxt { get; init; } = true;
        public bool EnableLog { get; init; } = true;
        public int FileNameMin { get; init; } = 1;
        public int FileNameMax { get; init; } = 10000;
        public int LinesPerFileMin { get; init; } = 100;
        public int LinesPerFileMax { get; init; } = 1000;
        public double DeleteExistingProbability { get; init; } = 0.1;
        public int DelayMsBetweenIterations { get; init; } = 200;
        public long MaxTotalFilesWritten { get; init; } = 100000;
        public int SummaryIntervalSeconds { get; init; } = 30;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TempPath)) throw new ArgumentException("TempPath is required");
            if (!Directory.Exists(TempPath))
            {
                // try to create later; not considered an error here
            }
            if (!EnableTxt && !EnableLog) throw new ArgumentException("At least one of EnableTxt or EnableLog must be true");
            if (FileNameMin < 0) throw new ArgumentOutOfRangeException(nameof(FileNameMin));
            if (FileNameMax < FileNameMin) throw new ArgumentOutOfRangeException(nameof(FileNameMax));
            if (LinesPerFileMin < 0) throw new ArgumentOutOfRangeException(nameof(LinesPerFileMin));
            if (LinesPerFileMax < LinesPerFileMin) throw new ArgumentOutOfRangeException(nameof(LinesPerFileMax));
            if (DeleteExistingProbability < 0.0 || DeleteExistingProbability > 1.0) throw new ArgumentOutOfRangeException(nameof(DeleteExistingProbability));
            if (DelayMsBetweenIterations < 0) throw new ArgumentOutOfRangeException(nameof(DelayMsBetweenIterations));
            if (MaxTotalFilesWritten < 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalFilesWritten));
            if (SummaryIntervalSeconds < 1) throw new ArgumentOutOfRangeException(nameof(SummaryIntervalSeconds));
        }
    }
}


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
        public long MaxTotalFileOperations { get; init; } = 100000;
        public int SummaryIntervalSeconds { get; init; } = 30;
        public int ConcurrentWorkers { get; init; } = 8;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TempPath)) throw new ArgumentException("TempPath is required");
            if (!Directory.Exists(TempPath))
            {
                throw new DirectoryNotFoundException($"{TempPath} does not exist");
            }
            if (!EnableTxt && !EnableLog) throw new ArgumentException("At least one of EnableTxt or EnableLog must be true");
            if (FileNameMin < 0) throw new ArgumentOutOfRangeException(nameof(FileNameMin));
            if (FileNameMax < FileNameMin) throw new ArgumentOutOfRangeException(nameof(FileNameMax));
            if (LinesPerFileMin < 0) throw new ArgumentOutOfRangeException(nameof(LinesPerFileMin));
            if (LinesPerFileMax < LinesPerFileMin) throw new ArgumentOutOfRangeException(nameof(LinesPerFileMax));
            if (DeleteExistingProbability < 0.0 || DeleteExistingProbability > 1.0) throw new ArgumentOutOfRangeException(nameof(DeleteExistingProbability));
            if (DelayMsBetweenIterations < 0) throw new ArgumentOutOfRangeException(nameof(DelayMsBetweenIterations));
            if (MaxTotalFileOperations < 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalFileOperations));
            if (SummaryIntervalSeconds < 1) throw new ArgumentOutOfRangeException(nameof(SummaryIntervalSeconds));
            if (ConcurrentWorkers < 1) throw new ArgumentOutOfRangeException(nameof(ConcurrentWorkers));
        }
    }
}


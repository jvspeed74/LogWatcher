namespace LogWatcher.Core.Processing.Tailing;

/// <summary>
/// Describes the outcome of an attempt to read newly appended bytes from a file.
/// </summary>
public enum TailReadStatus
{
    /// <summary>No new data was available for the requested offset.</summary>
    NoData,
    /// <summary>Some bytes were read and the provided offset was advanced.</summary>
    ReadSome,
    /// <summary>The target file was not found.</summary>
    FileNotFound,
    /// <summary>Access to the file was denied.</summary>
    AccessDenied,
    /// <summary>An I/O error occurred while attempting to read the file.</summary>
    IoError,
    /// <summary>The file was truncated and the offset was reset to zero; bytes were (optionally) read.</summary>
    TruncatedReset
}
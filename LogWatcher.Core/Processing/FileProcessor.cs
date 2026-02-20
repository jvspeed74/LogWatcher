using System.Text;

using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Processing.Parsing;
using LogWatcher.Core.Processing.Scanning;
using LogWatcher.Core.Processing.Tailing;
using LogWatcher.Core.Statistics;

namespace LogWatcher.Core.Processing
{
    /// <summary>
    /// Abstraction for a component that processes appended data in a single file once.
    /// Implementations should read newly appended bytes, extract complete lines, parse them
    /// and update the provided <see cref="WorkerStatsBuffer"/>.
    /// </summary>
    public interface IFileProcessor
    {
        /// <summary>
        /// Process whatever was appended to <paramref name="path"/> since <paramref name="state"/>.Offset.
        /// The caller is responsible for acquiring any necessary synchronization (e.g. <c>state.Gate</c>);
        /// this method will not attempt to acquire it.
        /// </summary>
        /// <param name="path">Filesystem path to the file to process.</param>
        /// <param name="state">File-specific state object (offset/carry buffer) that will be read and updated.</param>
        /// <param name="stats">Worker-local statistics buffer that will be updated from parsed lines.</param>
        /// <param name="chunkSize">Optional read chunk size passed to the file tailer. Defaults to 64KiB.</param>
        // FIXME: The default value `64 * 1024` here is a bare literal that duplicates the same constant defined
        // privately in FileTailer (DefaultChunkSize) and again in FileProcessor.ProcessOnce. All three must be
        // kept in sync manually. A shared public constant exposed from FileTailer (or a separate constants class)
        // would provide a single source of truth.
        void ProcessOnce(string path, FileState state, WorkerStatsBuffer stats, int chunkSize = 64 * 1024);
    }

    // TODO: FileProcessor.ProcessOnce hard-codes static calls to FileTailer, Utf8LineScanner, and LogParser.
    // None of these dependencies can be replaced with test doubles, so FileProcessor cannot be unit-tested
    // in isolation without real file I/O and real parsing. Consider injecting these as abstractions (interfaces
    // or delegates) through the constructor so each concern can be tested and substituted independently.

    // FileProcessor: tail -> scan -> parse -> update WorkerStatsBuffer
    /// <summary>
    /// Implementation of <see cref="IFileProcessor"/> that uses <see cref="FileTailer"/>,
    /// <see cref="Utf8LineScanner"/> and <see cref="LogParser"/> to read appended data,
    /// parse newline-delimited UTF-8 log lines and update a <see cref="WorkerStatsBuffer"/>.
    /// </summary>
    public sealed class FileProcessor : IFileProcessor
    {
        /// <summary>
        /// Creates a new <see cref="FileProcessor"/>.
        /// </summary>
        // TODO: This constructor is vestigial. All dependencies (FileTailer, Utf8LineScanner, LogParser) are
        // consumed as static calls, so there is nothing to inject or configure here. The empty constructor gives
        // callers the false impression that FileProcessor has no dependencies, masking the real coupling.
        // If the static dependencies were replaced with injected abstractions, this constructor would need parameters.
        public FileProcessor()
        {
        }

        // TODO: ProcessOnce violates the Single Responsibility Principle. It performs five distinct concerns
        // in one method body: (1) file I/O via FileTailer, (2) byte scanning via Utf8LineScanner,
        // (3) log parsing via LogParser, (4) statistics mutation, and (5) I/O error mapping.
        // These responsibilities should be separated so each can be understood, tested, and changed independently.
        //
        // TODO: The three levels of nested lambdas (chunk => { Scan(line => { ... }) }) make control flow,
        // exception propagation, and stat mutation hard to follow. Extracting the inner bodies into named
        // private methods (e.g., ProcessChunk, ProcessLine) would improve readability and testability.
        //
        // FIXME: The default value `chunkSize = 64 * 1024` is a bare literal here and in IFileProcessor.ProcessOnce,
        // duplicating the same magic number that FileTailer already defines as DefaultChunkSize (private).
        // There is no single authoritative constant; changing the value in one place silently diverges from the others.
        /// <summary>
        /// Process whatever is appended right now. Caller must hold <c>state.Gate</c>.
        /// This method advances <c>state.Offset</c> only after processing completes successfully.
        /// For each complete UTF-8 line it increments counters in <paramref name="stats"/>,
        /// updates message counts, and adds parsed latency values to the histogram.
        /// </summary>
        /// <param name="path">Path to the file to process. Must not be <c>null</c>.</param>
        /// <param name="state">File state object that contains offset and carry buffer. Must not be <c>null</c>.</param>
        /// <param name="stats">Worker-local statistics buffer to update. Must not be <c>null</c>.</param>
        /// <param name="chunkSize">Read buffer size in bytes for tailer. Defaults to 64KiB.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/>, <paramref name="state"/>, or <paramref name="stats"/> is <c>null</c>.</exception>
        public void ProcessOnce(string path, FileState state, WorkerStatsBuffer stats, int chunkSize = 64 * 1024)
        {
            // Precondition: caller must hold state.Gate. We won't double-check locking here, but document it.
            // Use local offset to avoid advancing state.Offset until processing completes.
            long localOffset = state.Offset;

            TailReadStatus status = FileTailer.ReadAppended(path, ref localOffset, chunk =>
            {
                // For each chunk, run the UTF8 scanner using state's carry buffer
                Utf8LineScanner.Scan(chunk, ref state.Carry, line =>
                {
                    // For each complete line
                    stats.LinesProcessed++;

                    if (!LogParser.TryParse(line, out var parsed))
                    {
                        stats.MalformedLines++;
                        return;
                    }

                    // level counts
                    stats.IncrementLevel(parsed.Level);

                    // message key to string
                    // TODO: Encoding.UTF8.GetString allocates a new string for every line with a non-empty message key.
                    // In a high-throughput scenario (many lines per second across many files) this creates sustained
                    // GC pressure. Consider an interning strategy (e.g., a ConcurrentDictionary<string,string> keyed
                    // on the raw UTF-8 bytes via a custom comparer) or using MemoryMarshal to avoid the allocation.
                    string key = parsed.MessageKey.IsEmpty ? string.Empty : Encoding.UTF8.GetString(parsed.MessageKey);
                    if (stats.MessageCounts.TryGetValue(key, out var c)) stats.MessageCounts[key] = c + 1;
                    else stats.MessageCounts[key] = 1;

                    // latency
                    if (parsed.LatencyMs is { } v)
                    {
                        stats.Histogram.Add(v);
                    }
                });
            }, out var totalBytesRead, chunkSize);

            // handle status counters
            // TODO: The stat mutations inside the lambda closures above (stats.LinesProcessed++, stats.MalformedLines++,
            // etc.) happen deep inside anonymous callbacks with no clear boundary. This makes it difficult to add
            // tracing, intercept individual line processing, or write tests that assert on per-line behavior without
            // exercising the full I/O pipeline. Extracting processing logic into named private methods would help.
            switch (status)
            {
                case TailReadStatus.FileNotFound:
                    stats.FileNotFoundCount++;
                    break;
                case TailReadStatus.AccessDenied:
                    stats.AccessDeniedCount++;
                    break;
                case TailReadStatus.IoError:
                    stats.IoExceptionCount++;
                    break;
                case TailReadStatus.TruncatedReset:
                    stats.TruncationResetCount++;
                    break;
                // FIXME: The `default:` catch-all here silently absorbs any future TailReadStatus values that are
                // added to the enum. NoData and ReadSome are listed explicitly, but new statuses would fall through
                // to the empty default without any counter update or compile-time warning. Consider removing the
                // default and letting the compiler warn on unhandled cases, or throw for truly unexpected values.
                case TailReadStatus.NoData:
                case TailReadStatus.ReadSome:
                default:
                    break;
            }

            // advance state.Offset only after successful processing
            if (totalBytesRead > 0 || status == TailReadStatus.TruncatedReset)
            {
                state.Offset = localOffset;
            }
        }
    }
}
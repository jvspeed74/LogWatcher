namespace WatchStats.Core.Processing
{
    /// <summary>
    /// Per-path mutable state used by the tailer and processor.
    /// Callers should synchronize access to mutable fields using the <see cref="Gate"/> object.
    /// </summary>
    public sealed class FileState
    {
        /// <summary>
        /// Lock object used to synchronize concurrent access to this FileState.
        /// Callers should acquire this lock (for example via <c>lock(state.Gate)</c> or <c>Monitor.Enter(state.Gate)</c>)
        /// when reading or modifying fields such as <see cref="Offset"/> and <see cref="Carry"/>.
        /// </summary>
        public object Gate { get; } = new();

        /// <summary>Offset (in bytes) representing how many bytes have been consumed from the file.</summary>
        public long Offset { get; set; }

        /// <summary>Carry buffer that holds trailing, incomplete line bytes between reads.</summary>
        internal PartialLineBuffer Carry;

        private int _dirty;
        private int _deletePending;

        /// <summary>Generation number assigned at creation (epoch + 1).</summary>
        public int Generation { get; init; }

        /// <summary>Returns true when the state is marked dirty and needs reprocessing.</summary>
        public bool IsDirty => Volatile.Read(ref _dirty) == 1;

        /// <summary>Returns true when a delete has been requested and the state should be finalized.</summary>
        public bool IsDeletePending => Volatile.Read(ref _deletePending) == 1;

        /// <summary>
        /// Marks the state dirty if deletion is not currently pending. This may be called without holding <see cref="Gate"/>.
        /// </summary>
        public void MarkDirtyIfAllowed()
        {
            // If delete pending is set, do not mark dirty
            if (Volatile.Read(ref _deletePending) == 1) return;
            Volatile.Write(ref _dirty, 1);
        }

        /// <summary>
        /// Clears the dirty flag. Typically called while holding <see cref="Gate"/>.
        /// </summary>
        public void ClearDirty()
        {
            Volatile.Write(ref _dirty, 0);
        }

        /// <summary>
        /// Requests deletion of the state (marks delete-pending) and clears the dirty flag.
        /// </summary>
        public void MarkDeletePending()
        {
            Volatile.Write(ref _deletePending, 1);
            // override dirty
            Volatile.Write(ref _dirty, 0);
        }

        /// <summary>
        /// Helper to clear carry memory for GC hygiene. This sets the carry buffer to null and zeroes its length.
        /// Callers typically hold <see cref="Gate"/> when invoking this.
        /// </summary>
        public void ClearCarry()
        {
            // reset buffer and length
            Carry.Buffer = null;
            Carry.Length = 0;
        }
    }
}
using LogWatcher.Core.Reporting;

namespace LogWatcher.Tests.Helpers;

/// <summary>
/// Test helper that invokes a callback on each snapshot for assertion purposes.
/// </summary>
internal sealed class CapturingSnapshotConsumer(Action<GlobalSnapshot> onSnapshot) : ISnapshotConsumer
{
    public void OnSnapshot(GlobalSnapshot snapshot, TimeSpan elapsed) => onSnapshot(snapshot);
}
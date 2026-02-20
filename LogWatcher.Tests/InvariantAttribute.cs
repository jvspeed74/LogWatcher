namespace LogWatcher.Tests;

/// <summary>
/// Marks a test method or class as protecting a specific invariant.
/// Multiple attributes can be applied to declare which invariants a test covers.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [Fact]
/// [Invariant("BP-001")]
/// [Invariant("BP-002")]
/// public void Publish_WhenFull_DropsNewestAndPreservesExisting() { ... }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class InvariantAttribute : Attribute
{
    /// <summary>
    /// Gets the invariant ID being protected by this test.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvariantAttribute"/> class.
    /// </summary>
    /// <param name="id">The invariant ID (e.g., "BP-001", "FM-002").</param>
    public InvariantAttribute(string id)
    {
        Id = id;
    }
}
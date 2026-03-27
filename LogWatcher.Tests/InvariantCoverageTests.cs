using System.Reflection;
using System.Text.RegularExpressions;

namespace LogWatcher.Tests;

/// <summary>
/// Two-way integrity check between invariants.md and [Invariant] tags on tests.
///
/// Forward check: every ID defined in invariants.md has at least one tagged test.
/// Reverse check: every ID referenced in [Invariant] tags exists in invariants.md.
///
/// Both directions fail hard so CI catches gaps and stale tags alike.
/// </summary>
public class InvariantCoverageTests
{
    private static readonly Lazy<IReadOnlySet<string>> DefinedIds = new(LoadDefinedIds);
    private static readonly Lazy<ILookup<string, string>> TaggedTests = new(LoadTaggedTests);

    // -------------------------------------------------------------------------
    // Forward: every defined invariant must have at least one tagged test
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> AllDefinedIds()
        => DefinedIds.Value.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AllDefinedIds))]
    public void DefinedInvariant_HasAtLeastOneTaggedTest(string invariantId)
    {
        var coveringTests = TaggedTests.Value[invariantId].ToList();

        Assert.True(
            coveringTests.Count > 0,
            $"Invariant {invariantId} has no covering tests. " +
            $"Add [Invariant(\"{invariantId}\")] to at least one test or remove the invariant from invariants.md.");
    }

    // -------------------------------------------------------------------------
    // Reverse: every tag referenced in tests must exist in invariants.md
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> AllTaggedIds()
        => TaggedTests.Value
            .Select(g => g.Key)
            .Distinct()
            .Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AllTaggedIds))]
    public void TaggedInvariant_ExistsInMarkdown(string invariantId)
    {
        Assert.True(
            DefinedIds.Value.Contains(invariantId),
            $"[Invariant(\"{invariantId}\")] is referenced in tests but not defined in invariants.md. " +
            $"Either add the invariant to the document or correct the tag.");
    }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    private static IReadOnlySet<string> LoadDefinedIds()
    {
        // invariants.md is embedded as a resource in the test assembly.
        // To embed: in the .csproj add
        //   <EmbeddedResource Include="invariants.md" />
        var assembly = typeof(InvariantCoverageTests).Assembly;
        var resourceName = assembly
                               .GetManifestResourceNames()
                               .SingleOrDefault(n => n.EndsWith("invariants.md", StringComparison.OrdinalIgnoreCase))
                           ?? throw new InvalidOperationException(
                               "Could not find embedded resource 'invariants.md'. " +
                               "Ensure the file is included with <EmbeddedResource Include=\"invariants.md\" /> in the test .csproj.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd();

        // Matches IDs of the form: BP-001, FM-PLB-003, HOST-002, etc.
        // Anchored to a word boundary so we don't match substrings inside longer tokens.
        var pattern = new Regex(
            @"\b([A-Z]+(?:-[A-Z]+)*-\d{3})\b",
            RegexOptions.Compiled);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in pattern.Matches(markdown))
            ids.Add(match.Groups[1].Value);

        if (ids.Count == 0)
            throw new InvalidOperationException(
                "No invariant IDs found in invariants.md. " +
                "Expected IDs matching the pattern [A-Z]+-[0-9]{3} (e.g. BP-001, FM-PLB-003).");

        return ids;
    }

    private static ILookup<string, string> LoadTaggedTests()
    {
        // Reflect over every test method in every test class in this assembly.
        var assembly = typeof(InvariantCoverageTests).Assembly;

        var entries = assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttributes<FactAttribute>().Any()
                        || m.GetCustomAttributes<TheoryAttribute>().Any())
            .SelectMany(m =>
                m.GetCustomAttributes<InvariantAttribute>()
                    .Select(attr => (Id: attr.Id, Test: $"{m.DeclaringType!.Name}.{m.Name}")));

        return entries.ToLookup(e => e.Id, e => e.Test, StringComparer.Ordinal);
    }
}
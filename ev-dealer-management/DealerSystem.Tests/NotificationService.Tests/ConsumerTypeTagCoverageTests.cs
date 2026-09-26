using System.Reflection;
using System.Text.RegularExpressions;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Closes the gap <see cref="NotificationPreferencePolicyTests"/> leaves open.
///
/// That file pins the type-tag table against ITSELF: MemberData lists 14
/// (tag, flag) pairs and the policy is expected to agree. It never looks at
/// the consumers, so the table and the producers can drift apart without a
/// single test going red — and the failure is invisible in production.
///
/// WHY THE DRIFT MATTERS. Every consumer hard-codes its tag as a string
/// literal in its data dictionary ("type", "order"). Nothing shares a
/// constant between them and the policy. A consumer that ships
/// ("type", "delivery") against a policy table that has no "delivery" row does
/// not fail: NotificationPreferencePolicy treats an unmapped tag as
/// fail-open, on purpose, so a new producer is never muted by absence. The
/// result is that ONE user turns off Deliveries, a push arrives anyway, and
/// nothing anywhere says so. That is the exact silent-failure shape
/// docs/EVENTS.md exists to prevent — a push that should have been muted and
/// was not.
///
/// WHY THIS IS NOT THE SAME AS THE OTHER TEST. Fail-open is the right
/// default and this does not change it. What this pins is the OTHER direction:
/// every tag a consumer actually ships must be a tag the policy knows. The
/// gap is fail-open, so it needs its own gate; the fail-open behaviour
/// itself is already covered by UnknownTag_Delivers_UnmappedIsNotMuted.
///
/// WHY A SOURCE SCAN AND NOT A BEHAVIOURAL TEST. There is no seam to observe
/// what tag a consumer sends: each builds its own Dictionary inline inside
/// ExecuteAsync and hands it to the push helper. Reaching the tags
/// behaviourally would mean running 15 consumers against a live FCM stub,
/// which is a much larger test than the thing being protected. The tags are
/// literals in source, so the honest oracle is the source — read by the
/// compiler's own build output location rather than a hand-maintained list,
/// so this test cannot itself rot into a fourth copy of the answer.
/// </summary>
public class ConsumerTypeTagCoverageTests
{
    /// <summary>
    /// Consumers' tag shape: the "type" entry of the data dictionary. Tolerates
    /// whitespace variations inside the initializer, which is why this is a
    /// regex over source and not a dictionary parse.
    /// </summary>
    private static readonly Regex TagLiteral = new(
        """\{\s*"type"\s*,\s*"([^"]+)"\s*\}""", RegexOptions.Compiled);

    /// <summary>
    /// Locates the consumers source directory. The test assembly runs from
    /// DealerSystem.Tests/NotificationService.Tests/bin/Debug/net8.0, so the
    /// sources are five directories up. Resolved from AppContext.BaseDirectory
    /// rather than a relative guess so the test fails with a clear message on a
    /// different layout instead of silently finding zero files and passing.
    /// </summary>
    private static string ConsumersDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "NotificationService", "Consumers");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException(
            $"could not locate NotificationService/Consumers above {AppContext.BaseDirectory}");
    }

    /// <summary>Every tag a consumer actually ships, with the file it came
    /// from — the file name is carried into the failure message so a violation
    /// names the consumer to fix rather than just the string.</summary>
    private static List<(string Tag, string Source)> ShippedTags() =>
        Directory.EnumerateFiles(ConsumersDirectory(), "*.cs")
            .SelectMany(file => TagLiteral.Matches(File.ReadAllText(file))
                .Select(m => (Tag: m.Groups[1].Value, Source: Path.GetFileName(file))))
            .ToList();

    /// <summary>
    /// The load-bearing assertion: a tag a consumer ships and the policy does
    /// not map would be delivered fail-open to a user who muted its category.
    /// This test is what stops that from shipping unnoticed.
    /// </summary>
    [Fact]
    public void EveryTypeTagAConsumerShips_IsMappedByThePolicy()
    {
        var shipped = ShippedTags();

        // A vacuous pass would be worse than a failure: if the scan silently
        // matched nothing (layout change, rename of the "type" key), the
        // assertion below would hold over an empty set and protect nothing.
        Assert.NotEmpty(shipped);

        // Ask the policy itself rather than reading its private dictionary, so
        // this stays a behavioural test wherever the lookup actually lives.
        var policy = new NotificationPreferencePolicy(new AlwaysMutedStore());

        var unmapped = new List<string>();
        foreach (var (tag, source) in shipped)
        {
            // A user: subject with every flag off delivers ONLY if the tag is
            // unmapped (fail-open). A mapped tag suppresses. So this doubles as
            // a behavioural read of "is this tag known", not a re-implementation
            // of the lookup.
            if (policy.ShouldDeliverAsync("user:1", tag).GetAwaiter().GetResult())
            {
                unmapped.Add($"'{tag}' (shipped by {source})");
            }
        }

        Assert.True(unmapped.Count == 0,
            "These consumer type tags are not in NotificationPreferencePolicy's table, "
            + "so they fail OPEN and reach users who muted the matching category: "
            + string.Join("; ", unmapped)
            + ". Add a row, or fix the consumer's spelling.");
    }

    /// <summary>Counting guard for the same reason: if the consumers directory
    /// ever stops being scannable, the test above would pass on an empty set
    /// and this one would notice.</summary>
    [Fact]
    public void ConsumerTagScan_FindsTheExpectedNumberOfTags()
    {
        var shipped = ShippedTags();
        var distinct = shipped.Select(t => t.Tag).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.NotEmpty(shipped);
        Assert.NotEmpty(distinct);

        // 15 consumers ship 15 literals today, but only 12 are DISTINCT: the
        // three customer.* consumers all ship "customer", and nothing ships
        // "promotion"/"promotions" — the policy maps those two so that the day
        // a promotions producer ships it is already gated, which is the
        // documented intent, not an oversight. So this count tracks the
        // CONSUMERS, not the policy table (14 rows), and the two are expected
        // to differ by exactly the two unmapped-until-needed tags.
        Assert.True(distinct.Count >= 12,
            $"expected at least 12 distinct consumer type tags, found "
            + $"{distinct.Count}: {string.Join(", ", distinct)}");
    }

    /// <summary>
    /// A store whose every flag is off. Used to probe whether a tag is mapped:
    /// with nothing enabled, a mapped tag suppresses and only an unmapped one
    /// falls through to fail-open. Inherits the real defaults path by never
    /// being asked for a document it does not have.
    /// </summary>
    private sealed class AlwaysMutedStore : INotificationPreferencesStore
    {
        public Task<NotificationPreferencesDto> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(new NotificationPreferencesDto
            {
                EmailNotifications = false,
                SmsNotifications = false,
                InAppNotifications = true,   // channel open, so the TYPE table is what decides
                Orders = false,
                Deliveries = false,
                Payments = false,
                SystemAlerts = false,
                Promotions = false,
            });

        public Task<NotificationPreferencesDto> PutAsync(string key, NotificationPreferencesDto prefs, CancellationToken ct = default)
            => Task.FromResult(prefs);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;
using Vivnest.Cloud.Services;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Tests;

// Sprint 8's throttle is the reason the feature was blocked before it was
// built: without it, a crash-looping worker turns one fault into hundreds
// of identical notifications. These tests are the argument that it works,
// because the failure mode only shows up under load in production - by
// which point you have already been paged fifty times.
public class AgentAlertThrottleTests
{
    private const string RuntimeAgentId = "agent-1";
    private static readonly SiteScope Scope = new("tenant-1", "site-1");
    private static readonly DateTime T0 = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeStore : IAgentAlertStateStore
    {
        private readonly Dictionary<string, AgentAlertStateEntity> _rows = new(StringComparer.Ordinal);

        public Task<AgentAlertStateEntity?> GetAsync(
            string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.TryGetValue($"{partitionKey}|{rowKey}", out var row) ? row : null);

        public Task UpsertAsync(AgentAlertStateEntity entity, CancellationToken cancellationToken = default)
        {
            // Azure Tables refuses any DateTime whose Kind is not Utc:
            // "DateTime ... has a Kind of Unspecified. Azure SDK requires
            // it to be UTC." This fake originally accepted anything, so
            // every test here passed while every real write threw - each
            // row type sets only one of the two DateTime fields, and the
            // other defaulted to 0001-01-01 Unspecified.
            //
            // A fake that is more permissive than the thing it stands in
            // for will hide exactly the bugs it was written to catch, so
            // this one now enforces the same rule.
            AssertUtc(entity.LastNotifiedUtc, nameof(entity.LastNotifiedUtc));
            AssertUtc(entity.WindowStartedUtc, nameof(entity.WindowStartedUtc));

            _rows[$"{entity.PartitionKey}|{entity.RowKey}"] = entity;
            return Task.CompletedTask;
        }

        private static void AssertUtc(DateTime value, string field) =>
            Assert.True(
                value.Kind == DateTimeKind.Utc,
                $"{field} has Kind {value.Kind} ({value:o}). Azure Tables requires DateTimeKind.Utc "
                + "and will throw NotSupportedException on write.");
    }

    private static AgentAlertThrottle Build(
        FakeStore store, int ceiling = 12, int cooldownMinutes = 10) =>
        new(store,
            Options.Create(new OperationalAlertOptions
            {
                Enabled = true,
                CooldownPerSignature = TimeSpan.FromMinutes(cooldownMinutes),
                MaxNotificationsPerAgentPerHour = ceiling
            }),
            NullLogger<AgentAlertThrottle>.Instance);

    private static Task<bool> Notify(
        AgentAlertThrottle throttle, string signature, DateTime at) =>
        throttle.ShouldNotifyAsync(Scope, RuntimeAgentId, signature, "boom", at);

    // ---- the signature ---------------------------------------------------

    // The crash-loop case that motivated all of this. If per-occurrence
    // numbers made every repetition a new signature, the cooldown would
    // never engage and the throttle would be decorative.
    [Fact]
    public void MessagesDifferingOnlyByNumbersOrIdsShareASignature()
    {
        var a = AgentAlertThrottle.ComputeSignature("Cam", "capture 41 failed after 3000ms");
        var b = AgentAlertThrottle.ComputeSignature("Cam", "capture 42 failed after 4500ms");

        Assert.Equal(a, b);
    }

    [Fact]
    public void GuidsAndTimestampsAreNormalisedOutToo()
    {
        var a = AgentAlertThrottle.ComputeSignature(
            "Cam", "device 55cc8aa6-dc4f-4cfc-a71d-36a46935650e failed at 2026-08-20T12:00:00Z");
        var b = AgentAlertThrottle.ComputeSignature(
            "Cam", "device 4b3e8c1f-9a72-4e8b-82c1-3f0e8f7a92b3 failed at 2026-08-19T04:59:42Z");

        Assert.Equal(a, b);
    }

    // ...but genuinely different faults must stay distinguishable, or the
    // normalisation has gone too far and real errors get masked.
    [Fact]
    public void GenuinelyDifferentFaultsGetDifferentSignatures()
    {
        var rtsp = AgentAlertThrottle.ComputeSignature("Cam", "RTSP connect timed out");
        var disk = AgentAlertThrottle.ComputeSignature("Cam", "disk full writing snapshot");
        var other = AgentAlertThrottle.ComputeSignature("Uploader", "RTSP connect timed out");

        Assert.NotEqual(rtsp, disk);
        Assert.NotEqual(rtsp, other); // same message, different category
    }

    // Persisted as a RowKey, so it must be stable across processes -
    // string.GetHashCode is randomised per process and would silently break
    // dedup on every restart.
    [Fact]
    public void SignaturesAreStableAndRowKeySafe()
    {
        var a = AgentAlertThrottle.ComputeSignature("Cam", "RTSP connect timed out");
        var b = AgentAlertThrottle.ComputeSignature("Cam", "RTSP connect timed out");

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{16}$", a);
    }

    // ---- gate 1: the per-signature cooldown ------------------------------

    [Fact]
    public async Task TheFirstOccurrenceOfASignatureNotifies()
    {
        var throttle = Build(new FakeStore());

        Assert.True(await Notify(throttle, "sig-a", T0));
    }

    [Fact]
    public async Task ARepeatWithinTheCooldownIsSuppressed()
    {
        var throttle = Build(new FakeStore());

        Assert.True(await Notify(throttle, "sig-a", T0));
        Assert.False(await Notify(throttle, "sig-a", T0.AddMinutes(1)));
        Assert.False(await Notify(throttle, "sig-a", T0.AddMinutes(9)));
    }

    [Fact]
    public async Task TheSameSignatureNotifiesAgainOnceTheCooldownExpires()
    {
        var throttle = Build(new FakeStore());

        Assert.True(await Notify(throttle, "sig-a", T0));
        Assert.True(await Notify(throttle, "sig-a", T0.AddMinutes(10)));
    }

    // The reason the cooldown is keyed on signature rather than on agent: a
    // noisy subsystem must not silence a different, possibly worse fault on
    // the same agent.
    [Fact]
    public async Task ADifferentSignatureIsNotBlockedByAnotherOnesCooldown()
    {
        var throttle = Build(new FakeStore());

        Assert.True(await Notify(throttle, "sig-a", T0));
        Assert.False(await Notify(throttle, "sig-a", T0.AddMinutes(1)));
        Assert.True(await Notify(throttle, "sig-b", T0.AddMinutes(1)));
    }

    // ---- gate 2: the per-agent hourly ceiling ----------------------------

    // What the cooldown structurally cannot catch: every error distinct, so
    // every one passes gate 1. Without the ceiling this still floods.
    [Fact]
    public async Task ManyDistinctSignaturesAreCappedByTheHourlyCeiling()
    {
        var throttle = Build(new FakeStore(), ceiling: 5);

        var allowed = 0;

        for (var i = 0; i < 50; i++)
        {
            if (await Notify(throttle, $"sig-{i}", T0.AddSeconds(i)))
                allowed++;
        }

        Assert.Equal(5, allowed);
    }

    [Fact]
    public async Task TheCeilingWindowResetsAfterAnHour()
    {
        var throttle = Build(new FakeStore(), ceiling: 2);

        Assert.True(await Notify(throttle, "sig-a", T0));
        Assert.True(await Notify(throttle, "sig-b", T0));
        Assert.False(await Notify(throttle, "sig-c", T0.AddMinutes(30)));

        Assert.True(await Notify(throttle, "sig-d", T0.AddHours(1)));
    }

    // Suppressed duplicates must not consume the budget, or a crash loop
    // would exhaust the hour's allowance without ever notifying anyone.
    [Fact]
    public async Task SuppressedDuplicatesDoNotConsumeTheCeiling()
    {
        var store = new FakeStore();
        var throttle = Build(store, ceiling: 3);

        Assert.True(await Notify(throttle, "sig-a", T0));

        for (var i = 1; i <= 20; i++)
            Assert.False(await Notify(throttle, "sig-a", T0.AddSeconds(i)));

        // Two of the three still available, despite 20 suppressed repeats.
        Assert.True(await Notify(throttle, "sig-b", T0.AddMinutes(1)));
        Assert.True(await Notify(throttle, "sig-c", T0.AddMinutes(2)));
        Assert.False(await Notify(throttle, "sig-d", T0.AddMinutes(3)));
    }

    // Throttling is per agent - one failing agent must not silence another.
    [Fact]
    public async Task TheCeilingIsPerAgentNotGlobal()
    {
        var store = new FakeStore();
        var throttle = Build(store, ceiling: 1);

        Assert.True(await throttle.ShouldNotifyAsync(Scope, "agent-1", "sig-a", "boom", T0));
        Assert.False(await throttle.ShouldNotifyAsync(Scope, "agent-1", "sig-b", "boom", T0));

        Assert.True(await throttle.ShouldNotifyAsync(Scope, "agent-2", "sig-a", "boom", T0));
    }
}

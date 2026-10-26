using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Cloud.Options;

namespace Vivnest.Tests;

// ADR-129. The phone push channel carries the alerts that must not be
// missed, so these pin the parts that would fail quietly: which
// notification types go to phones at all, that a dead token is removed
// instead of failing forever, and that a delivery failure never escapes
// into the dispatcher (which would retry and re-send to Telegram).
public class PushNotificationTests
{
    // ---- routing ----------------------------------------------------------

    [Theory]
    [InlineData(NotificationTypes.FireAlarmDetected, "fire-alarm", true)]
    [InlineData(NotificationTypes.DeviceOffline, "alerts", true)]
    [InlineData(NotificationTypes.AgentOffline, "alerts", true)]
    [InlineData(NotificationTypes.DeviceRecovered, "info", false)]
    [InlineData(NotificationTypes.AgentRecovered, "info", false)]
    public async Task TheRightTypesAreRoutedToTheRightAndroidChannel(string type, string channel, bool loud)
    {
        var sender = new RecordingSender();

        await Channel(sender, Phone("a")).SendAsync(Note(type));

        var sent = Assert.Single(sender.Alerts);
        Assert.Equal(channel, sent.ChannelId);
        Assert.Equal(loud, sent.HighPriority);
        Assert.Equal(type, sent.Data!["type"]);
    }

    [Theory]
    [InlineData(NotificationTypes.MotionDetected)]
    [InlineData(NotificationTypes.SinkCleanliness)]
    [InlineData(NotificationTypes.SmartPlugPowerStateChanged)]
    [InlineData(NotificationTypes.CameraCaptured)]
    public async Task EverythingElseStaysOffThePhone(string type)
    {
        var sender = new RecordingSender();

        await Channel(sender, Phone("a")).SendAsync(Note(type));

        Assert.Empty(sender.Alerts);
    }

    [Fact]
    public async Task DisabledPhonesAreSkipped()
    {
        var sender = new RecordingSender();
        var off = Phone("a");
        off.Enabled = false;

        await Channel(sender, off).SendAsync(Note(NotificationTypes.FireAlarmDetected));

        Assert.Empty(sender.Alerts);
    }

    [Fact]
    public async Task AFailingPhoneListDoesNotEscapeTheChannel()
    {
        var sender = new RecordingSender();
        var channel = new ExpoPushNotificationChannel(
            new ThrowingStore(), sender, NullLogger<ExpoPushNotificationChannel>.Instance);

        await channel.SendAsync(Note(NotificationTypes.FireAlarmDetected));

        Assert.Empty(sender.Alerts);
    }

    // ---- sender -----------------------------------------------------------

    [Fact]
    public async Task TheRequestCarriesTheChannelPriorityAndTtl()
    {
        var handler = new FakeExpo("{\"data\":[{\"status\":\"ok\"}]}");

        var accepted = await Sender(handler, new InMemoryStore()).SendAsync(
            [Phone("a")],
            new PushAlert("Fire", "Beeping", "fire-alarm", true, TimeSpan.FromHours(1)));

        Assert.Equal(1, accepted);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var message = doc.RootElement[0];
        Assert.Equal("ExponentPushToken[token-a]", message.GetProperty("to").GetString());
        Assert.Equal("fire-alarm", message.GetProperty("channelId").GetString());
        Assert.Equal("high", message.GetProperty("priority").GetString());
        Assert.Equal(3600, message.GetProperty("ttl").GetInt32());
    }

    [Fact]
    public async Task ADeadTokenIsRemovedAndTheOthersStillCount()
    {
        var store = new InMemoryStore();
        var dead = Phone("dead");
        var live = Phone("live");
        store.Rows.AddRange([dead, live]);

        var handler = new FakeExpo(
            "{\"data\":[{\"status\":\"error\",\"message\":\"gone\",\"details\":{\"error\":\"DeviceNotRegistered\"}},{\"status\":\"ok\"}]}");

        var accepted = await Sender(handler, store).SendAsync(
            [dead, live],
            new PushAlert("Fire", "x", "fire-alarm", true, TimeSpan.FromHours(1)));

        Assert.Equal(1, accepted);
        Assert.DoesNotContain(store.Rows, r => r.RowKey == dead.RowKey);
        Assert.Contains(store.Rows, r => r.RowKey == live.RowKey);
    }

    [Fact]
    public async Task AnExpoOutageIsLoggedNotThrown()
    {
        var sender = Sender(new FakeExpo("oops", HttpStatusCode.BadGateway), new InMemoryStore());

        var accepted = await sender.SendAsync(
            [Phone("a")],
            new PushAlert("Fire", "x", "fire-alarm", true, TimeSpan.FromHours(1)));

        Assert.Equal(0, accepted);
    }

    [Fact]
    public async Task ANetworkFailureIsLoggedNotThrown()
    {
        var sender = Sender(new FakeExpo(throwOnSend: true), new InMemoryStore());

        var accepted = await sender.SendAsync(
            [Phone("a")],
            new PushAlert("Fire", "x", "fire-alarm", true, TimeSpan.FromHours(1)));

        Assert.Equal(0, accepted);
    }

    [Fact]
    public async Task DisabledOptionSendsNothing()
    {
        var handler = new FakeExpo("{\"data\":[]}");
        var sender = new ExpoPushSender(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new ExpoPushOptions { Enabled = false }),
            new InMemoryStore(),
            NullLogger<ExpoPushSender>.Instance);

        var accepted = await sender.SendAsync(
            [Phone("a")],
            new PushAlert("Fire", "x", "fire-alarm", true, TimeSpan.FromHours(1)));

        Assert.Equal(0, accepted);
        Assert.Null(handler.LastBody);
    }

    // ---- ids and tokens ---------------------------------------------------

    [Fact]
    public void TheSameTokenAlwaysGivesTheSameRowId()
    {
        Assert.Equal(
            PushDeviceIds.ForToken("ExponentPushToken[abc]"),
            PushDeviceIds.ForToken("  ExponentPushToken[abc]  "));

        Assert.NotEqual(
            PushDeviceIds.ForToken("ExponentPushToken[abc]"),
            PushDeviceIds.ForToken("ExponentPushToken[abd]"));
    }

    [Theory]
    [InlineData("ExponentPushToken[abc123]", true)]
    [InlineData("ExpoPushToken[abc123]", true)]
    [InlineData("abc123", false)]
    [InlineData("ExponentPushToken[abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyExpoShapedTokensAreAccepted(string? token, bool expected) =>
        Assert.Equal(expected, PushDeviceIds.LooksLikeExpoToken(token));

    // ---- helpers ----------------------------------------------------------

    private static ExpoPushNotificationChannel Channel(RecordingSender sender, params PushDeviceEntity[] phones)
    {
        var store = new InMemoryStore();
        store.Rows.AddRange(phones);

        return new ExpoPushNotificationChannel(store, sender, NullLogger<ExpoPushNotificationChannel>.Instance);
    }

    private static ExpoPushSender Sender(FakeExpo handler, InMemoryStore store) =>
        new(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new ExpoPushOptions()),
            store,
            NullLogger<ExpoPushSender>.Instance);

    private static Notification Note(string type) =>
        new() { Type = type, Title = "Title", Message = "Message" };

    private static PushDeviceEntity Phone(string id) =>
        new()
        {
            TenantId = "t",
            SiteId = "s",
            PartitionKey = "t|s",
            RowKey = id,
            ExpoPushToken = "ExponentPushToken[token-" + id + "]",
            Name = "Phone " + id,
        };

    private sealed class RecordingSender : IPushAlertSender
    {
        public List<PushAlert> Alerts { get; } = [];

        public Task<int> SendAsync(
            IReadOnlyCollection<PushDeviceEntity> devices, PushAlert alert, CancellationToken cancellationToken = default)
        {
            Alerts.Add(alert);
            return Task.FromResult(devices.Count);
        }
    }

    private sealed class InMemoryStore : IPushDeviceStore
    {
        public List<PushDeviceEntity> Rows { get; } = [];

        public Task<IReadOnlyList<PushDeviceEntity>> ListAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PushDeviceEntity>>(Rows.ToList());

        public Task<IReadOnlyList<PushDeviceEntity>> ListAsync(string tenantId, string siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PushDeviceEntity>>(Rows.Where(r => r.TenantId == tenantId && r.SiteId == siteId).ToList());

        public Task<PushDeviceEntity?> GetAsync(string tenantId, string siteId, string pushDeviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.SiteId == siteId && r.RowKey == pushDeviceId));

        public Task UpsertAsync(PushDeviceEntity entity, CancellationToken cancellationToken = default)
        {
            Rows.RemoveAll(r => r.RowKey == entity.RowKey);
            Rows.Add(entity);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string tenantId, string siteId, string pushDeviceId, CancellationToken cancellationToken = default)
        {
            Rows.RemoveAll(r => r.RowKey == pushDeviceId);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStore : IPushDeviceStore
    {
        public Task<IReadOnlyList<PushDeviceEntity>> ListAllAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("table unavailable");

        public Task<IReadOnlyList<PushDeviceEntity>> ListAsync(string tenantId, string siteId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PushDeviceEntity?> GetAsync(string tenantId, string siteId, string pushDeviceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpsertAsync(PushDeviceEntity entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string tenantId, string siteId, string pushDeviceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeExpo : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;
        private readonly bool _throw;

        public FakeExpo(string body = "", HttpStatusCode status = HttpStatusCode.OK, bool throwOnSend = false)
        {
            _body = body;
            _status = status;
            _throw = throwOnSend;
        }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throw)
                throw new HttpRequestException("network down");

            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}

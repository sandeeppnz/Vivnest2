using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Domain.Sites;

namespace Vivnest.Cloud.Functions.Http;

// The phone app registers itself here (ADR-129). Tenant x-api-key via
// ApiFunctionBase, any role: the app only ever holds the key its owner
// typed in, and registering a phone grants nothing the key did not have.
// Routed as "push-devices" - no reserved "admin/" segment.
public class PushDevicesFunction : ApiFunctionBase
{
    private static readonly string[] TestChannels =
    [
        ExpoPushNotificationChannel.FireAlarmChannel,
        ExpoPushNotificationChannel.AlertsChannel,
        ExpoPushNotificationChannel.InfoChannel,
    ];

    private readonly IPushDeviceStore _devices;
    private readonly IPushAlertSender _sender;

    public PushDevicesFunction(
        IApiKeyAuthenticator authenticator,
        IPushDeviceStore devices,
        IPushAlertSender sender)
        : base(authenticator)
    {
        _devices = devices;
        _sender = sender;
    }

    [Function(nameof(ListPushDevices))]
    public async Task<IActionResult> ListPushDevices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "push-devices")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var phones = await _devices.ListAsync(tenant.TenantId, tenant.SiteId, cancellationToken);

        return new OkObjectResult(phones.Select(ToDto).ToList());
    }

    // Idempotent on the token: the app calls this on every launch, and a
    // phone that re-registers updates its one row rather than adding a
    // second (which would send every alert twice).
    [Function(nameof(RegisterPushDevice))]
    public async Task<IActionResult> RegisterPushDevice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "push-devices")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        RegisterPushDeviceRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<RegisterPushDeviceRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || !PushDeviceIds.LooksLikeExpoToken(body.ExpoPushToken))
            return new BadRequestObjectResult("ExpoPushToken must look like ExponentPushToken[...].");

        var token = body.ExpoPushToken.Trim();
        var id = PushDeviceIds.ForToken(token);
        var now = DateTime.UtcNow;

        var existing = await _devices.GetAsync(tenant.TenantId, tenant.SiteId, id, cancellationToken);

        var entity = new PushDeviceEntity
        {
            TenantId = tenant.TenantId,
            SiteId = tenant.SiteId,
            PartitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey,
            RowKey = id,
            ExpoPushToken = token,
            Name = string.IsNullOrWhiteSpace(body.Name) ? (existing?.Name ?? "Phone") : body.Name.Trim(),
            Platform = string.IsNullOrWhiteSpace(body.Platform) ? (existing?.Platform ?? "android") : body.Platform.Trim().ToLowerInvariant(),
            // A re-registration never silently re-enables a phone that was
            // deliberately switched off.
            Enabled = existing?.Enabled ?? true,
            CreatedUtc = existing?.CreatedUtc ?? now,
            LastSeenUtc = now,
        };

        await _devices.UpsertAsync(entity, cancellationToken);

        return new OkObjectResult(ToDto(entity));
    }

    [Function(nameof(DeletePushDevice))]
    public async Task<IActionResult> DeletePushDevice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "push-devices/{pushDeviceId}")]
            HttpRequest request,
        string pushDeviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var existing = await _devices.GetAsync(tenant.TenantId, tenant.SiteId, pushDeviceId, cancellationToken);

        if (existing == null)
            return new NotFoundResult();

        await _devices.DeleteAsync(tenant.TenantId, tenant.SiteId, pushDeviceId, cancellationToken);

        return new NoContentResult();
    }

    // Sends to THIS tenant's phones only, straight through the sender - not
    // through the notification dispatcher, which would also message Telegram.
    // Defaults to the fire-alarm channel because that is the one worth
    // proving rings through Do Not Disturb.
    [Function(nameof(SendTestPush))]
    public async Task<IActionResult> SendTestPush(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "push-devices/test")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        SendTestPushRequest? body = null;

        try
        {
            if (request.ContentLength is > 0)
                body = await request.ReadFromJsonAsync<SendTestPushRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        var channel = string.IsNullOrWhiteSpace(body?.Channel)
            ? ExpoPushNotificationChannel.FireAlarmChannel
            : body!.Channel!.Trim();

        if (!TestChannels.Contains(channel, StringComparer.Ordinal))
            return new BadRequestObjectResult($"Channel must be one of: {string.Join(", ", TestChannels)}.");

        var phones = (await _devices.ListAsync(tenant.TenantId, tenant.SiteId, cancellationToken))
            .Where(d => d.Enabled)
            .ToList();

        var loud = channel != ExpoPushNotificationChannel.InfoChannel;

        var accepted = await _sender.SendAsync(
            phones,
            new PushAlert(
                Title: "Vivnest test alert",
                Body: "If this rang through Do Not Disturb, fire alarms will too.",
                ChannelId: channel,
                HighPriority: loud,
                TimeToLive: TimeSpan.FromMinutes(10),
                Data: new Dictionary<string, string> { ["type"] = "Test" }),
            cancellationToken);

        return new OkObjectResult(new SendTestPushResult(phones.Count, accepted));
    }

    private static PushDeviceDto ToDto(PushDeviceEntity entity) =>
        new(entity.RowKey, entity.Name, entity.Platform, entity.Enabled, entity.CreatedUtc, entity.LastSeenUtc);
}

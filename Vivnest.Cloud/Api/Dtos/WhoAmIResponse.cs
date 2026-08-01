namespace Vivnest.Cloud.Api.Dtos;

public sealed record WhoAmIResponse(string TenantId, string SiteId, bool DevicesOnly);

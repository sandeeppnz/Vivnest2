namespace Vivnest.Cloud.Api.Dtos;

public sealed record CaptureDaySummaryDto(
    DateOnly Date,
    int Count);

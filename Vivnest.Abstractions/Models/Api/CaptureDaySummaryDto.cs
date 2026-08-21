namespace Vivnest.Abstractions.Models.Api;

public sealed record CaptureDaySummaryDto(
    DateOnly Date,
    int Count);

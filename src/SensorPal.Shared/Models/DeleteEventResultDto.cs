namespace SensorPal.Shared.Models;

public sealed record DeleteEventResultDto(
    long SessionId,
    bool SessionNowEmpty,
    bool SessionHasBackground);

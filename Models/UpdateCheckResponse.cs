using System;

namespace MSCS.Models;

public sealed record UpdateCheckResponse(UpdateCheckOutcome Outcome, UpdateCheckResult? Update)
{
    public static UpdateCheckResponse Available(UpdateCheckResult update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        return new UpdateCheckResponse(UpdateCheckOutcome.UpdateAvailable, update);
    }

    public static UpdateCheckResponse UpToDate() => new(UpdateCheckOutcome.UpToDate, null);

    public static UpdateCheckResponse Failed() => new(UpdateCheckOutcome.Failed, null);

    public static UpdateCheckResponse Disabled() => new(UpdateCheckOutcome.Disabled, null);
}

public enum UpdateCheckOutcome
{
    UpdateAvailable,
    UpToDate,
    Failed,
    Disabled
}
namespace PEMS.Licensing.Seal;

public sealed record ClockEvaluation(DateTimeOffset EffectiveNow, bool BackwardJump);

/// <summary>
/// Clock-tampering guard (design 4.5). All term, trial and demo checks use <see cref="ClockEvaluation.EffectiveNow"/>,
/// the greatest of the system clock and every trusted earlier instant, so setting the clock back cannot extend a term.
/// </summary>
public static class ClockGuard
{
    public static ClockEvaluation Evaluate(DateTimeOffset systemNow, DateTimeOffset lastSeenUtc, DateTimeOffset? newestEventUtc, TimeSpan tolerance)
    {
        var floor = newestEventUtc.HasValue && newestEventUtc.Value > lastSeenUtc ? newestEventUtc.Value : lastSeenUtc;
        var backward = systemNow < floor - tolerance;
        return new ClockEvaluation(systemNow > floor ? systemNow : floor, backward);
    }
}

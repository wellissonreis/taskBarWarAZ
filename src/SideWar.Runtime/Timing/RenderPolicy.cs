using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Timing;

public static class RenderPolicy
{
    /// <summary>Times and deadlines are seconds. RenderNow requests composition; the consumer skips unchanged frames.</summary>
    public static RenderDecision Evaluate(RenderInputs input)
    {
        if (!input.Visible || input.Presence == PresenceState.Offline)
            return new(false, null);

        if (input.ActiveFps is not (20 or 30 or 60))
            throw new ArgumentOutOfRangeException(nameof(input), "ActiveFps must be 20, 30 or 60.");
        if (input.AbsentFps is not (4 or 8))
            throw new ArgumentOutOfRangeException(nameof(input), "AbsentFps must be 4 or 8.");
        if (!input.SimRunning)
            return new(input.Dirty, null);

        int fps = input.Presence == PresenceState.Ausente ? input.AbsentFps : input.ActiveFps;
        if (input.Profile == PowerProfile.Saver)
            fps = Math.Min(fps, LoopConstants.SaverActiveFps);
        double deadline = input.LastPresent + 1.0 / fps;
        return new(input.Dirty || input.Now >= deadline - LoopConstants.RenderToleranceSeconds, deadline);
    }
}

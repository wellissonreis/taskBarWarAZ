using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Timing;

public readonly record struct RenderInputs(
    bool Visible, bool SimRunning, bool AnimationsEnabled, PresenceState Presence,
    bool Dirty, PowerProfile Profile, double Now, double LastPresent,
    int ActiveFps = LoopConstants.DefaultActiveFps, int AbsentFps = LoopConstants.AmbientFps);

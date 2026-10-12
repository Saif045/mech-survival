using System;

namespace MechSurvival.Player;

/// <summary>Initial playable balance: canonical GAS Mobility to horizontal speed.</summary>
public static class MobilityMovementPolicy
{
    public const float SpeedPerMobility = 3;

    public static float Speed(float mobility, bool hasMobility)
    {
        if (!float.IsFinite(mobility) || mobility < 0 || mobility > float.MaxValue / SpeedPerMobility)
            throw new ArgumentOutOfRangeException(nameof(mobility));
        return hasMobility ? mobility * SpeedPerMobility : 0;
    }
}

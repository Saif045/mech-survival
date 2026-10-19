namespace MechSurvival.Player;

public enum MechServiceIntent { Install, Detach }

/// <summary>Intent never changes when carry state changes between click and receipt.</summary>
public static class MechServiceIntentPolicy
{
    public static bool Allows(MechServiceIntent intent, bool carriedPart, bool installedPart, bool compatible) => intent switch
    {
        MechServiceIntent.Install => carriedPart && compatible,
        MechServiceIntent.Detach => installedPart,
        _ => false
    };
}

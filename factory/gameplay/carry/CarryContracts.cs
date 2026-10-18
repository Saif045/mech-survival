using System;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Gameplay.Carry;

/// <summary>Carry-only identity and server transitions, without inventory semantics.</summary>
public interface ICarryTarget
{
    NetworkObject NetworkObject { get; }
    long HolderNetworkObjectId { get; }
    bool IsAvailable { get; }
    bool TryAttach(NetworkObject holder);
    bool TryRelease(NetworkObject holder, Transform3D worldTransform);
}

/// <summary>One-item holder and explicit local presentation anchor.</summary>
public interface ICarryHolder
{
    NetworkObject NetworkObject { get; }
    Node3D CarryAnchor { get; }
    bool HasCarriedItem { get; }
    bool TryPickup(ICarryTarget item);
}

/// <summary>Actor policy: canonical on server, side-effect-free hint locally.</summary>
public interface ICarryPermission
{
    bool CanPickup(NetworkObject actor);
    bool CanDrop(NetworkObject actor);
}

/// <summary>Focused host/direct-child composition; no recursive or global lookup.</summary>
public static class CarryCapabilities
{
    public static ICarryTarget? Target(Node host) => Direct<ICarryTarget>(host);
    public static ICarryHolder? Holder(Node host) => Direct<ICarryHolder>(host);
    private static T? Direct<T>(Node host) where T : class
    {
        if (host is T root) return root;
        T? result = null;
        foreach (Node child in host.GetChildren())
        {
            if (child is not T capability) continue;
            if (result is not null) throw new InvalidOperationException($"Multiple direct {typeof(T).Name} capabilities on {host.Name}.");
            result = capability;
        }
        return result;
    }
}

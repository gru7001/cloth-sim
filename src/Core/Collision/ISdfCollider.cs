using Godot;

namespace DelaunyFabric.Core;

/// <summary>Signed distance: 0 on surface, positive outside, negative inside. Normal points outward.</summary>
public interface ISdfCollider
{
    void Sample(Vector3 position, out float signedDistance, out Vector3 normal);
}

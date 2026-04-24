using Godot;

namespace CowColonySim.UI.Selection;

/// <summary>
/// Converts a world-space ray (from the camera through a mouse click) into
/// the best <see cref="SelectionTarget"/> that provider knows about.
/// <see cref="SelectionController"/> asks every registered provider and keeps
/// whichever hit is closest to the camera.
///
/// Implement once per kind of selectable thing (trees, colonists, walls,
/// items, etc.). Providers are free to consult whatever state they need —
/// ECS, TileWorld, Godot scene tree — as long as they stay side-effect free.
/// </summary>
public interface ISelectionProvider
{
    /// <summary>Return true and fill <paramref name="target"/> +
    /// <paramref name="distance"/> if this provider found a hit along the
    /// ray. <paramref name="distance"/> is the parametric t along
    /// <paramref name="rayDirection"/> from <paramref name="rayOrigin"/>;
    /// the controller uses it to break ties across providers.</summary>
    bool TryPick(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        out SelectionTarget target,
        out float distance);
}

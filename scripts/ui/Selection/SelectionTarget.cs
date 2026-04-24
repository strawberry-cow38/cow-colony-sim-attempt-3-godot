using System.Collections.Generic;
using Godot;

namespace CowColonySim.UI.Selection;

/// <summary>
/// Snapshot of a picked thing — tree, colonist, wall, anything that advertises
/// itself through an <see cref="ISelectionProvider"/>. The framework treats
/// every target the same: draw the wire box at <see cref="Bounds"/>, show
/// <see cref="Name"/> + <see cref="Description"/> in the side panel, render
/// <see cref="Actions"/> as buttons.
///
/// <see cref="Payload"/> is opaque to the framework. Providers stash the
/// underlying ECS entity / tile / etc. here so action callbacks can reach
/// them without reflection.
/// </summary>
public sealed record SelectionTarget(
    string Name,
    string Description,
    Aabb Bounds,
    IReadOnlyList<SelectionAction> Actions,
    object? Payload = null);

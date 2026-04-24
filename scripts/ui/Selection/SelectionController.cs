using System.Collections.Generic;
using Godot;

namespace CowColonySim.UI.Selection;

/// <summary>
/// Root of the selection framework. Owns the list of <see cref="ISelectionProvider"/>s,
/// listens for LMB/RMB/Escape, and broadcasts <see cref="SelectionChanged"/>
/// whenever the current target changes so the ghost + panel can redraw.
///
/// Providers self-register in <see cref="_Ready"/> so downstream scenes don't
/// have to touch node paths. Adding a new selectable kind = one new class
/// plus a line in RegisterBuiltinProviders.
/// </summary>
public partial class SelectionController : Node
{
    [Signal] public delegate void SelectionChangedEventHandler();

    private readonly List<ISelectionProvider> _providers = new();
    private SelectionTarget? _current;
    private Vector2? _lastPickScreen;

    public SelectionTarget? Current => _current;

    /// <summary>Re-run providers at the last pick position. Used after an
    /// action mutates the picked entity so the panel's snapshot description
    /// (growth %, chop-mark) can refresh without the user re-clicking.</summary>
    public void Refresh()
    {
        if (_lastPickScreen.HasValue) Pick(_lastPickScreen.Value);
    }

    public override void _Ready()
    {
        RegisterBuiltinProviders();
    }

    private void RegisterBuiltinProviders()
    {
        var sim = GetNodeOrNull<SimHost>("/root/SimHost");
        if (sim != null) _providers.Add(new TreeSelectionProvider(sim));
    }

    public void Register(ISelectionProvider provider) => _providers.Add(provider);

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _lastPickScreen = mb.Position;
                Pick(mb.Position);
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Right && _current != null)
            {
                Clear();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape && _current != null)
        {
            Clear();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Pick(Vector2 screen)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var origin = cam.ProjectRayOrigin(screen);
        var dir = cam.ProjectRayNormal(screen);

        SelectionTarget? best = null;
        var bestDist = float.MaxValue;
        foreach (var p in _providers)
        {
            if (!p.TryPick(origin, dir, out var t, out var d)) continue;
            if (d >= bestDist) continue;
            best = t;
            bestDist = d;
        }
        Set(best);
    }

    public void Clear()
    {
        _lastPickScreen = null;
        Set(null);
    }

    private void Set(SelectionTarget? t)
    {
        // Always fire even for same-target clicks — growth/state may have
        // advanced and panel text is a snapshot at pick time.
        _current = t;
        EmitSignal(SignalName.SelectionChanged);
    }
}

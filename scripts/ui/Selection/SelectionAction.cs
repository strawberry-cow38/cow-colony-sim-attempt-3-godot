using System;

namespace CowColonySim.UI.Selection;

/// <summary>
/// One entry in a <see cref="SelectionTarget"/>'s action list. <see cref="Label"/>
/// drives the button text; <see cref="Invoke"/> runs when pressed. Providers
/// build these at pick time — actions can close over the picked entity so
/// callbacks run against the correct target without the framework having to
/// know about entity types.
/// </summary>
public readonly record struct SelectionAction(string Label, Action Invoke);

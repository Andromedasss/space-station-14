namespace Content.Shared.Body.Events;

/// <summary>
/// Raised after a bleed-rate change, including clotting and medical treatment.
/// </summary>
[ByRefEvent]
public readonly record struct BleedAmountChangedEvent(float Previous, float Current);

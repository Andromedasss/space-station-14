namespace Content.Shared.Damage;

/// <summary>
/// Final local validation and healing cap before aggregate damage is changed.
/// </summary>
[ByRefEvent]
public record struct LocalizedDamageAttemptEvent(DamageSpecifier Damage, EntityUid? Part, bool Cancelled = false);

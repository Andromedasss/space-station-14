using Content.Shared.Damage;

namespace Content.Shared.Medical;

[ByRefEvent]
public record struct LocalizedHealingQueryEvent(EntityUid Part, DamageSpecifier Damage, float BleedingModifier,
    bool Handled = false, bool CanHeal = false);

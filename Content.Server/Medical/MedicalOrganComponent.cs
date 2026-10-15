using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

public enum MedicalOrganKind : byte
{
    Heart,
    Lungs,
}

/// <summary>
/// Functional capacity stays on the organ when it is removed or transplanted.
/// Structural trauma is independent of the debug functional-capacity setting and external wound healing.
/// </summary>
[RegisterComponent]
public sealed partial class MedicalOrganComponent : Component
{
    [DataField(required: true)]
    public MedicalOrganKind Kind;

    [DataField]
    public float Function = 1f;

    [DataField]
    public float Trauma;

    [DataField]
    public string ProtectedBy = "Torso";

    /// <summary>
    /// Applied damage below this per-hit threshold does not reach the internal organ.
    /// </summary>
    [DataField]
    public float TraumaThreshold = 20f;

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> TraumaPerDamage = new()
    {
        ["Blunt"] = 0.5f,
        ["Slash"] = 0.25f,
        ["Piercing"] = 1f,
    };

    public float EffectiveFunction => Math.Clamp(Function, 0f, 1f) * (1f - Math.Clamp(Trauma / 100f, 0f, 1f));
}

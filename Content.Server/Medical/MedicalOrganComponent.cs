using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

public enum MedicalOrganKind : byte
{
    Heart,
    Lungs,
    Kidney,
    Liver,
    Intestines,
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
    public bool Fibrillating;

    [DataField]
    public bool Perforated;

    [DataField]
    public string ProtectedBy = "Torso";

    /// <summary>
    /// Tissue protection threshold, cumulative for blunt trauma and per-hit for cutting/penetrating trauma.
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

    public float EffectiveFunction => Fibrillating ? 0f
        : Math.Clamp(Function, 0f, 1f) * (1f - Math.Clamp(Trauma / 100f, 0f, 1f));
}

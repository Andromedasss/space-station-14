using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

/// <summary>
/// Experimental tissue injury aggregated by damage type on a physical external organ.
/// Bleeding is independent of tissue repair: a dressing does not erase the injury.
/// </summary>
[RegisterComponent]
public sealed partial class MedicalWoundComponent : Component
{
    [DataField]
    public DamageSpecifier Injury = new();

    [DataField]
    public float Bleeding;

    [DataField]
    public int SurgicalAccess;

    // 0: intact, 1: sawn, 2: spread. Separate from traumatic fractures.
    [DataField]
    public int BoneAccess;

    [DataField]
    public bool RequiresBoneAccess;

    public bool InternalsExposed => SurgicalAccess == 3 && (!RequiresBoneAccess || BoneAccess == 2);

    public int SurgeryRevision;

    /// <summary>
    /// Tissue damage left on the attached parent when this part is removed.
    /// </summary>
    [DataField]
    public float DetachmentDamage = 20f;

    /// <summary>
    /// Local structural injury at which this part provides no limb function.
    /// </summary>
    [DataField]
    public float SupportDamage = 100f;

    [DataField]
    public bool Fractured;

    [DataField]
    public float FractureThreshold = 60f;

    /// <summary>
    /// Deep vascular injury. External dressings and clotting do not repair it.
    /// </summary>
    [DataField]
    public float VascularDamage;

    [DataField]
    public float VascularThreshold = 40f;

    [DataField]
    public float CrushVascularThreshold = 120f;

    [DataField]
    public float CrushSkinThreshold = 160f;

    [DataField]
    public float CrushVascularTransfer = 0.5f;

    [DataField]
    public float CrushBleedingTransfer = 0.05f;

    [DataField]
    public float InternalBleedingPerDamage = 0.02f;

    [DataField]
    public float IschemiaDebt;

    [DataField]
    public bool Necrotic;

    [DataField]
    public bool Prosthetic;

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> BleedingPerDamage = new()
    {
        ["Blunt"] = 0f,
        ["Slash"] = 0.2f,
        ["Piercing"] = 0.3f,
        ["Heat"] = 0f,
    };
}

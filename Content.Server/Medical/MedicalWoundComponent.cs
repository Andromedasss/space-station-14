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

    /// <summary>
    /// Tissue damage left on the attached parent when this part is removed.
    /// </summary>
    [DataField]
    public float DetachmentDamage = 20f;

    /// <summary>
    /// Local tissue injury at which this part provides no locomotor support.
    /// Only legs and feet participate in the first mobility model.
    /// </summary>
    [DataField]
    public float SupportDamage = 100f;

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> BleedingPerDamage = new()
    {
        ["Blunt"] = 0f,
        ["Slash"] = 0.2f,
        ["Piercing"] = 0.3f,
        ["Heat"] = 0f,
    };
}

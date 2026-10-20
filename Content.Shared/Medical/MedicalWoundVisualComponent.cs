using Robust.Shared.GameStates;

namespace Content.Shared.Medical;

/// <summary>
/// Client-replicated visualization data for a single body part wound.
/// The server mirrors <c>MedicalWoundComponent</c> into this component so the
/// client can drive the per-part damage overlay layers independently instead
/// of lighting up the whole body at once.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MedicalWoundVisualComponent : Component
{
    /// <summary>
    /// Total injury on this body part, used to pick the damage overlay threshold.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TotalInjury;

    /// <summary>
    /// Id of the dominant damage group for this part (e.g. "Brute", "Burn").
    /// Selects which per-part overlay layer is used.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string DominantGroup = "Brute";

    /// <summary>
    /// Whether this part has a fracture.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Fractured;

    /// <summary>
    /// Whether this part is necrotic.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Necrotic;
}

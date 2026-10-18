using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical;

/// <summary>
/// Client-replicated visualization data for <see cref="MedicalWoundComponent"/>.
/// Contains only the fields needed to draw wound overlays on body parts.
/// Synced from server to client so the client can render per-part wounds.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class MedicalWoundVisualComponent : Component
{
    /// <summary>
    /// Total injury across all damage types on this body part (0-100+).
    /// Used to scale wound overlay opacity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TotalInjury;

    /// <summary>
    /// Current bleeding rate on this part.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Bleeding;

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

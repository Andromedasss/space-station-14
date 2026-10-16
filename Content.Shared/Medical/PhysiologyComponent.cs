using Robust.Shared.GameStates;

namespace Content.Shared.Medical;

/// <summary>
/// Opts a test patient into server-authoritative physiology instead of damage thresholds.
/// Not intended for arbitrary mobs: the patient must have a bloodstream, respirator and medical organs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PhysiologyComponent : Component
{
    /// <summary>
    /// Server-derived support from the experimental patient's legs and feet.
    /// Replicated so client movement prediction applies the same modifier.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Mobility = 1f;

    [DataField, AutoNetworkedField]
    public float LeftHandFunction = 1f;

    [DataField, AutoNetworkedField]
    public float RightHandFunction = 1f;

    /// <summary>Rounded perceived pain for patient feedback and predicted sprint speed.</summary>
    [DataField, AutoNetworkedField]
    public int Pain;

    [DataField, AutoNetworkedField]
    public float TemperatureSpeed = 1f;
}

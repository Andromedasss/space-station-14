using Robust.Shared.GameStates;

namespace Content.Shared.Medical;

/// <summary>
/// Enables server-authoritative physiology for a humanoid mob instead of damage thresholds.
/// Requires a bloodstream, respirator and medical organs on the mob.
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

    // Derived from the attached physical brain; never the source of persistent pathology.
    [DataField, AutoNetworkedField]
    public MedicalColorVision NeurologicalVision;

    [DataField, AutoNetworkedField]
    public bool Aphasia;

    [DataField, AutoNetworkedField]
    public bool MuscleWeakness;

    [DataField, AutoNetworkedField] public MedicalCognitiveSymptoms CognitiveSymptoms;
    [DataField, AutoNetworkedField] public MedicalPhobia Phobia;
    [DataField, AutoNetworkedField] public int Fear;

    /// <summary>
    /// Blood group of the patient. Decided once and only revealed by a blood test.
    /// </summary>
    [DataField, AutoNetworkedField]
    public BloodGroup BloodGroup = BloodGroup.ONegative;
}

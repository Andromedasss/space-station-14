using Content.Shared.Medical;

namespace Content.Server.Medical;

/// <summary>
/// Injury and irreversible death belong to the physical brain, never the player session.
/// Only administrative rejuvenation can reset irreversible injury in this initial version.
/// </summary>
[RegisterComponent]
public sealed partial class CerebralHealthComponent : Component
{
    [DataField]
    public float OxygenDebt;

    [DataField]
    public float Damage;

    // Fractional new injury carries over between hits/frames. Scanning never rolls symptoms.
    [DataField]
    public float NeurologicalInjuryRemainder;

    [DataField]
    public MedicalColorVision ColorVision;

    [DataField]
    public bool Aphasia;

    [DataField]
    public bool MuscleWeakness;

    [DataField] public MedicalCognitiveSymptoms CognitiveSymptoms;
    [DataField] public MedicalPhobia Phobia;
    [DataField] public float Fear;
    [DataField] public float FearUnconsciousSeconds;
    [DataField] public float FearRecoverySeconds;
    // An active symptom without an entry is persistent, including previously saved brains.
    [DataField] public Dictionary<MedicalNeurologicalCondition, float> RecoveryRemaining = new();
    public float FearSampleSeconds;
    public int VisibleFearTriggers;
}

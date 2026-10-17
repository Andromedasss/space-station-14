using Content.Shared.Medical;
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// Read-only experimental medical observations. No admin history or intervention controls.
/// </summary>
[Serializable, NetSerializable]
public sealed class PhysiologyScan
{
    public bool Detailed;
    public bool InternalWarning;
    public bool NeurologicalWarning;
    public List<WoundHygieneScan> WoundHygiene = new();
    public float HeartFunction;
    public float KidneyFunction;
    public float LiverFunction;
    public float IntestinalFunction;
    public float RenalWaste;
    public float HepaticWaste;
    public float Contamination;
    public float MetabolicDemand;
    public float Pulse;
    public float Systolic;
    public float Diastolic;
    public float CirculatoryFunction;
    public bool LifeSupport;
    public float LifeSupportGasMoles;
    public bool CprActive;
    public bool ShockableRhythm;
    public float InotropeDose;
    public float LungFunction;
    public float OxygenDelivery;
    public float OxygenDebt;
    public float BrainDamage;
    public bool BrainMissing;
    public MedicalColorVision NeurologicalVision;
    public bool Aphasia;
    public bool MuscleWeakness;
    public MedicalCognitiveSymptoms CognitiveSymptoms;
    public MedicalPhobia Phobia;
    public float Fear;
    public float NeurorestorativeDose;
    public float AnxiolyticDose;
    public List<NeurologicalConditionScan> NeurologicalConditions = new();
    public float Mobility;
    public float PainBurden;
    public float Pain;
    public float AnalgesicDose;
    public float SedativeDose;
    public float RespiratoryDrive;
    public bool Sedated;
    public List<string> AttachedParts = new();
    public List<LocalizedInjuryScan> Injuries = new();
    public List<InternalOrganInjuryScan> OrganInjuries = new();
    public List<InternalOrganInjuryScan> StorageInjuries = new();
    public List<SurgicalAccessScan> SurgicalSites = new();
    public List<SurgicalAccessScan> BoneSites = new();
    public List<InternalOrganInjuryScan> OrganFunctions = new();
    public List<string> Fractures = new();
    public List<VascularInjuryScan> VascularInjuries = new();
    public List<string> NecroticParts = new();
    public List<string> ProstheticParts = new();
}

[Serializable, NetSerializable]
public sealed record LocalizedInjuryScan(string Category, float Damage, float Bleeding);

[Serializable, NetSerializable]
public sealed record InternalOrganInjuryScan(string Category, float Trauma);

[Serializable, NetSerializable]
public sealed record SurgicalAccessScan(string Category, int Stage);

[Serializable, NetSerializable]
public sealed record VascularInjuryScan(string Category, float Damage, float InternalBleeding);

[Serializable, NetSerializable]
public sealed record NeurologicalConditionScan(MedicalNeurologicalCondition Condition, float RecoverySeconds);

[Serializable, NetSerializable]
public sealed record WoundHygieneScan(string Category, float Contamination, float Infection, float ProtectionSeconds);

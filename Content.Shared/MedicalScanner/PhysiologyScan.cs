using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// Read-only experimental medical observations. No admin history or intervention controls.
/// </summary>
[Serializable, NetSerializable]
public sealed class PhysiologyScan
{
    public float HeartFunction;
    public float CirculatoryFunction;
    public bool LifeSupport;
    public bool CprActive;
    public bool ShockableRhythm;
    public float InotropeDose;
    public float LungFunction;
    public float OxygenDelivery;
    public float OxygenDebt;
    public float BrainDamage;
    public bool BrainMissing;
    public float Mobility;
    public List<LocalizedInjuryScan> Injuries = new();
    public List<InternalOrganInjuryScan> OrganInjuries = new();
    public List<SurgicalAccessScan> SurgicalSites = new();
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

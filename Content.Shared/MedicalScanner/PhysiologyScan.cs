using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// Read-only experimental medical observations. No admin history or intervention controls.
/// </summary>
[Serializable, NetSerializable]
public sealed class PhysiologyScan
{
    public float HeartFunction;
    public float LungFunction;
    public float OxygenDelivery;
    public float OxygenDebt;
    public float BrainDamage;
    public bool BrainMissing;
    public List<LocalizedInjuryScan> Injuries = new();
}

[Serializable, NetSerializable]
public sealed record LocalizedInjuryScan(string Category, float Damage, float Bleeding);

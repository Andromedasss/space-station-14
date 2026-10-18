using Content.Shared.MedicalScanner;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

/// <summary>
/// Request from client to server to modify medical state of a target entity.
/// </summary>
[Serializable, NetSerializable]
public sealed class MedicalDebugRequestMessage : EntityEventArgs
{
    public enum Action : byte
    {
        SetOrganFunction,
        InflictWound,
        SetBlood,
        SetBleeding,
        SetTemperature,
        SetAphasia,
        SetWeakness,
        SetColorVision,
        SetCognitiveSymptoms,
        SetPhobia,
        ResetNeurology,
        ResetAll,
    }

    public NetEntity TargetEntity;
    public Action ActionType;
    public string StringParam1 = string.Empty;   // organ kind / body part
    public string StringParam2 = string.Empty;   // damage type
    public float FloatParam1;                     // value / amount / temperature
    public int IntParam1;                         // cognitive bitmask / color vision enum
    public bool BoolParam1;                       // true/false
}

/// <summary>
/// Response from server to client with current physiology state of the target.
/// </summary>
[Serializable, NetSerializable]
public sealed class MedicalDebugStateMessage : EntityEventArgs
{
    public PhysiologyScan? Physiology;
    public float Temperature;
    public float BloodLevel;
    public string? Error;
}

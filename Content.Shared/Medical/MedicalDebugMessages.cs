using Content.Shared.MedicalScanner;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

/// <summary>
/// Client to server request to inspect or change the experimental physiology of an entity.
/// </summary>
[Serializable, NetSerializable]
public sealed class MedicalDebugRequestMessage : EntityEventArgs
{
    public enum Action : byte
    {
        /// <summary>Only re-send the current state, no side effects.</summary>
        Refresh,
        /// <summary>Apply a physiology intervention, see <c>PhysiologySystem.TryIntervene</c>.</summary>
        Intervene,
        /// <summary>Apply a localized wound, see <c>MedicalWoundSystem.TryInjure</c>.</summary>
        InflictWound,
        /// <summary>Clear every neurological symptom.</summary>
        ResetNeurology,
        /// <summary>Restore organ function and clear neurological symptoms.</summary>
        ResetAll,
    }

    public NetEntity TargetEntity;
    public Action ActionType;
    /// <summary>Organ kind, body part category or intervention name.</summary>
    public string StringParam1 = string.Empty;
    /// <summary>Damage type for wounds.</summary>
    public string StringParam2 = string.Empty;
    public float FloatParam1;
}

/// <summary>
/// Server to client snapshot of the experimental physiology of a target entity.
/// </summary>
[Serializable, NetSerializable]
public sealed class MedicalDebugStateMessage : EntityEventArgs
{
    public NetEntity TargetEntity;
    public string TargetName = string.Empty;
    public bool Valid;
    public string? Error;
    public float Temperature;
    public float BloodLevel;
    public bool Bleeding;
    public bool Unrevivable;
    public PhysiologyScan? Scan;
}

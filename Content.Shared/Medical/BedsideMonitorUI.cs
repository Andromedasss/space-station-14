using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

[Serializable, NetSerializable]
public enum BedsideMonitorUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum BedsideMonitorAlarm : byte
{
    None,
    Hypoxia,
    Shock,
    Asystole,
    Fibrillation
}

[Serializable, NetSerializable]
public sealed class BedsideMonitorUiState : BoundUserInterfaceState
{
    public bool Powered;
    public NetEntity? Patient;
    public string PatientName = string.Empty;
    public float Pulse;
    public float Systolic;
    public float Diastolic;
    public float OxygenDelivery;
    public BedsideMonitorAlarm Alarm;
}

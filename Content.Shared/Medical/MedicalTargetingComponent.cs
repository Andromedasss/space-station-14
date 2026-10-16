using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical;

/// <summary>
/// Explicit per-user selection. Only experimental targets consume this setting.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MedicalTargetingComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Category = "Torso";
}

[Serializable, NetSerializable]
public sealed class MedicalTargetZoneEvent(string category) : EntityEventArgs
{
    public string Category = category;
}

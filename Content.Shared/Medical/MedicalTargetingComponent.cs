using Robust.Shared.GameStates;

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

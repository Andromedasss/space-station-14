using Content.Shared.Medical;

namespace Content.Server.Medical;

[RegisterComponent]
public sealed partial class MedicalSurgeryToolComponent : Component
{
    [DataField(required: true)]
    public MedicalSurgeryAction Action;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(4);

    [DataField]
    public bool Sterile = true;
}

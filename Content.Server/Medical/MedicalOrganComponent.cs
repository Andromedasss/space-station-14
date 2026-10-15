namespace Content.Server.Medical;

public enum MedicalOrganKind : byte
{
    Heart,
    Lungs,
}

/// <summary>
/// Functional capacity stays on the organ when it is removed or transplanted.
/// Initial version is manipulated through the medical debugging command, not combat damage.
/// </summary>
[RegisterComponent]
public sealed partial class MedicalOrganComponent : Component
{
    [DataField(required: true)]
    public MedicalOrganKind Kind;

    [DataField]
    public float Function = 1f;
}

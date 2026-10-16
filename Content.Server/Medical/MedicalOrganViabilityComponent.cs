namespace Content.Server.Medical;

/// <summary>Irreversible storage injury on an experimental heart or lungs, separate from repairable trauma.</summary>
[RegisterComponent]
public sealed partial class MedicalOrganViabilityComponent : Component
{
    [DataField]
    public float Damage;
}

[RegisterComponent]
public sealed partial class MedicalOrganPreservationComponent : Component
{
    [DataField]
    public float RemainingSeconds = 600f;
}

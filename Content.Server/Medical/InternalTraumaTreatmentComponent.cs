namespace Content.Server.Medical;

/// <summary>
/// Explicit experimental treatment; ordinary medical supplies never repair internal trauma.
/// </summary>
[RegisterComponent]
public sealed partial class InternalTraumaTreatmentComponent : Component
{
    [DataField]
    public float Repair = 25f;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(8);
}

[RegisterComponent]
public sealed partial class MedicalOperatingTableComponent : Component;

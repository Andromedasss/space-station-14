using Content.Shared.Atmos;

namespace Content.Server.Medical;

/// <summary>
/// Powered support for the patient physically strapped to this bed. Gas is finite and consumed by breathing.
/// </summary>
[RegisterComponent]
public sealed partial class MedicalLifeSupportComponent : Component
{
    [DataField]
    public GasMixture GasSupply = new();

    [DataField]
    public float PumpFunction = 0.8f;
}

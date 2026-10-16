using Content.Shared.Atmos;

namespace Content.Server.Medical;

[RegisterComponent]
public sealed partial class MedicalVentilationComponent : Component
{
    [DataField]
    public GasMixture GasSupply = new();
}

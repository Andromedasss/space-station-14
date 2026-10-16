using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Medical;

namespace Content.Server.Medical;

/// <summary>
/// Bloodstream-derived anesthesia, never a persistent override or treatment for cerebral injury.
/// </summary>
public sealed class MedicalSedationSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public float Dose(EntityUid patient)
    {
        return HasComp<PhysiologyComponent>(patient) && TryComp<BloodstreamComponent>(patient, out var blood)
            && _solutions.TryGetSolution(patient, blood.BloodSolutionName, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity("MedicalTestSedative").Float() : 0f;
    }

    public bool IsSedated(EntityUid patient) => Dose(patient) >= PhysiologyModel.AnestheticDose;

    public float RespiratoryDrive(EntityUid patient) => PhysiologyModel.SedativeRespiratoryDrive(Dose(patient));
}

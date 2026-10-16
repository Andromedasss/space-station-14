using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Medical;
using Content.Shared.Movement.Systems;

namespace Content.Server.Medical;

/// <summary>
/// Derives pain from attached tissue, never from aggregate damage or a lasting drug override.
/// Ordinary patients and mechanical parts retain their existing behavior.
/// </summary>
public sealed class MedicalPainSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private MedicalSedationSystem _sedation = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<PhysiologyComponent, BodyComponent>();
        while (query.MoveNext(out var uid, out var physiology, out _))
        {
            var sample = Sample(uid);
            var perceived = (int) MathF.Round(sample.Pain);
            if (physiology.Pain == perceived)
                continue;
            physiology.Pain = perceived;
            Dirty(uid, physiology);
            _movement.RefreshMovementSpeedModifiers(uid);
        }
    }

    public (float Burden, float Pain, float Dose) Sample(EntityUid patient)
    {
        if (!HasComp<PhysiologyComponent>(patient))
            return (0f, 0f, 0f);

        var burden = 0f;
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(patient))
            burden += PhysiologyModel.PartPain(part.Comp2.Injury.GetTotal().Float(), part.Comp2.Fractured,
                part.Comp2.SurgicalAccess, part.Comp2.IschemiaDebt, part.Comp2.Prosthetic || part.Comp2.Necrotic)
                + (part.Comp2.BoneAccess > 0 && !part.Comp2.Prosthetic && !part.Comp2.Necrotic ? 15f : 0f);
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(patient))
            burden += Math.Clamp(organ.Comp2.Trauma, 0f, 100f) * 0.25f;
        burden = Math.Clamp(burden, 0f, 100f);

        var dose = TryComp<BloodstreamComponent>(patient, out var blood)
            && _solutions.TryGetSolution(patient, blood.BloodSolutionName, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity("MedicalTestAnalgesic").Float() : 0f;
        var conscious = false;
        foreach (var brain in _body.EnumerateOrgans<CerebralHealthComponent>(patient))
        {
            conscious = brain.Comp2.Damage < PhysiologyModel.UnconsciousBrainDamage
                && brain.Comp2.OxygenDebt < PhysiologyModel.UnconsciousDebt && !_sedation.IsSedated(patient);
            break;
        }
        return (burden, conscious ? PhysiologyModel.PerceivedPain(burden, dose) : 0f, dose);
    }
}

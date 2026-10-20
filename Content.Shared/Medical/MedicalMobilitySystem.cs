using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Medical;

public sealed class MedicalMobilitySystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysiologyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<PhysiologyComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<PhysiologyComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnRefreshSpeed(Entity<PhysiologyComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Mobility);
        args.ModifySpeed(ent.Comp.TemperatureSpeed);
        if (ent.Comp.MuscleWeakness)
            args.ModifySpeed(NeurologicalTrauma.WeaknessSpeed);
        args.ModifySpeed(1f, PhysiologyModel.PainSprintModifier(ent.Comp.Pain));
    }
}

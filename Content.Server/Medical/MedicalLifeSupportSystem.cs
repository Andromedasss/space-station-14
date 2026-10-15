using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Content.Shared.Buckle.Components;
using Content.Shared.Medical;

namespace Content.Server.Medical;

public sealed class MedicalLifeSupportSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysiologyComponent, InhaleLocationEvent>(OnInhale);
    }

    public MedicalLifeSupportComponent? GetSupport(EntityUid patient)
    {
        if (!HasComp<PhysiologyComponent>(patient)
            || !TryComp<BuckleComponent>(patient, out var buckle) || buckle.BuckledTo is not { } bed
            || TerminatingOrDeleted(bed) || !TryComp<MedicalLifeSupportComponent>(bed, out var support)
            || !TryComp<ApcPowerReceiverComponent>(bed, out var power) || !power.Powered || power.PowerDisabled)
            return null;
        return support;
    }

    private void OnInhale(Entity<PhysiologyComponent> ent, ref InhaleLocationEvent args)
    {
        if (GetSupport(ent) is not { } support)
            return;

        // Use the same regulated, finite gas transfer as a breathing tank. Never synthesize oxygen reserve.
        var gas = _atmos.RemoveVolumeAtPressure(support.GasSupply, args.Respirator.BreathVolume,
            Atmospherics.OneAtmosphere);
        gas.Volume = args.Respirator.BreathVolume;
        args.Gas = gas;
    }
}

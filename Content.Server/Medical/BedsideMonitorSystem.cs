using Content.Server.Power.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Medical;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Medical;

public sealed class BedsideMonitorSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PhysiologySystem _physiology = default!;
    [Dependency] private IGameTiming _timing = default!;

    private float _elapsed;

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<BedsideMonitorComponent>(BedsideMonitorUiKey.Key,
            subs => subs.Event<BoundUIOpenedEvent>(OnOpened));
    }

    private void OnOpened(Entity<BedsideMonitorComponent> ent, ref BoundUIOpenedEvent args)
    {
        _ui.SetUiState(ent.Owner, BedsideMonitorUiKey.Key, GetMonitorState(ent.Owner));
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 1f)
            return;
        _elapsed = 0f;

        var query = EntityQueryEnumerator<BedsideMonitorComponent>();
        while (query.MoveNext(out var uid, out var monitor))
        {
            var state = GetMonitorState(uid);
            _ui.SetUiState(uid, BedsideMonitorUiKey.Key, state);
            if (!state.Powered || state.Patient == null || state.Alarm == BedsideMonitorAlarm.None)
            {
                monitor.NextAlarm = TimeSpan.Zero;
                continue;
            }

            if (_timing.CurTime < monitor.NextAlarm)
                continue;
            monitor.NextAlarm = _timing.CurTime + TimeSpan.FromSeconds(5);
            _audio.PlayPvs(monitor.AlarmSound, uid);
        }
    }

    private bool CanMonitor(EntityUid uid, EntityUid patient, float range)
    {
        return !TerminatingOrDeleted(patient) && HasComp<PhysiologyComponent>(patient)
            && TryComp<BuckleComponent>(patient, out var buckle) && buckle.BuckledTo is { } bed
            && !TerminatingOrDeleted(bed)
            && _transform.InRange(Transform(uid).Coordinates, Transform(patient).Coordinates, range);
    }

    public BedsideMonitorUiState GetMonitorState(EntityUid uid)
    {
        var result = new BedsideMonitorUiState();
        if (!TryComp<BedsideMonitorComponent>(uid, out var monitor)
            || !Transform(uid).Anchored
            || !TryComp<ApcPowerReceiverComponent>(uid, out var power) || !power.Powered || power.PowerDisabled)
            return result;

        result.Powered = true;
        if (monitor.Patient is not { } previous || !CanMonitor(uid, previous, monitor.Range))
        {
            monitor.Patient = null;
            var closest = float.MaxValue;
            var query = EntityQueryEnumerator<PhysiologyComponent, BuckleComponent>();
            while (query.MoveNext(out var patient, out _, out _))
            {
                if (!CanMonitor(uid, patient, monitor.Range))
                    continue;
                var distance = (_transform.GetWorldPosition(uid) - _transform.GetWorldPosition(patient)).LengthSquared();
                if (distance >= closest)
                    continue;
                closest = distance;
                monitor.Patient = patient;
            }
        }

        if (monitor.Patient is not { } target || _physiology.Scan(target) is not { } scan)
            return result;

        result.Patient = GetNetEntity(target);
        result.PatientName = Name(target);
        result.Pulse = scan.Pulse;
        result.Systolic = scan.Systolic;
        result.Diastolic = scan.Diastolic;
        result.OxygenDelivery = scan.OxygenDelivery;
        // Absence of a pulse alone cannot distinguish fibrillation from asystole.
        result.Alarm = scan.ShockableRhythm ? BedsideMonitorAlarm.Fibrillation
            : scan.Pulse <= 0f ? BedsideMonitorAlarm.Asystole
            : scan.Systolic < 90f ? BedsideMonitorAlarm.Shock
            : scan.OxygenDelivery < 0.6f ? BedsideMonitorAlarm.Hypoxia
            : BedsideMonitorAlarm.None;
        return result;
    }
}

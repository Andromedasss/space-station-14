using Content.Shared.Body;
using Content.Shared.Examine;
using Content.Shared.Medical;
using Content.Shared.Rejuvenate;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Server.Medical;

public sealed class MedicalOrganViabilitySystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MedicalOrganViabilityComponent, RejuvenateEvent>((uid, comp, args) => comp.Damage = 0f);
        SubscribeLocalEvent<MedicalOrganViabilityComponent, ExaminedEvent>((uid, comp, args) =>
            args.PushMarkup(Loc.GetString("medical-organ-viability", ("damage", comp.Damage.ToString("F1")))));
        SubscribeLocalEvent<CerebralHealthComponent, ExaminedEvent>((uid, comp, args) =>
            args.PushMarkup(Loc.GetString("medical-brain-storage", ("debt", comp.OxygenDebt.ToString("F1")), ("damage", comp.Damage.ToString("F1")))));
        SubscribeLocalEvent<MedicalOrganPreservationComponent, ExaminedEvent>((uid, comp, args) =>
            args.PushMarkup(Loc.GetString("medical-organ-preservation", ("seconds", comp.RemainingSeconds.ToString("F0")))));
    }

    public bool Supported(EntityUid uid)
    {
        return TryComp<OrganComponent>(uid, out var organ) && organ.Body is { } host
            && HasComp<PhysiologyComponent>(host) && HasComp<PhysiologyStateComponent>(host)
            && HasComp<BodyComponent>(host);
    }

    // Only actual containment in the dedicated storage compartment preserves tissue, not carrying the case.
    public float ExposureSeconds(EntityUid uid, float seconds)
    {
        var current = uid;
        for (var depth = 0; depth < 16 && _containers.TryGetContainingContainer(current, out var container); depth++)
        {
            if (container.ID == StorageComponent.ContainerId && TryComp<MedicalOrganPreservationComponent>(container.Owner, out var preservation))
                return Math.Min(seconds, Math.Max(0f, preservation.RemainingSeconds)) * 0.1f
                    + Math.Max(0f, seconds - Math.Max(0f, preservation.RemainingSeconds));
            current = container.Owner;
        }
        return seconds;
    }

    public float Function(EntityUid uid) => TryComp<MedicalOrganViabilityComponent>(uid, out var viability)
        ? 1f - Math.Clamp(viability.Damage / 100f, 0f, 1f) : 1f;

    public bool Viable(EntityUid uid) => (!TryComp<MedicalOrganViabilityComponent>(uid, out var viability) || viability.Damage < 100f)
        && (!TryComp<CerebralHealthComponent>(uid, out var brain) || brain.Damage < PhysiologyModel.MaximumBrainDamage);

    public override void Update(float frameTime)
    {
        var organs = EntityQueryEnumerator<MedicalOrganViabilityComponent, OrganComponent>();
        while (organs.MoveNext(out var uid, out var viability, out _))
            if (!Supported(uid))
                viability.Damage = Math.Clamp(viability.Damage + ExposureSeconds(uid, frameTime) * 0.1f, 0f, 100f);
        var cases = EntityQueryEnumerator<MedicalOrganPreservationComponent>();
        while (cases.MoveNext(out _, out var preservation))
            preservation.RemainingSeconds = Math.Max(0f, preservation.RemainingSeconds - frameTime);
    }
}

using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

/// <summary>
/// First localized injury slice. Unlocated attacks use the torso, never a fabricated click location.
/// Bloodstream remains responsible for spilling blood, clotting, alerts and treatment.
/// </summary>
public sealed class MedicalWoundSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private PhysiologySystem _physiology = default!;
    [Dependency] private OrganRelationSystem _relations = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysiologyComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<PhysiologyComponent, BleedAmountChangedEvent>(OnBleedChanged);
        SubscribeLocalEvent<PhysiologyComponent, OrganInsertedIntoEvent>(OnInserted);
        SubscribeLocalEvent<PhysiologyComponent, OrganRemovedFromEvent>(OnRemoved);
        SubscribeLocalEvent<PhysiologyStateComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<MedicalWoundComponent, RejuvenateEvent>(OnPartRejuvenate);
        SubscribeLocalEvent<PhysiologyComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<PhysiologyComponent> ent, ref MapInitEvent args)
    {
        Synchronize(ent);
    }

    private void OnDamageChanged(Entity<PhysiologyComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null)
            return;

        var part = FindPart(ent, args.TargetPart, "Torso");
        foreach (var (type, amount) in args.DamageDelta.DamageDict)
        {
            if (amount > 0 && part is { } hit && hit.Comp2.BleedingPerDamage.TryGetValue(type, out var coefficient))
            {
                hit.Comp2.Injury.DamageDict[type] = hit.Comp2.Injury.DamageDict.GetValueOrDefault(type) + amount;
                if (TryComp<BloodstreamComponent>(ent, out var blood))
                    hit.Comp2.Bleeding += Math.Min(Math.Max(0f, blood.MaxBleedAmount - TotalBleeding(ent)),
                        Math.Max(0f, amount.Float() * coefficient));
                if (TryComp<PhysiologyStateComponent>(ent, out var state))
                    _physiology.Record(state, $"WOUND {hit.Comp1.Category}: {type} +{amount}; source={args.Origin}");
                foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(ent.Owner))
                {
                    var medical = organ.Comp2;
                    if (medical.ProtectedBy != hit.Comp1.Category?.Id
                        || !medical.TraumaPerDamage.TryGetValue(type, out var transfer)
                        || !float.IsFinite(transfer) || transfer <= 0f
                        || !float.IsFinite(medical.TraumaThreshold) || medical.TraumaThreshold < 0f)
                        continue;

                    var trauma = Math.Max(0f, amount.Float() - medical.TraumaThreshold) * transfer;
                    var previous = medical.Trauma;
                    medical.Trauma = Math.Clamp(previous + trauma, 0f, 100f);
                    if (medical.Trauma != previous && TryComp<PhysiologyStateComponent>(ent, out var history))
                        _physiology.Record(history, $"ORGAN {medical.Kind}: trauma {previous:F2} -> {medical.Trauma:F2}");
                }
            }
            else if (amount < 0)
            {
                // Upstream healing has no location. Distribute actual repaired damage, not requested healing.
                var total = FixedPoint2.Zero;
                foreach (var organ in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
                    total += organ.Comp2.Injury.DamageDict.GetValueOrDefault(type);
                var remaining = FixedPoint2.Min(-amount, total);
                foreach (var organ in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
                {
                    var injury = organ.Comp2.Injury.DamageDict.GetValueOrDefault(type);
                    if (injury <= 0 || total <= 0)
                        continue;
                    var repaired = FixedPoint2.FromCents((int)((long)remaining.Value * injury.Value / total.Value));
                    organ.Comp2.Injury.DamageDict[type] = injury - repaired;
                    remaining -= repaired;
                    total -= injury;
                }
            }
        }
        Synchronize(ent);
    }

    private Entity<OrganComponent, MedicalWoundComponent>? FindPart(EntityUid uid, EntityUid? target, string category)
    {
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(uid))
        {
            if (target != null ? part.Owner == target : part.Comp1.Category?.Id == category)
                return part;
        }
        return null;
    }

    private float TotalBleeding(EntityUid uid)
    {
        var total = 0f;
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(uid))
            total += part.Comp2.Bleeding;
        return total;
    }

    private void Synchronize(EntityUid uid)
    {
        if (TryComp<PhysiologyComponent>(uid, out var physiology))
        {
            var leftLeg = 0f;
            var rightLeg = 0f;
            var leftFoot = 0f;
            var rightFoot = 0f;
            foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(uid))
            {
                var support = float.IsFinite(part.Comp2.SupportDamage) && part.Comp2.SupportDamage > 0f
                    ? Math.Clamp(1f - part.Comp2.Injury.GetTotal().Float() / part.Comp2.SupportDamage, 0f, 1f)
                    : 0f;
                switch (part.Comp1.Category?.Id)
                {
                    case "LegLeft": leftLeg = Math.Max(leftLeg, support); break;
                    case "LegRight": rightLeg = Math.Max(rightLeg, support); break;
                    case "FootLeft": leftFoot = Math.Max(leftFoot, support); break;
                    case "FootRight": rightFoot = Math.Max(rightFoot, support); break;
                }
            }

            // Each side needs both a leg and a foot. Keep a limited fallback for dragging oneself.
            var mobility = 0.2f + 0.4f * (Math.Min(leftLeg, leftFoot) + Math.Min(rightLeg, rightFoot));
            if (physiology.Mobility != mobility)
            {
                physiology.Mobility = mobility;
                Dirty(uid, physiology);
            }
            _movement.RefreshMovementSpeedModifiers(uid);
        }

        if (!TryComp<BloodstreamComponent>(uid, out var blood))
            return;

        var total = TotalBleeding(uid);
        if (total > blood.MaxBleedAmount)
        {
            // Insertion can exceed the cap even when bloodstream is already capped and raises no change event.
            var ratio = blood.MaxBleedAmount / total;
            foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(uid))
                part.Comp2.Bleeding *= ratio;
            total = blood.MaxBleedAmount;
        }
        _blood.TryModifyBleedAmount((uid, blood), total - blood.BleedAmount);
    }

    private void OnBleedChanged(Entity<PhysiologyComponent> ent, ref BleedAmountChangedEvent args)
    {
        var total = TotalBleeding(ent);
        if (args.Current < total && total > 0f)
        {
            // Persist clotting/bandaging on the wounds so bleeding is not restored next tick.
            var ratio = args.Current / total;
            foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
                part.Comp2.Bleeding *= ratio;
        }
        else if (args.Current > total && FindPart(ent, null, "Torso") is { } torso)
        {
            // Legacy chemical/debug increases lack a location, just like unlocated attacks.
            torso.Comp2.Bleeding += args.Current - total;
        }
    }

    private void OnInserted(Entity<PhysiologyComponent> ent, ref OrganInsertedIntoEvent args)
    {
        if (TryComp<MedicalWoundComponent>(args.Organ, out var wound))
            TransferAggregateDamage(ent, wound.Injury);
        Synchronize(ent);
    }

    private void OnRemoved(Entity<PhysiologyComponent> ent, ref OrganRemovedFromEvent args)
    {
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent)
            || !TryComp<BodyComponent>(ent, out var body) || body.LifeStage != ComponentLifeStage.Running)
            return;

        if (TryComp<MedicalWoundComponent>(args.Organ, out var wound))
        {
            TransferAggregateDamage(ent, -wound.Injury);
            foreach (var parent in _relations.AllParents(args.Organ))
            {
                if (!TryComp<OrganComponent>(parent, out var organ) || organ.Body != ent.Owner
                    || !HasComp<MedicalWoundComponent>(parent) || TerminatingOrDeleted(parent))
                    continue;

                if (float.IsFinite(wound.DetachmentDamage) && wound.DetachmentDamage > 0f)
                {
                    var damage = new DamageSpecifier { DamageDict = { ["Slash"] = FixedPoint2.New(wound.DetachmentDamage) } };
                    // Severing tissue bypasses worn armor; insertion alone cannot close this wound.
                    _damage.ChangeDamage(ent.Owner, damage, ignoreResistances: true,
                        ignoreGlobalModifiers: true, targetPart: parent.Owner);
                    if (TryComp<PhysiologyStateComponent>(ent, out var state))
                        _physiology.Record(state, $"DETACH {Comp<OrganComponent>(args.Organ).Category}: stump on {organ.Category}");
                }
                break;
            }
        }
        Synchronize(ent);
    }

    private void TransferAggregateDamage(EntityUid host, DamageSpecifier change)
    {
        if (change.Empty)
            return;

        // Moving existing tissue is not a new attack or treatment: do not generate another local wound.
        var damage = _damage.GetAllDamage(host);
        foreach (var (type, amount) in change.DamageDict)
            damage.DamageDict[type] = FixedPoint2.Max(FixedPoint2.Zero, damage.DamageDict.GetValueOrDefault(type) + amount);
        _damage.SetDamage(host, damage);
    }

    private void OnRejuvenate(Entity<PhysiologyStateComponent> ent, ref RejuvenateEvent args)
    {
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
        {
            part.Comp2.Injury = new DamageSpecifier();
            part.Comp2.Bleeding = 0f;
        }
        Synchronize(ent);
    }

    private void OnPartRejuvenate(Entity<MedicalWoundComponent> ent, ref RejuvenateEvent args)
    {
        if (TryComp<OrganComponent>(ent, out var attached) && attached.Body is { } body && HasComp<PhysiologyComponent>(body))
            TransferAggregateDamage(body, -ent.Comp.Injury);
        ent.Comp.Injury = new DamageSpecifier();
        ent.Comp.Bleeding = 0f;
        if (TryComp<OrganComponent>(ent, out var organ) && organ.Body is { } host && HasComp<PhysiologyComponent>(host))
            Synchronize(host);
    }

    public bool TryInjure(EntityUid uid, string category, string damageType, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f || amount > 1000f || !HasComp<PhysiologyComponent>(uid))
            return false;
        var part = FindPart(uid, null, category);
        if (part == null || !part.Value.Comp2.BleedingPerDamage.ContainsKey(new ProtoId<DamageTypePrototype>(damageType)))
            return false;

        var damage = new DamageSpecifier();
        damage.DamageDict[damageType] = FixedPoint2.New(amount);
        if (!damage.AnyPositive())
            return false;
        return !_damage.ChangeDamage(uid, damage, targetPart: part.Value.Owner).Empty;
    }
}

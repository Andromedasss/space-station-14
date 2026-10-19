using System.Linq;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
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
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private PhysiologySystem _physiology = default!;
    [Dependency] private OrganRelationSystem _relations = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MedicalHandSystem _medicalHands = default!;
    [Dependency] private MedicalNeurologySystem _neurology = default!;

    private float _internalBleedingElapsed;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(BloodstreamSystem));
        SubscribeLocalEvent<PhysiologyComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<PhysiologyComponent, LocalizedDamageAttemptEvent>(OnLocalizedDamage);
        SubscribeLocalEvent<PhysiologyComponent, LocalizedHemostasisEvent>(OnLocalizedHemostasis);
        SubscribeLocalEvent<PhysiologyComponent, LocalizedHealingQueryEvent>(OnHealingQuery);
        SubscribeLocalEvent<PhysiologyComponent, BleedAmountChangedEvent>(OnBleedChanged);
        SubscribeLocalEvent<PhysiologyComponent, OrganInsertedIntoEvent>(OnInserted);
        SubscribeLocalEvent<PhysiologyComponent, OrganRemovedFromEvent>(OnRemoved);
        SubscribeLocalEvent<PhysiologyStateComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<MedicalWoundComponent, RejuvenateEvent>(OnPartRejuvenate);
        SubscribeLocalEvent<PhysiologyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MedicalWoundComponent, ComponentInit>(OnWoundComponentInit);
    }

    /// <summary>
    /// Mirrors the server-only wound state into a replicated component so the client can
    /// render the damage overlay for this body part only.
    /// </summary>
    private void OnWoundComponentInit(EntityUid uid, MedicalWoundComponent wound, ComponentInit args)
    {
        SyncVisualComponent(uid, wound);
    }

    private void SyncVisualComponent(EntityUid part, MedicalWoundComponent wound)
    {
        var total = wound.Injury.GetTotal().Float();

        if (!_entities.TryGetComponent<MedicalWoundVisualComponent>(part, out var visual) || visual is null)
        {
            // Only wounded parts carry the replicated visual state. Healthy parts stay
            // free of it, which keeps the networked state small for whole stations.
            if (total <= 0f)
                return;

            _entities.AddComponent(part, new MedicalWoundVisualComponent
            {
                TotalInjury = total,
                Fractured = wound.Fractured,
                Necrotic = wound.Necrotic,
                DominantGroup = DominantDamageGroup(wound),
            });
            return;
        }

        visual.TotalInjury = total;
        visual.Fractured = wound.Fractured;
        visual.Necrotic = wound.Necrotic;
        visual.DominantGroup = DominantDamageGroup(wound);

        // Networked component fields are only sent to the client once marked dirty.
        Dirty(part, visual);
    }

    /// <summary>
    /// Picks the damage group that best represents the wound, matching the groups the
    /// client damage overlay layers are configured for (Brute, Burn).
    /// </summary>
    private string DominantDamageGroup(MedicalWoundComponent wound)
    {
        var best = "Brute";
        var bestAmount = 0f;

        foreach (var (type, amount) in wound.Injury.DamageDict)
        {
            if (amount.Float() <= 0f)
                continue;

            var group = DamageGroupOf(type);
            if (group == null)
                continue;

            if (group == "Burn" && best != "Burn")
            {
                // Burns are more visually distinct than brute trauma.
                if (amount.Float() >= bestAmount)
                {
                    best = group;
                    bestAmount = amount.Float();
                }
                continue;
            }

            if (group == "Brute" && amount.Float() > bestAmount)
            {
                best = group;
                bestAmount = amount.Float();
            }
        }

        return best;
    }

    private string? DamageGroupOf(ProtoId<DamageTypePrototype> type)
    {
        foreach (var group in _prototypes.EnumeratePrototypes<DamageGroupPrototype>())
        {
            if (group.DamageTypes.Contains(type))
                return group.ID;
        }

        return null;
    }

    private void OnMapInit(Entity<PhysiologyComponent> ent, ref MapInitEvent args)
    {
        Synchronize(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _internalBleedingElapsed += frameTime;
        if (_internalBleedingElapsed < 1f)
            return;
        var elapsed = _internalBleedingElapsed;
        _internalBleedingElapsed = 0f;
        var query = EntityQueryEnumerator<PhysiologyComponent, BodyComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var blood))
        {
            var rate = 0f;
            foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(uid))
            {
                AdvanceInfection(uid, part.Comp2, elapsed);
                rate += InternalBleeding(part.Comp2);
                if (part.Comp2.Prosthetic || part.Comp1.Category?.Id is not ("ArmLeft" or "ArmRight" or "HandLeft" or "HandRight"
                        or "LegLeft" or "LegRight" or "FootLeft" or "FootRight")
                    || !TryComp<PhysiologyStateComponent>(uid, out var state))
                    continue;
                var perfusion = state.OxygenDelivery * (1f - Math.Clamp(part.Comp2.VascularDamage / 100f, 0f, 1f));
                part.Comp2.IschemiaDebt = Math.Clamp(part.Comp2.IschemiaDebt + (perfusion < 0.2f ? elapsed : -2f * elapsed), 0f, 180f);
                if (!part.Comp2.Necrotic && part.Comp2.IschemiaDebt >= 180f)
                {
                    part.Comp2.Necrotic = true;
                    part.Comp2.SurgeryRevision++;
                    _physiology.Record(state, $"NECROSIS {part.Comp1.Category}");
                    Synchronize(uid);
                }
            }
            if (rate > 0f)
                _blood.TryModifyBloodLevel((uid, blood), -FixedPoint2.New(Math.Min(rate, 10f) * elapsed));
        }
    }

    public void AdvanceInfection(EntityUid patient, MedicalWoundComponent wound, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f || wound.Prosthetic
            || !HasComp<PhysiologyComponent>(patient) || !TryComp<PhysiologyStateComponent>(patient, out var state))
            return;
        var antibiotic = _neurology.Dose(patient, "MedicalTestAntibiotic") >= 2f;
        // Protected time is integrated separately so a long update cannot bypass exposure.
        var exposed = wound.SurgicalAccess > 0 ? Math.Max(0f, seconds - wound.AntisepticSeconds) : 0f;
        wound.AntisepticSeconds = Math.Max(0f, wound.AntisepticSeconds - seconds);
        wound.Contamination = Math.Clamp(wound.Contamination + exposed * 0.1f - (antibiotic ? seconds * 0.5f : 0f), 0f, 100f);
        wound.Infection = Math.Clamp(wound.Infection + seconds *
            (antibiotic ? -0.5f : wound.Contamination >= 20f ? 0.1f : -0.02f), 0f, 100f);
        state.Contamination = Math.Clamp(state.Contamination + seconds * wound.Infection * 0.001f, 0f, 100f);
    }

    public static float InternalBleeding(MedicalWoundComponent wound)
    {
        return float.IsFinite(wound.VascularDamage) && float.IsFinite(wound.InternalBleedingPerDamage)
            ? Math.Clamp(wound.VascularDamage, 0f, 100f) * Math.Clamp(wound.InternalBleedingPerDamage, 0f, 1f)
            : 0f;
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
                var previousInjury = hit.Comp2.Injury.DamageDict.GetValueOrDefault(type);
                hit.Comp2.Injury.DamageDict[type] = previousInjury + amount;
                SyncVisualComponent(hit.Owner, hit.Comp2);
                if (!hit.Comp2.Prosthetic && type == "Blunt" && (hit.Comp1.Category?.Id is "Head" or "ArmLeft" or "ArmRight" or "HandLeft" or "HandRight"
                        or "LegLeft" or "LegRight" or "FootLeft" or "FootRight")
                    && float.IsFinite(hit.Comp2.FractureThreshold) && hit.Comp2.FractureThreshold > 0f
                    && hit.Comp2.Injury.DamageDict[type] >= FixedPoint2.New(hit.Comp2.FractureThreshold))
                    hit.Comp2.Fractured = true;
                if (!hit.Comp2.Prosthetic && type == "Piercing" && float.IsFinite(hit.Comp2.VascularThreshold) && hit.Comp2.VascularThreshold > 0f)
                    hit.Comp2.VascularDamage = Math.Clamp(hit.Comp2.VascularDamage
                        + Math.Max(0f, amount.Float() - hit.Comp2.VascularThreshold), 0f, 100f);
                var crushBleeding = 0f;
                if (!hit.Comp2.Prosthetic && type == "Blunt")
                {
                    var deep = PhysiologyModel.NewlyExposedDamage(previousInjury.Float(), amount.Float(), hit.Comp2.CrushVascularThreshold);
                    var skin = PhysiologyModel.NewlyExposedDamage(previousInjury.Float(), amount.Float(), hit.Comp2.CrushSkinThreshold);
                    if (float.IsFinite(hit.Comp2.CrushVascularTransfer))
                        hit.Comp2.VascularDamage = Math.Clamp(hit.Comp2.VascularDamage
                            + deep * Math.Clamp(hit.Comp2.CrushVascularTransfer, 0f, 1f), 0f, 100f);
                    if (float.IsFinite(hit.Comp2.CrushBleedingTransfer))
                        crushBleeding = skin * Math.Clamp(hit.Comp2.CrushBleedingTransfer, 0f, 1f);
                }
                if (!hit.Comp2.Prosthetic && TryComp<BloodstreamComponent>(ent, out var blood))
                    hit.Comp2.Bleeding += Math.Min(Math.Max(0f, blood.MaxBleedAmount - TotalBleeding(ent)),
                        Math.Max(0f, amount.Float() * coefficient + crushBleeding));
                if (TryComp<PhysiologyStateComponent>(ent, out var state))
                    _physiology.Record(state, $"WOUND {hit.Comp1.Category}: {type} +{amount}; source={args.Origin}");
                if (!hit.Comp2.Prosthetic && hit.Comp1.Category?.Id == "Head")
                {
                    foreach (var brain in _body.EnumerateOrgans<CerebralHealthComponent>(ent.Owner))
                    {
                        if (!TryComp<ChildOrganComponent>(brain.Owner, out var child) || child.Parent != hit.Owner)
                            continue;
                        var trauma = PhysiologyModel.HeadTrauma(type.Id, previousInjury.Float(), amount.Float());
                        if (trauma <= 0f)
                            continue;
                        var previousBrainDamage = brain.Comp2.Damage;
                        brain.Comp2.Damage = Math.Clamp(previousBrainDamage + trauma, 0f, PhysiologyModel.MaximumBrainDamage);
                        _neurology.AddInjury((brain.Owner, brain.Comp2), brain.Comp2.Damage - previousBrainDamage);
                        if (TryComp<PhysiologyStateComponent>(ent, out var history))
                            _physiology.Record(history, $"BRAIN mechanical trauma +{trauma:F2}; injury={brain.Comp2.Damage:F2}");
                    }
                }
                foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(ent.Owner))
                {
                    var medical = organ.Comp2;
                    if (medical.ProtectedBy != hit.Comp1.Category?.Id
                        || !medical.TraumaPerDamage.TryGetValue(type, out var transfer)
                        || !float.IsFinite(transfer) || transfer <= 0f
                        || !float.IsFinite(medical.TraumaThreshold) || medical.TraumaThreshold < 0f)
                        continue;

                    var trauma = (type == "Blunt"
                        ? PhysiologyModel.NewlyExposedDamage(previousInjury.Float(), amount.Float(), medical.TraumaThreshold)
                        : Math.Max(0f, amount.Float() - medical.TraumaThreshold)) * transfer;
                    var previous = medical.Trauma;
                    medical.Trauma = Math.Clamp(previous + trauma, 0f, 100f);
                    if (medical.Kind == MedicalOrganKind.Intestines && medical.Trauma >= 60f
                        && trauma > 0f && type.Id is "Piercing" or "Slash")
                        medical.Perforated = true;
                    if (medical.Trauma != previous && TryComp<PhysiologyStateComponent>(ent, out var history))
                        _physiology.Record(history, $"ORGAN {medical.Kind}: trauma {previous:F2} -> {medical.Trauma:F2}");
                }
            }
            else if (amount < 0)
            {
                if (args.TargetPart != null && part is { } healed)
                {
                    var injury = healed.Comp2.Injury.DamageDict.GetValueOrDefault(type);
                    healed.Comp2.Injury.DamageDict[type] = FixedPoint2.Max(FixedPoint2.Zero, injury + amount);
                    SyncVisualComponent(healed.Owner, healed.Comp2);
                    continue;
                }
                // Upstream healing has no location. Distribute actual repaired damage, not requested healing.
                var total = FixedPoint2.Zero;
                foreach (var organ in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
                {
                    if (!organ.Comp2.Prosthetic)
                        total += organ.Comp2.Injury.DamageDict.GetValueOrDefault(type);
                }
                var remaining = FixedPoint2.Min(-amount, total);
                foreach (var organ in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
                {
                    if (organ.Comp2.Prosthetic)
                        continue;
                    var injury = organ.Comp2.Injury.DamageDict.GetValueOrDefault(type);
                    if (injury <= 0 || total <= 0)
                        continue;
                    var repaired = FixedPoint2.FromCents((int)((long)remaining.Value * injury.Value / total.Value));
                    organ.Comp2.Injury.DamageDict[type] = injury - repaired;
                    SyncVisualComponent(organ.Owner, organ.Comp2);
                    remaining -= repaired;
                    total -= injury;
                }
            }
        }
        Synchronize(ent);
    }

    private void OnLocalizedDamage(Entity<PhysiologyComponent> ent, ref LocalizedDamageAttemptEvent args)
    {
        if (args.Part == null)
        {
            args.Damage = args.Damage.Clone();
            foreach (var (type, amount) in args.Damage.DamageDict.ToArray())
            {
                if (amount >= 0)
                    continue;
                var biological = FixedPoint2.Zero;
                var localized = false;
                foreach (var organ in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
                {
                    localized |= organ.Comp2.BleedingPerDamage.ContainsKey(type);
                    if (!organ.Comp2.Prosthetic)
                        biological += organ.Comp2.Injury.DamageDict.GetValueOrDefault(type);
                }
                if (localized)
                    args.Damage.DamageDict[type] = -FixedPoint2.Min(-amount, biological);
            }
            return;
        }
        var part = FindPart(ent, args.Part, "Torso");
        if (part == null)
        {
            args.Cancelled = true;
            return;
        }
        args.Damage = args.Damage.Clone();
        foreach (var (type, amount) in args.Damage.DamageDict.ToArray())
        {
            if (amount < 0 && part.Value.Comp2.BleedingPerDamage.ContainsKey(type))
                args.Damage.DamageDict[type] = part.Value.Comp2.Prosthetic ? FixedPoint2.Zero
                    : -FixedPoint2.Min(-amount, part.Value.Comp2.Injury.DamageDict.GetValueOrDefault(type));
        }
    }

    private void OnLocalizedHemostasis(Entity<PhysiologyComponent> ent, ref LocalizedHemostasisEvent args)
    {
        if (args.Part == null)
            return;
        args.Handled = true;
        if (FindPart(ent, args.Part, "Torso") is not { } part)
            return;
        part.Comp2.Bleeding = Math.Max(0f, part.Comp2.Bleeding + args.Amount);
        Synchronize(ent);
    }

    private void OnHealingQuery(Entity<PhysiologyComponent> ent, ref LocalizedHealingQueryEvent args)
    {
        args.Handled = true;
        if (FindPart(ent, args.Part, "Torso") is not { } part)
            return;
        args.CanHeal = args.BleedingModifier < 0f && part.Comp2.Bleeding > 0f;
        foreach (var (type, amount) in args.Damage.DamageDict)
        {
            if (amount < 0 && (part.Comp2.BleedingPerDamage.ContainsKey(type)
                    ? part.Comp2.Prosthetic ? FixedPoint2.Zero : part.Comp2.Injury.DamageDict.GetValueOrDefault(type)
                    : _damage.GetAllDamage(ent.Owner).DamageDict.GetValueOrDefault(type)) > 0)
                args.CanHeal = true;
        }
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
            var leftArm = 0f;
            var rightArm = 0f;
            var leftHand = 0f;
            var rightHand = 0f;
            foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(uid))
            {
                var support = float.IsFinite(part.Comp2.SupportDamage) && part.Comp2.SupportDamage > 0f
                    ? Math.Clamp(1f - part.Comp2.Injury.GetTotal().Float() / part.Comp2.SupportDamage, 0f, 1f)
                    : 0f;
                if (part.Comp2.Fractured || part.Comp2.Necrotic)
                    support = 0f;
                switch (part.Comp1.Category?.Id)
                {
                    case "LegLeft": leftLeg = Math.Max(leftLeg, support); break;
                    case "LegRight": rightLeg = Math.Max(rightLeg, support); break;
                    case "FootLeft": leftFoot = Math.Max(leftFoot, support); break;
                    case "FootRight": rightFoot = Math.Max(rightFoot, support); break;
                    case "ArmLeft": leftArm = Math.Max(leftArm, support); break;
                    case "ArmRight": rightArm = Math.Max(rightArm, support); break;
                    case "HandLeft": leftHand = Math.Max(leftHand, support); break;
                    case "HandRight": rightHand = Math.Max(rightHand, support); break;
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
            var left = Math.Min(leftArm, leftHand);
            var right = Math.Min(rightArm, rightHand);
            if (physiology.LeftHandFunction != left || physiology.RightHandFunction != right)
            {
                physiology.LeftHandFunction = left;
                physiology.RightHandFunction = right;
                Dirty(uid, physiology);
                foreach (var id in _hands.EnumerateHands(uid))
                {
                    if (!_medicalHands.CanUseHand(uid, id))
                        _hands.TryDrop(uid, id, checkActionBlocker: false);
                }
            }
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
        _neurology.Refresh(ent);
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
        _neurology.Refresh(ent);
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
            part.Comp2.SurgicalAccess = 0;
            part.Comp2.Contamination = part.Comp2.Infection = part.Comp2.AntisepticSeconds = 0f;
            part.Comp2.BoneAccess = 0;
            part.Comp2.Fractured = false;
            part.Comp2.VascularDamage = 0f;
            part.Comp2.IschemiaDebt = 0f;
            part.Comp2.Necrotic = false;
            part.Comp2.SurgeryRevision++;
        }
        Synchronize(ent);
    }

    private void OnPartRejuvenate(Entity<MedicalWoundComponent> ent, ref RejuvenateEvent args)
    {
        if (TryComp<OrganComponent>(ent, out var attached) && attached.Body is { } body && HasComp<PhysiologyComponent>(body))
            TransferAggregateDamage(body, -ent.Comp.Injury);
        ent.Comp.Injury = new DamageSpecifier();
        ent.Comp.Bleeding = 0f;
        ent.Comp.SurgicalAccess = 0;
        ent.Comp.Contamination = ent.Comp.Infection = ent.Comp.AntisepticSeconds = 0f;
        ent.Comp.BoneAccess = 0;
        ent.Comp.Fractured = false;
        ent.Comp.VascularDamage = 0f;
        ent.Comp.IschemiaDebt = 0f;
        ent.Comp.Necrotic = false;
        ent.Comp.SurgeryRevision++;
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

    public void ClampSurgicalBleeding(EntityUid patient, MedicalWoundComponent wound)
    {
        wound.Bleeding = 0f;
        Synchronize(patient);
    }

    public void RepairFracture(EntityUid patient, MedicalWoundComponent wound)
    {
        wound.Fractured = false;
        Synchronize(patient);
    }

    public void RepairVessel(MedicalWoundComponent wound)
    {
        wound.VascularDamage = 0f;
    }

    public void RepairProsthetic(EntityUid patient, MedicalWoundComponent wound)
    {
        TransferAggregateDamage(patient, -wound.Injury);
        wound.Injury = new DamageSpecifier();
        Synchronize(patient);
    }
}

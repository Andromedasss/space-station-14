using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Robust.Shared.Containers;

namespace Content.Shared.Medical;

public sealed class MedicalHandSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HandsComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        SubscribeLocalEvent<PhysiologyComponent, InteractionAttemptEvent>(OnInteract);
        SubscribeLocalEvent<PhysiologyComponent, AttackAttemptEvent>(OnAttack);
        SubscribeLocalEvent<PhysiologyComponent, UseAttemptEvent>(OnUse);
    }

    public bool CanUseHand(EntityUid user, string? id)
    {
        if (!TryComp<PhysiologyComponent>(user, out var physiology))
            return true;
        if (!_hands.TryGetHand(user, id, out var hand))
            return false;
        return hand.Value.Location switch
        {
            HandLocation.Left => physiology.LeftHandFunction > 0f,
            HandLocation.Right => physiology.RightHandFunction > 0f,
            _ => true,
        };
    }

    private void OnInsert(Entity<HandsComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (_hands.TryGetHand(ent.Owner, args.Container.ID, out _) && !CanUseHand(ent, args.Container.ID))
            args.Cancel();
    }

    private void OnInteract(Entity<PhysiologyComponent> ent, ref InteractionAttemptEvent args)
    {
        if (!CanUseHand(ent, _hands.GetActiveHand(ent.Owner)))
            args.Cancelled = true;
    }

    private void OnAttack(Entity<PhysiologyComponent> ent, ref AttackAttemptEvent args)
    {
        if (!CanUseHand(ent, _hands.GetActiveHand(ent.Owner)))
            args.Cancel();
    }

    private void OnUse(Entity<PhysiologyComponent> ent, ref UseAttemptEvent args)
    {
        if (_hands.IsHolding(ent.Owner, args.Used, out var id) && !CanUseHand(ent, id))
            args.Cancel();
    }
}

using System.Linq;
using Content.Shared.Body;
using Content.Shared.Medical;
using Robust.Shared.Containers;

namespace Content.Server.Medical;

/// <summary>
/// Maintains experimental anatomy across physical amputations and transplants.
/// Detached descendants remain contained in their root, not functional in the donor.
/// </summary>
public sealed class MedicalAnatomySystem : EntitySystem
{
    private const string DetachedParts = "medical_detached_parts";
    [Dependency] private BodySystem _body = default!;
    [Dependency] private InitialBodySystem _initialBody = default!;
    [Dependency] private OrganRelationSystem _relations = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    private readonly HashSet<EntityUid> _moving = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PhysiologyStateComponent, MapInitEvent>(OnMapInit, after: [typeof(InitialBodySystem)]);
        SubscribeLocalEvent<PhysiologyComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<PhysiologyComponent, EntInsertedIntoContainerMessage>(OnInserted, after: [typeof(BodySystem)]);
        SubscribeLocalEvent<PhysiologyComponent, EntRemovedFromContainerMessage>(OnRemoved, after: [typeof(BodySystem)]);
    }

    private void OnMapInit(Entity<PhysiologyStateComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.AnatomyInitialized = true;
    }

    private bool Ready(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid)
            && TryComp<BodyComponent>(uid, out var body) && body.LifeStage == ComponentLifeStage.Running
            && TryComp<PhysiologyStateComponent>(uid, out var state) && state.AnatomyInitialized;
    }

    private void OnInsertAttempt(Entity<PhysiologyComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != BodyComponent.ContainerID || !Ready(ent))
            return;

        if (!TryComp<InitialBodyComponent>(ent, out var anatomy)
            || !TryComp<OrganComponent>(args.EntityUid, out var organ) || organ.Category is not { } category
            || !_initialBody.SupportsCategory(ent.Owner, category))
        {
            args.Cancel();
            return;
        }

        var incoming = new[] { args.EntityUid }.Concat(_relations.AllChildren(args.EntityUid).Select(p => p.Owner)).ToArray();
        var occupied = _body.EnumerateOrgans<OrganComponent>(ent.Owner).Select(p => p.Comp1.Category).ToHashSet();
        foreach (var part in incoming)
        {
            if (!TryComp<OrganComponent>(part, out var candidate) || candidate.Category is not { } slot
                || !_initialBody.SupportsCategory(ent.Owner, slot) || !occupied.Add(slot))
            {
                args.Cancel();
                return;
            }
        }

        if (anatomy.Relationships == null)
            return;
        foreach (var (parent, children) in anatomy.Relationships)
        {
            if (children.Contains(category) && !_body.EnumerateOrgans<OrganComponent>(ent.Owner)
                    .Any(p => p.Comp1.Category == parent))
            {
                args.Cancel();
                return;
            }
        }
    }

    private void OnRemoved(Entity<PhysiologyComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != BodyComponent.ContainerID || !Ready(ent) || _moving.Contains(args.Entity))
            return;

        var descendants = _relations.AllChildren(args.Entity).Select(p => p.Owner)
            .Where(p => TryComp<OrganComponent>(p, out var organ) && organ.Body == ent.Owner).ToArray();
        // The wound system has already made the root stump. Orphan before removing
        // descendants so they cannot create additional wounds on the donor torso.
        _relations.Orphan(args.Entity);
        if (descendants.Length == 0)
            return;
        // A deleted root cannot own a new container. Its surviving descendants
        // must still leave the donor, but become separate detached parts.
        var bundle = TerminatingOrDeleted(args.Entity) || EntityManager.IsQueuedForDeletion(args.Entity)
            ? null : _containers.EnsureContainer<Container>(args.Entity, DetachedParts);
        foreach (var child in descendants)
            _moving.Add(child);
        try
        {
            foreach (var child in descendants)
            {
                _containers.Remove(child, args.Container);
                if (bundle != null)
                    _containers.Insert(child, bundle);
                else
                    _relations.Orphan(child);
            }
        }
        finally
        {
            foreach (var child in descendants)
                _moving.Remove(child);
        }
    }

    private void OnInserted(Entity<PhysiologyComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != BodyComponent.ContainerID || !Ready(ent)
            || !TryComp<OrganComponent>(args.Entity, out var organ)
            || !TryComp<InitialBodyComponent>(ent, out var anatomy))
            return;

        if (anatomy.Relationships != null && organ.Category is { } category)
        {
            foreach (var (slot, children) in anatomy.Relationships)
            {
                if (!children.Contains(category))
                    continue;
                var parent = _body.EnumerateOrgans<OrganComponent>(ent.Owner).First(p => p.Comp1.Category == slot);
                if (!TryComp<ChildOrganComponent>(args.Entity, out var child) || child.Parent != parent.Owner)
                {
                    _relations.Orphan(args.Entity);
                    _relations.Relate(parent.Owner, args.Entity);
                }
                break;
            }
        }

        if (!_containers.TryGetContainer(args.Entity, DetachedParts, out var bundle))
            return;
        foreach (var part in _relations.AllChildren(args.Entity).Select(p => p.Owner).ToArray())
        {
            if (!bundle.Contains(part))
                continue;
            _containers.Remove(part, bundle);
            _containers.Insert(part, args.Container);
        }
    }
}

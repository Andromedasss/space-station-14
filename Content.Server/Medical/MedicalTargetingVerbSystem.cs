using Content.Shared.Body;
using Content.Shared.Medical;
using Content.Shared.Verbs;

namespace Content.Server.Medical;

public sealed class MedicalTargetingVerbSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PhysiologyComponent, GetVerbsEvent<Verb>>(OnVerbs);
    }

    private void OnVerbs(Entity<PhysiologyComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        foreach (var part in _body.EnumerateOrgans<MedicalWoundComponent>(ent.Owner))
        {
            if (part.Comp1.Category is not { } category)
                continue;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("medical-target-zone", ("part", category.Id)),
                Act = () =>
                {
                    var selection = EnsureComp<MedicalTargetingComponent>(user);
                    selection.Category = category.Id;
                    Dirty(user, selection);
                },
            });
        }
    }
}

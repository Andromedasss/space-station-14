using Content.Shared.Body;

namespace Content.Shared.Medical;

public sealed class MedicalTargetingSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;

    public EntityUid? SelectedPart(EntityUid user, EntityUid patient)
    {
        if (!HasComp<PhysiologyComponent>(patient))
            return null;
        var category = TryComp<MedicalTargetingComponent>(user, out var selection) ? selection.Category : "Torso";
        foreach (var part in _body.EnumerateOrgans<OrganComponent>(patient))
        {
            if (part.Comp1.Category?.Id == category)
                return part.Owner;
        }
        return null;
    }
}

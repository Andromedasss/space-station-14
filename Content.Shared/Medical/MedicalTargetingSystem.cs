using Content.Shared.Body;
using Content.Shared.Random.Helpers;
using Robust.Shared.Timing;

namespace Content.Shared.Medical;

public sealed class MedicalTargetingSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private IGameTiming _timing = default!;

    // These are gameplay exposure weights, not sprite hitboxes. Iterate in a fixed order for prediction.
    private static readonly (string Category, int Weight)[] Exposure =
    {
        ("Torso", 22), ("Abdomen", 12), ("Groin", 6), ("Head", 10),
        ("ArmLeft", 10), ("ArmRight", 10), ("LegLeft", 10), ("LegRight", 10),
        ("HandLeft", 3), ("HandRight", 3), ("FootLeft", 2), ("FootRight", 2),
    };

    public static bool IsTargetZone(string category)
    {
        foreach (var zone in Exposure)
            if (zone.Category == category)
                return true;
        return false;
    }

    /// <summary>
    /// A non-aimed hit wounds one attached external part. Missing parts cannot absorb damage.
    /// Wide swings roll independently for each victim, without multiplying damage across their limbs.
    /// </summary>
    public EntityUid? UntargetedPart(EntityUid patient, EntityUid source)
    {
        if (!HasComp<PhysiologyComponent>(patient))
            return null;

        var parts = new Dictionary<string, EntityUid>();
        foreach (var part in _body.EnumerateOrgans<OrganComponent>(patient))
        {
            if (part.Comp1.Category is { } category)
                parts.TryAdd(category.Id, part.Owner);
        }

        var total = 0;
        foreach (var (category, weight) in Exposure)
        {
            if (parts.ContainsKey(category))
                total += weight;
        }
        if (total == 0)
            return null;

        var roll = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(patient), GetNetEntity(source)).Next(total);
        foreach (var (category, weight) in Exposure)
        {
            if (!parts.TryGetValue(category, out var part))
                continue;
            roll -= weight;
            if (roll < 0)
                return part;
        }
        return null;
    }

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

using Content.Client.Damage;
using Content.Shared.Body;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Medical;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.Medical;

/// <summary>
/// Drives the per-body-part damage overlay layers for physiology-tracked mobs.
/// The damage overlay art already contains per-part states (e.g. "LArm_Brute_50"),
/// but the stock visualizer drives every part from the body-wide damage total,
/// which makes wounds appear all over the body at once. This system instead
/// drives each part's overlay from that part's own wound data.
/// </summary>
public sealed class MedicalWoundVisualsSystem : EntitySystem
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    /// <summary>
    /// Fallback mapping from organ category to body sprite layer, used when the
    /// organ does not carry visual layer information.
    /// </summary>
    private static readonly Dictionary<string, HumanoidVisualLayers> CategoryLayers = new()
    {
        ["Torso"] = HumanoidVisualLayers.Chest,
        ["Head"] = HumanoidVisualLayers.Head,
        ["ArmLeft"] = HumanoidVisualLayers.LArm,
        ["ArmRight"] = HumanoidVisualLayers.RArm,
        ["LegLeft"] = HumanoidVisualLayers.LLeg,
        ["LegRight"] = HumanoidVisualLayers.RLeg,
    };

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (visual, organ) in _entities.EntityQuery<MedicalWoundVisualComponent, OrganComponent>(true))
        {
            UpdatePart(visual, organ);
        }
    }

    private void UpdatePart(MedicalWoundVisualComponent visual, OrganComponent organ)
    {
        if (organ.Body is not { } body)
            return;

        var layer = ResolveLayer(organ);
        if (layer == null)
            return;

        if (!_entities.TryGetComponent(body, out SpriteComponent? sprite))
            return;

        if (!_entities.TryGetComponent(body, out DamageVisualsComponent? damageVis))
            return;

        if (damageVis.DamageOverlayGroups == null)
            return;

        var group = visual.DominantGroup;
        if (!damageVis.DamageOverlayGroups.ContainsKey(group))
            return;

        // The stock visualizer registers one overlay layer per target layer and group,
        // keyed "<Layer><Group>" e.g. "LArmBrute", with states "<Layer>_<Group>_<threshold>".
        var layerName = layer.Value.ToString();
        var overlayKey = $"{layerName}{group}";

        if (!_spriteSystem.LayerMapTryGet((body, sprite), overlayKey, out var spriteLayer, false))
            return;

        var threshold = PickThreshold(damageVis.Thresholds, visual.TotalInjury);
        if (threshold <= 0f)
        {
            _spriteSystem.LayerSetVisible((body, sprite), spriteLayer, false);
            return;
        }

        _spriteSystem.LayerSetVisible((body, sprite), spriteLayer, true);
        _spriteSystem.LayerSetRsiState((body, sprite), spriteLayer, $"{layerName}_{group}_{threshold}");
    }

    /// <summary>
    /// Finds the body sprite layer this organ is drawn on. The visual organ component
    /// carries the exact layer, the category mapping is only a fallback.
    /// </summary>
    private HumanoidVisualLayers? ResolveLayer(OrganComponent organ)
    {
        if (_entities.TryGetComponent<VisualOrganComponent>(organ.Owner, out var visualOrgan)
            && visualOrgan.Layer is HumanoidVisualLayers visualLayer)
        {
            return visualLayer;
        }

        if (organ.Category is { } category && CategoryLayers.TryGetValue(category.Id, out var mapped))
            return mapped;

        return null;
    }

    /// <summary>
    /// Picks the highest configured damage threshold that the injury reaches,
    /// mirroring the stock threshold boundary logic.
    /// </summary>
    private static int PickThreshold(List<FixedPoint2> thresholds, float damage)
    {
        if (damage <= 0f)
            return 0;

        var result = 0;
        foreach (var threshold in thresholds)
        {
            var value = threshold.Int();
            if (value > 0 && damage >= value)
                result = value;
        }

        return result;
    }
}

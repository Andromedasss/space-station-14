using Content.Shared.Body;
using Content.Shared.Medical;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Medical;

/// <summary>
/// Client-side system that draws red wound overlays on individual body parts
/// when they sustain damage, based on <see cref="MedicalWoundVisualComponent"/>.
/// </summary>
public sealed class MedicalWoundVisualsSystem : EntitySystem
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    private const string WoundOverlayLayerKey = "MedicalWoundOverlay";
    private readonly string[] _woundOverlayRsis =
    {
        "/Textures/Mobs/Effects/brute_damage.rsi",
        "/Textures/Mobs/Effects/burn_damage.rsi",
    };

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (visual, sprite) in _entities.EntityQuery<MedicalWoundVisualComponent, SpriteComponent>(true))
        {
            UpdateWoundOverlay(visual.Owner, visual, sprite);
        }
    }

    private void UpdateWoundOverlay(EntityUid uid, MedicalWoundVisualComponent visual, SpriteComponent sprite)
    {
        var totalInjury = visual.TotalInjury;
        var hasWound = totalInjury > 0f;

        if (hasWound)
        {
            AddOrUpdateWoundOverlay(uid, sprite, totalInjury);
        }
        else
        {
            RemoveWoundOverlay(uid, sprite);
        }
    }

    private void AddOrUpdateWoundOverlay(EntityUid uid, SpriteComponent sprite, float injury)
    {
        var spriteEnt = (uid, sprite);

        // Check if overlay already exists
        if (_spriteSystem.LayerMapTryGet(spriteEnt, WoundOverlayLayerKey, out var existingLayer, false))
        {
            // Update opacity based on injury severity
            var overlayOpacity = Math.Clamp(injury / 100f, 0.2f, 1f);
            var overlayColor = Color.FromHex("FF0000");
            overlayColor.A = overlayOpacity;
            _spriteSystem.LayerSetColor(spriteEnt, existingLayer, overlayColor);
            _spriteSystem.LayerSetVisible(spriteEnt, existingLayer, true);
            return;
        }

        // Add new overlay layer using brute damage RSI
        var newLayer = _spriteSystem.AddLayer(
            spriteEnt,
            new SpriteSpecifier.Rsi(new ResPath(_woundOverlayRsis[0]), "brute_damage")
        );
        _spriteSystem.LayerMapSet(spriteEnt, WoundOverlayLayerKey, newLayer);
        var newOpacity = Math.Clamp(injury / 100f, 0.2f, 1f);
        var newColor = Color.FromHex("FF0000");
        newColor.A = newOpacity;
        _spriteSystem.LayerSetColor(spriteEnt, newLayer, newColor);
        _spriteSystem.LayerSetVisible(spriteEnt, newLayer, true);
    }

    private void RemoveWoundOverlay(EntityUid uid, SpriteComponent sprite)
    {
        var spriteEnt = (uid, sprite);
        if (_spriteSystem.LayerMapTryGet(spriteEnt, WoundOverlayLayerKey, out var layer, false))
        {
            _spriteSystem.RemoveLayer(spriteEnt, layer);
            _spriteSystem.LayerMapRemove(spriteEnt, WoundOverlayLayerKey);
        }
    }
}

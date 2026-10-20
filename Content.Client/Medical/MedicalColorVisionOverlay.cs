using Content.Shared.Medical;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Medical;

/// <summary>Filters only the controlled body's world viewport, never chat or medical UI.</summary>
public sealed class MedicalColorVisionOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly ShaderInstance _shader;
    private MedicalColorVision _vision;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public MedicalColorVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index<ShaderPrototype>("MedicalColorVision").InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entities.TryGetComponent(_player.LocalEntity, out PhysiologyComponent? physiology)
            || physiology.NeurologicalVision == MedicalColorVision.Normal
            || !_entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye)
            || args.Viewport.Eye != eye.Eye)
            return false;

        _vision = physiology.NeurologicalVision;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("vision", (float) _vision);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}

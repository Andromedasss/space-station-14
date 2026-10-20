using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Timing;
using Content.Shared.Camera;
using Content.Shared.Movement.Systems;
using Content.Shared.CCVar;
using Content.Shared.Medical;
using Robust.Shared.Configuration;
using System.Numerics;

namespace Content.Client.Medical;

public sealed class MedicalNeurologySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedContentEyeSystem _eye = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    private EntityUid? _lastViewer;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_lastViewer != _player.LocalEntity && TryComp<EyeComponent>(_lastViewer, out var oldEye))
            _eye.UpdateEyeOffset((_lastViewer!.Value, oldEye));
        _lastViewer = _player.LocalEntity;
        if (TryComp<EyeComponent>(_lastViewer, out var eye))
            _eye.UpdateEyeOffset((_lastViewer!.Value, eye));
    }

    private void OnEyeOffset(Entity<PhysiologyComponent> ent, ref GetEyeOffsetEvent args)
    {
        if (ent.Owner != _player.LocalEntity || _config.GetCVar(CCVars.ReducedMotion))
            return;
        // An independent additive offset never accumulates or overwrites weapon recoil.
        var strength = (Math.Clamp((ent.Comp.Fear - 20) / 80f, 0f, 1f) * 0.035f
            + (ent.Comp.CognitiveSymptoms.HasFlag(MedicalCognitiveSymptoms.Tremor) ? 0.006f : 0f))
            * Math.Clamp(_config.GetCVar(CCVars.ScreenShakeIntensity), 0f, 1f);
        var phase = (float) _timing.CurTime.TotalSeconds;
        args.Offset += new Vector2(MathF.Sin(phase * 31f), MathF.Cos(phase * 37f)) * strength;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysiologyComponent, GetEyeOffsetEvent>(OnEyeOffset);
        _overlays.AddOverlay(new MedicalColorVisionOverlay());
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<MedicalColorVisionOverlay>();
        base.Shutdown();
    }
}

using Content.Shared.Medical;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Medical;

/// <summary>
/// A target preference, not a scan of a particular patient's anatomy.
/// </summary>
public sealed class MedicalTargetingControl : BoxContainer
{
    private readonly IEntityManager _entities;
    private readonly IPlayerManager _players;
    private readonly Label _selected = new() { MaxWidth = 140, ClipText = true };
    private readonly Label _pain = new() { Visible = false };
    private readonly MedicalBodySelector _body = new();
    private string? _category;
    private int _lastPain = -1;

    public MedicalTargetingControl()
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _players = IoCManager.Resolve<IPlayerManager>();
        Orientation = LayoutOrientation.Vertical;
        ToolTip = Loc.GetString("medical-hud-help");
        AddChild(_selected);
        _body.OnSelect += category =>
        {
            // Wait for the replicated choice before highlighting a different zone.
            UpdateSelection(_category ?? "Torso");
            _entities.System<MedicalPanelSystem>().SelectHudZone(category);
        };
        AddChild(_body);
        AddChild(_pain);
        UpdateSelection("Torso");
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var category = _players.LocalEntity is { } user
            && _entities.TryGetComponent<MedicalTargetingComponent>(user, out var selection)
            ? selection.Category : "Torso";
        if (_category != category)
            UpdateSelection(category);
        var pain = _players.LocalEntity is { } patient
            && _entities.TryGetComponent<PhysiologyComponent>(patient, out var physiology)
            ? physiology.Pain : -1;
        if (_lastPain != pain)
        {
            _lastPain = pain;
            _pain.Visible = pain >= 0;
            _pain.Text = pain >= 0 ? Loc.GetString("medical-pain-hud", ("pain", pain)) : "";
            _pain.ToolTip = Loc.GetString("medical-pain-help");
        }
    }

    private void UpdateSelection(string category)
    {
        _category = category;
        _selected.Text = Loc.GetString("medical-hud-zone", ("part", category));
        _body.SetState(category);
    }
}

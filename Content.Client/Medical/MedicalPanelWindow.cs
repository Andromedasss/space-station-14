using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared.Medical;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Medical;

public sealed class MedicalPanelWindow : FancyWindow
{
    private readonly BoxContainer _content = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private string? _signature;
    public event Action<MedicalPanelOperation>? OnOperate;

    public MedicalPanelWindow()
    {
        Title = Loc.GetString("medical-panel-title");
        MinWidth = 480;
        MaxHeight = 650;
        ContentsContainer.AddChild(new ScrollContainer { Children = { _content } });
        _content.AddChild(new Label { Text = Loc.GetString("medical-panel-loading") });
    }

    public void Populate(MedicalPanelStateEvent state)
    {
        // Do not replace buttons under the pointer on every periodic refresh.
        var signature = $"{state.Patient}/{state.Accessible}/{state.Name}/{state.SelectedZone}:"
            + string.Join(";", state.Parts.Select(p => $"{p.Category}/{p.Access}/{p.BoneAccess}/{string.Join(',', p.Organs)}"))
            + string.Join(";", state.Operations.Select(o => $"{o.Label}/{o.Requirement}/{o.Tool}/"
                + $"{o.Surgery?.Part}/{o.Surgery?.Organ}/{o.Surgery?.Table}/{o.Surgery?.Revision}/"
                + $"{o.Repair?.Part}/{o.Repair?.Organ}/{o.Repair?.Table}/{o.Repair?.Revision}"));
        if (_signature == signature)
            return;
        _signature = signature;
        _content.RemoveAllChildren();
        if (!state.Accessible)
        {
            _content.AddChild(new Label { Text = Loc.GetString("medical-panel-unavailable") });
            return;
        }
        _content.AddChild(new Label { Text = state.Name });
        _content.AddChild(new Label { Text = Loc.GetString("medical-selected-zone", ("part", state.SelectedZone)) });
        var part = state.Parts.FirstOrDefault(p => p.Category == state.SelectedZone);
        if (part == null)
        {
            _content.AddChild(new Label { Text = Loc.GetString("medical-panel-selected-missing") });
            return;
        }
        _content.AddChild(new Label { Text = Loc.GetString("medical-panel-access", ("stage", part.Access)) });
        _content.AddChild(new Label { Text = Loc.GetString("medical-bone-access", ("stage", part.BoneAccess)) });
        _content.AddChild(new Label { Text = Loc.GetString("medical-panel-organs", ("organs", string.Join(", ", part.Organs))) });
        foreach (var operation in state.Operations)
        {
            var button = new Button
            {
                Text = operation.Label,
                Disabled = operation.Requirement != null,
                ToolTip = operation.Requirement,
            };
            button.OnPressed += _ => OnOperate?.Invoke(operation);
            _content.AddChild(button);
            if (operation.Requirement is { } reason)
                _content.AddChild(new Label { Text = reason, MaxWidth = 460 });
        }
    }
}

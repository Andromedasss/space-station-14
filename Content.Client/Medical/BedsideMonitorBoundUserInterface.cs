using Content.Client.UserInterface.Controls;
using Content.Shared.Medical;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Medical;

[UsedImplicitly]
public sealed class BedsideMonitorBoundUserInterface : BoundUserInterface
{
    private BedsideMonitorWindow? _window;

    public BedsideMonitorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<BedsideMonitorWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is BedsideMonitorUiState monitor)
            _window?.UpdateState(monitor);
    }
}

public sealed class BedsideMonitorWindow : FancyWindow
{
    private readonly Label _status = new();
    private readonly Label _pulse = new();
    private readonly Label _pressure = new();
    private readonly Label _oxygen = new();
    private readonly Label _alarm = new();

    public BedsideMonitorWindow()
    {
        Title = Loc.GetString("bedside-monitor-title");
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 340, Margin = new Thickness(12) };
        ContentsContainer.AddChild(box);
        box.AddChild(_status);
        box.AddChild(_pulse);
        box.AddChild(_pressure);
        box.AddChild(_oxygen);
        box.AddChild(_alarm);
    }

    public void UpdateState(BedsideMonitorUiState state)
    {
        var active = state.Powered && state.Patient != null;
        _status.Text = !state.Powered ? Loc.GetString("bedside-monitor-unpowered")
            : !active ? Loc.GetString("bedside-monitor-no-patient") : state.PatientName;
        _pulse.Text = Loc.GetString("bedside-monitor-pulse", ("value", active ? state.Pulse.ToString("F0") : "--"));
        _pressure.Text = Loc.GetString("bedside-monitor-pressure", ("value", active ? $"{state.Systolic:F0}/{state.Diastolic:F0}" : "--"));
        _oxygen.Text = Loc.GetString("bedside-monitor-oxygen", ("value", active ? (state.OxygenDelivery * 100f).ToString("F0") : "--"));
        _alarm.Text = active ? Loc.GetString($"bedside-monitor-alarm-{state.Alarm.ToString().ToLowerInvariant()}") : string.Empty;
        _alarm.FontColorOverride = state.Alarm == BedsideMonitorAlarm.None ? Color.Green : Color.Red;
    }
}

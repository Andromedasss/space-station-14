using Content.Client.UserInterface.Controls;
using Content.Shared.Medical;
using Content.Shared.MedicalScanner;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Medical;

public sealed partial class MedicalDebugWindow : FancyWindow
{
    private readonly LineEdit _targetInput = new() { Text = "" };
    private readonly Button _targetButton = new() { Text = "Target" };
    private readonly Button _refreshButton = new() { Text = "Refresh" };
    private readonly Label _statusLabel = new() { Text = "" };
    private readonly BoxContainer _organsContainer = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly LineEdit _woundPartInput = new() { Text = "" };
    private readonly LineEdit _woundTypeInput = new() { Text = "" };
    private readonly LineEdit _woundAmountInput = new() { Text = "" };
    private readonly CheckBox _aphasiaCheck = new() { Text = "Aphasia" };
    private readonly CheckBox _weaknessCheck = new() { Text = "Weakness" };
    private readonly OptionButton _visionDropdown = new();
    private readonly LineEdit _cognitionInput = new() { Text = "" };
    private readonly OptionButton _phobiaDropdown = new();

    private readonly Dictionary<string, Slider> _organSliders = new();
    private NetEntity? _target;
    private PhysiologyScan? _scan;
    private float _temperature;
    private float _bloodLevel;

    public event Action<MedicalDebugRequestMessage>? OnRequest;
    public event Action? OnClose;

    public MedicalDebugWindow()
    {
        Title = Loc.GetString("medical-debug-title");
        MinWidth = 520;
        MinHeight = 600;

        var scroll = new ScrollContainer();
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        // Target section
        var targetBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        targetBox.AddChild(_targetInput);
        targetBox.AddChild(_targetButton);
        targetBox.AddChild(_refreshButton);
        content.AddChild(targetBox);
        content.AddChild(_statusLabel);

        // Organs section
        content.AddChild(new Label { Text = "Organ Functions" });
        foreach (var kind in new[] { "heart", "lungs", "kidney", "liver", "intestines" })
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            var label = new Label { Text = kind };
            var slider = new Slider { MinValue = 0, MaxValue = 1, Value = 1 };
            var apply = new Button { Text = "Set" };
            row.AddChild(label);
            row.AddChild(slider);
            row.AddChild(apply);
            _organsContainer.AddChild(row);
            _organSliders[kind] = slider;

            var kindName = kind;
            apply.OnPressed += _ =>
            {
                if (_target is not { } t)
                    return;
                OnRequest?.Invoke(new MedicalDebugRequestMessage
                {
                    TargetEntity = t,
                    ActionType = MedicalDebugRequestMessage.Action.SetOrganFunction,
                    StringParam1 = kindName,
                    FloatParam1 = slider.Value,
                });
            };
        }
        content.AddChild(_organsContainer);

        // Wound section
        content.AddChild(new Label { Text = "Inflict Wound" });
        var woundBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var woundApply = new Button { Text = "Inflict" };
        woundBox.AddChild(_woundPartInput);
        woundBox.AddChild(_woundTypeInput);
        woundBox.AddChild(_woundAmountInput);
        woundBox.AddChild(woundApply);
        content.AddChild(woundBox);
        woundApply.OnPressed += _ =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.InflictWound,
                StringParam1 = _woundPartInput.Text,
                StringParam2 = _woundTypeInput.Text,
                FloatParam1 = float.TryParse(_woundAmountInput.Text, out var amt) ? amt : 0f,
            });
        };

        // Neurology section
        content.AddChild(new Label { Text = "Neurology" });
        _aphasiaCheck.OnToggled += args =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.SetAphasia,
                BoolParam1 = args.Pressed,
            });
        };
        content.AddChild(_aphasiaCheck);

        _weaknessCheck.OnToggled += args =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.SetWeakness,
                BoolParam1 = args.Pressed,
            });
        };
        content.AddChild(_weaknessCheck);

        var visionBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        visionBox.AddChild(new Label { Text = "Color vision:" });
        foreach (var vision in Enum.GetValues<MedicalColorVision>())
            _visionDropdown.AddItem(vision.ToString());
        _visionDropdown.OnItemSelected += args =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.SetColorVision,
                IntParam1 = args.Id,
            });
        };
        visionBox.AddChild(_visionDropdown);
        content.AddChild(visionBox);

        var cogBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        cogBox.AddChild(new Label { Text = "Cognitive bitmask:" });
        var cogApply = new Button { Text = "Set" };
        cogBox.AddChild(_cognitionInput);
        cogBox.AddChild(cogApply);
        content.AddChild(cogBox);
        cogApply.OnPressed += _ =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.SetCognitiveSymptoms,
                IntParam1 = int.TryParse(_cognitionInput.Text, out var bm) ? bm : 0,
            });
        };

        var phobiaBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        phobiaBox.AddChild(new Label { Text = "Phobia:" });
        foreach (var phobia in Enum.GetValues<MedicalPhobia>())
            _phobiaDropdown.AddItem(phobia.ToString());
        _phobiaDropdown.OnItemSelected += args =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.SetPhobia,
                IntParam1 = args.Id,
            });
        };
        phobiaBox.AddChild(_phobiaDropdown);
        content.AddChild(phobiaBox);

        // Reset buttons
        var resetBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var resetNeuro = new Button { Text = "Reset Neurology" };
        var resetAll = new Button { Text = "Reset All" };
        resetBox.AddChild(resetNeuro);
        resetBox.AddChild(resetAll);
        content.AddChild(resetBox);
        resetNeuro.OnPressed += _ =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.ResetNeurology,
            });
        };
        resetAll.OnPressed += _ =>
        {
            if (_target is not { } t)
                return;
            OnRequest?.Invoke(new MedicalDebugRequestMessage
            {
                TargetEntity = t,
                ActionType = MedicalDebugRequestMessage.Action.ResetAll,
            });
        };

        _targetButton.OnPressed += _ =>
        {
            var text = _targetInput.Text;
            if (text.Length > 0 && (text[0] == 'n' || text[0] == 'N'))
                text = text.Substring(1);
            if (int.TryParse(text, out var id))
                SetTarget(new NetEntity(id));
            else
                _statusLabel.Text = "Invalid entity ID (use numeric ID like 12345)";
        };
        _refreshButton.OnPressed += _ => RequestRefresh();

        scroll.AddChild(content);
        ContentsContainer.AddChild(scroll);
    }

    public void SetTarget(NetEntity target)
    {
        _target = target;
        _targetInput.Text = $"n{target.Id}";
        _statusLabel.Text = $"Target: {target}";
        RequestRefresh();
    }

    public void RequestRefresh()
    {
        if (_target is not { } t)
            return;
        OnRequest?.Invoke(new MedicalDebugRequestMessage
        {
            TargetEntity = t,
            ActionType = MedicalDebugRequestMessage.Action.SetOrganFunction,
            StringParam1 = "heart",
            FloatParam1 = 1f,
        });
    }

    public void Populate(PhysiologyScan? scan, float temperature, float bloodLevel, string? error)
    {
        _scan = scan;
        _temperature = temperature;
        _bloodLevel = bloodLevel;
        if (error != null)
        {
            _statusLabel.Text = error;
            return;
        }

        if (scan == null)
        {
            _statusLabel.Text = "No physiology data";
            return;
        }

        _statusLabel.Text =
            $"Scan OK | Blood: {_bloodLevel:P0} | Temp: {_temperature - 273.15f:F1}\u00B0C | " +
            $"OxygenDebt: {scan.OxygenDebt:F1} | BrainDamage: {scan.BrainDamage:F1}";

        // Update organ sliders from scan
        foreach (var organ in scan.OrganFunctions)
        {
            if (_organSliders.TryGetValue(organ.Category, out var slider))
                slider.Value = Math.Clamp(1f - organ.Trauma / 100f, 0f, 1f);
        }

        // Update neurology checkboxes
        _aphasiaCheck.Pressed = scan.Aphasia;
        _weaknessCheck.Pressed = scan.MuscleWeakness;
        _visionDropdown.SelectId((int)scan.NeurologicalVision);
        _cognitionInput.Text = ((int)scan.CognitiveSymptoms).ToString();
        _phobiaDropdown.SelectId((int)scan.Phobia);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            OnClose?.Invoke();
        base.Dispose(disposing);
    }
}

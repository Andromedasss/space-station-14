using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared.Medical;
using Content.Shared.MedicalScanner;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Medical;

/// <summary>
/// Admin debug window for the experimental medical system. Shows the full physiology
/// scan of a target and lets admins apply test interventions.
/// </summary>
public sealed class MedicalDebugWindow : FancyWindow
{
    private static readonly string[] OrganInterventions =
        { "heart", "lungs", "kidneys", "liver", "intestines" };

    private static readonly string[] BodyParts =
    {
        "Torso", "Head", "ArmLeft", "ArmRight", "HandLeft", "HandRight",
        "LegLeft", "LegRight", "FootLeft", "FootRight", "Abdomen", "Groin",
    };

    private static readonly string[] DamageTypes = { "Blunt", "Slash", "Piercing", "Heat" };

    private readonly LineEdit _targetInput = new();
    private readonly Label _status = new();
    private readonly Label _targetName = new();

    private readonly BoxContainer _overview = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _organList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _organTrauma = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _injuryList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _surgeryList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _neuroList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };

    private readonly Dictionary<string, Slider> _organSliders = new();
    private readonly LineEdit _bloodInput = new();
    private readonly LineEdit _temperatureInput = new();
    private readonly LineEdit _bleedingInput = new();

    private readonly OptionButton _woundPart = new();
    private readonly OptionButton _woundType = new();
    private readonly LineEdit _woundAmount = new();

    private readonly CheckBox _aphasia = new() { Text = "Aphasia" };
    private readonly CheckBox _weakness = new() { Text = "Muscle weakness" };
    private readonly OptionButton _vision = new();
    private readonly OptionButton _cognition = new();
    private readonly OptionButton _phobia = new();

    private NetEntity? _target;
    private bool _updating;

    public event Action<MedicalDebugRequestMessage>? OnRequest;

    public MedicalDebugWindow()
    {
        Title = "Medical Debug";
        MinWidth = 620;
        MinHeight = 640;

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        // Header: target selection and global actions.
        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _targetInput.Text = "";
        _targetInput.HorizontalExpand = true;
        var targetButton = new Button { Text = "Target" };
        var refreshButton = new Button { Text = "Refresh" };
        var resetNeuroButton = new Button { Text = "Reset neuro" };
        var resetAllButton = new Button { Text = "Reset all" };
        header.AddChild(_targetInput);
        header.AddChild(targetButton);
        header.AddChild(refreshButton);
        header.AddChild(resetNeuroButton);
        header.AddChild(resetAllButton);
        root.AddChild(header);

        _targetName.Text = "No target";
        root.AddChild(_targetName);
        _status.Text = "";
        root.AddChild(_status);

        var tabs = new TabContainer();

        tabs.AddChild(BuildOverviewTab());
        tabs.AddChild(BuildOrganTab());
        tabs.AddChild(BuildWoundTab());
        tabs.AddChild(BuildNeuroTab());

        TabContainer.SetTabTitle(tabs.Children[0], "Overview");
        TabContainer.SetTabTitle(tabs.Children[1], "Organs");
        TabContainer.SetTabTitle(tabs.Children[2], "Wounds");
        TabContainer.SetTabTitle(tabs.Children[3], "Neurology");

        root.AddChild(tabs);
        ContentsContainer.AddChild(root);

        targetButton.OnPressed += _ => ApplyTargetFromInput();
        refreshButton.OnPressed += _ => RequestRefresh();
        resetNeuroButton.OnPressed += _ => Send(MedicalDebugRequestMessage.Action.ResetNeurology, string.Empty, 0f);
        resetAllButton.OnPressed += _ => Send(MedicalDebugRequestMessage.Action.ResetAll, string.Empty, 0f);

        _aphasia.OnToggled += args => SendToggle(MedicalDebugRequestMessage.Action.Intervene, "aphasia", args.Pressed ? 1f : 0f);
        _weakness.OnToggled += args => SendToggle(MedicalDebugRequestMessage.Action.Intervene, "weakness", args.Pressed ? 1f : 0f);
        _vision.OnItemSelected += args => Send(MedicalDebugRequestMessage.Action.Intervene, "vision", args.Id);
        _cognition.OnItemSelected += args => Send(MedicalDebugRequestMessage.Action.Intervene, "cognition", args.Id);
        _phobia.OnItemSelected += args => Send(MedicalDebugRequestMessage.Action.Intervene, "phobia", args.Id);
    }

    private Control BuildOverviewTab()
    {
        var scroll = new ScrollContainer();
        scroll.AddChild(_overview);
        return scroll;
    }

    private Control BuildOrganTab()
    {
        var scroll = new ScrollContainer();
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        content.AddChild(new Label { Text = "Organ function (0 - 1)" });
        foreach (var organ in OrganInterventions)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            var slider = new Slider { MinValue = 0, MaxValue = 1, Value = 1 };
            slider.HorizontalExpand = true;
            var set = new Button { Text = "Set" };
            var name = organ;
            set.OnPressed += _ => Send(MedicalDebugRequestMessage.Action.Intervene, name, slider.Value);
            row.AddChild(new Label { Text = organ });
            row.AddChild(slider);
            row.AddChild(set);
            content.AddChild(row);
            _organSliders[organ] = slider;
        }

        content.AddChild(new Label { Text = "Other values" });
        content.AddChild(BuildNumberRow("blood (0-1)", _bloodInput, "blood"));
        content.AddChild(BuildNumberRow("temperature (K)", _temperatureInput, "temperature"));
        content.AddChild(BuildNumberRow("bleeding (0-10)", _bleedingInput, "bleeding"));

        content.AddChild(new Label { Text = "Organ trauma" });
        content.AddChild(_organTrauma);

        content.AddChild(new Label { Text = "Internal organ injuries" });
        content.AddChild(_organList);

        scroll.AddChild(content);
        return scroll;
    }

    private Control BuildWoundTab()
    {
        var scroll = new ScrollContainer();
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        content.AddChild(new Label { Text = "Localized injuries" });
        content.AddChild(_injuryList);

        content.AddChild(new Label { Text = "Inflict wound" });
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        foreach (var part in BodyParts)
            _woundPart.AddItem(part);
        foreach (var type in DamageTypes)
            _woundType.AddItem(type);
        _woundAmount.Text = "20";
        _woundAmount.HorizontalExpand = true;
        var inflict = new Button { Text = "Inflict" };
        row.AddChild(_woundPart);
        row.AddChild(_woundType);
        row.AddChild(_woundAmount);
        row.AddChild(inflict);
        content.AddChild(row);
        inflict.OnPressed += _ =>
        {
            if (_woundPart.SelectedId < 0 || _woundType.SelectedId < 0)
                return;
            if (!float.TryParse(_woundAmount.Text, out var amount))
                return;
            Send(MedicalDebugRequestMessage.Action.InflictWound, BodyParts[_woundPart.SelectedId], amount, DamageTypes[_woundType.SelectedId]);
        };

        content.AddChild(new Label { Text = "Surgical state" });
        content.AddChild(_surgeryList);

        scroll.AddChild(content);
        return scroll;
    }

    private Control BuildNeuroTab()
    {
        var scroll = new ScrollContainer();
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        content.AddChild(new Label { Text = "Symptoms" });
        content.AddChild(_aphasia);
        content.AddChild(_weakness);

        content.AddChild(new Label { Text = "Color vision" });
        foreach (var vision in Enum.GetValues<MedicalColorVision>())
            _vision.AddItem(vision.ToString());
        content.AddChild(_vision);

        content.AddChild(new Label { Text = "Cognitive symptoms (bitmask)" });
        _cognition.AddItem("None (0)");
        _cognition.AddItem("Prosopagnosia (1)");
        _cognition.AddItem("Alexia (2)");
        _cognition.AddItem("Agraphia (4)");
        _cognition.AddItem("Tremor (8)");
        _cognition.AddItem("Dysmetria (16)");
        _cognition.AddItem("All (31)");
        content.AddChild(_cognition);

        content.AddChild(new Label { Text = "Phobia" });
        foreach (var phobia in Enum.GetValues<MedicalPhobia>())
            _phobia.AddItem(phobia.ToString());
        content.AddChild(_phobia);

        content.AddChild(new Label { Text = "Active neurological conditions" });
        content.AddChild(_neuroList);

        scroll.AddChild(content);
        return scroll;
    }

    private Control BuildNumberRow(string label, LineEdit input, string parameter)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        input.HorizontalExpand = true;
        var set = new Button { Text = "Set" };
        set.OnPressed += _ =>
        {
            if (!float.TryParse(input.Text, out var value))
                return;
            Send(MedicalDebugRequestMessage.Action.Intervene, parameter, value);
        };
        row.AddChild(new Label { Text = label });
        row.AddChild(input);
        row.AddChild(set);
        return row;
    }

    public void SetTarget(NetEntity target)
    {
        _target = target;
        _targetInput.Text = target.ToString();
        RequestRefresh();
    }

    private void ApplyTargetFromInput()
    {
        var text = _targetInput.Text;
        if (text.Length > 0 && (text[0] == 'n' || text[0] == 'N'))
            text = text.Substring(1);
        if (int.TryParse(text, out var id))
            SetTarget(new NetEntity(id));
        else
            _status.Text = "Invalid entity id";
    }

    private void RequestRefresh()
    {
        // A pure refresh must not touch the patient, so it uses its own action.
        Send(MedicalDebugRequestMessage.Action.Refresh, string.Empty, 0f);
    }

    private void Send(MedicalDebugRequestMessage.Action action, string param1, float value, string param2 = "")
    {
        if (_target is not { } target)
        {
            _status.Text = "No target selected";
            return;
        }

        OnRequest?.Invoke(new MedicalDebugRequestMessage
        {
            TargetEntity = target,
            ActionType = action,
            StringParam1 = param1,
            StringParam2 = param2,
            FloatParam1 = value,
        });
    }

    private void SendToggle(MedicalDebugRequestMessage.Action action, string param, float value)
    {
        if (_updating)
            return;
        Send(action, param, value);
    }

    public void Populate(MedicalDebugStateMessage msg)
    {
        // The error is shown, but the data is still filled in.
        _status.Text = msg.Error != null ? "Error: " + msg.Error : "";
        _targetName.Text = msg.Valid ? $"Target: {msg.TargetName} ({msg.TargetEntity})" : "Target has no physiology";

        var scan = msg.Scan;
        if (scan == null)
        {
            Fill(_overview, new[] { "No physiology data" });
            Fill(_organTrauma, Array.Empty<string>());
            Fill(_organList, Array.Empty<string>());
            Fill(_injuryList, Array.Empty<string>());
            Fill(_surgeryList, Array.Empty<string>());
            Fill(_neuroList, Array.Empty<string>());
            return;
        }

        _updating = true;
        try
        {
            Fill(_overview, OverviewLines(msg, scan));

            foreach (var (organ, slider) in _organSliders)
            {
                var value = organ switch
                {
                    "heart" => scan.HeartFunction,
                    "lungs" => scan.LungFunction,
                    "kidneys" => scan.KidneyFunction,
                    "liver" => scan.LiverFunction,
                    "intestines" => scan.IntestinalFunction,
                    _ => 1f,
                };
                slider.Value = Math.Clamp(value, 0f, 1f);
            }

            _bloodInput.Text = msg.BloodLevel.ToString("F2");
            _temperatureInput.Text = msg.Temperature.ToString("F1");

            _aphasia.Pressed = scan.Aphasia;
            _weakness.Pressed = scan.MuscleWeakness;
            _vision.SelectId((int) scan.NeurologicalVision);
            _phobia.SelectId((int) scan.Phobia);
            _cognition.SelectId(CognitionPresetId((int) scan.CognitiveSymptoms));
        }
        finally
        {
            _updating = false;
        }

        Fill(_organTrauma, scan.OrganFunctions.Select(o => $"{o.Category}: trauma {o.Trauma:F1}"));
        Fill(_organList, scan.OrganInjuries.Select(o => $"{o.Category}: trauma {o.Trauma:F1}"));
        Fill(_injuryList, scan.Injuries.Select(i => $"{i.Category}: damage {i.Damage:F1}, bleeding {i.Bleeding:F1}"));
        Fill(_surgeryList, SurgeryLines(scan));
        Fill(_neuroList, scan.NeurologicalConditions.Select(c => $"{c.Condition}: recovering in {c.RecoverySeconds:F0}s"));
    }

    private static IEnumerable<string> OverviewLines(MedicalDebugStateMessage msg, PhysiologyScan scan)
    {
        yield return "--- vitals ---";
        yield return $"Temperature: {msg.Temperature - 273.15f:F1} C ({msg.Temperature:F1} K)";
        yield return $"Blood level: {msg.BloodLevel * 100f:F1} %";
        yield return $"Pulse: {scan.Pulse:F0}";
        yield return $"Blood pressure: {scan.Systolic:F0}/{scan.Diastolic:F0}";
        yield return $"Mobility: {scan.Mobility:F2}";
        yield return $"Pain: {scan.Pain:F1} (burden {scan.PainBurden:F1})";
        yield return $"Respiratory drive: {scan.RespiratoryDrive:F2}";
        yield return $"Sedated: {scan.Sedated}";

        yield return "--- metabolism ---";
        yield return $"Metabolic demand: {scan.MetabolicDemand:F2}";
        yield return $"Circulatory function: {scan.CirculatoryFunction:F2}";
        yield return $"Renal waste: {scan.RenalWaste:F2}";
        yield return $"Hepatic waste: {scan.HepaticWaste:F2}";
        yield return $"Contamination: {scan.Contamination:F1}";

        yield return "--- respiration ---";
        yield return $"Lung function: {scan.LungFunction:F2}";
        yield return $"Oxygen delivery: {scan.OxygenDelivery:F2}";
        yield return $"Oxygen debt: {scan.OxygenDebt:F2}";

        yield return "--- organs ---";
        yield return $"Heart: {scan.HeartFunction:F2}";
        yield return $"Kidneys: {scan.KidneyFunction:F2}";
        yield return $"Liver: {scan.LiverFunction:F2}";
        yield return $"Intestines: {scan.IntestinalFunction:F2}";

        yield return "--- neurology ---";
        yield return $"Brain damage: {scan.BrainDamage:F1} (missing: {scan.BrainMissing})";
        yield return $"Fear: {scan.Fear:F1}";
        yield return $"Aphasia: {scan.Aphasia}";
        yield return $"Muscle weakness: {scan.MuscleWeakness}";
        yield return $"Cognitive symptoms: {scan.CognitiveSymptoms}";
        yield return $"Phobia: {scan.Phobia}";
        yield return $"Color vision: {scan.NeurologicalVision}";

        yield return "--- support ---";
        yield return $"Life support: {scan.LifeSupport} ({scan.LifeSupportGasMoles:F2} mol)";
        yield return $"CPR active: {scan.CprActive}";
        yield return $"Shockable rhythm: {scan.ShockableRhythm}";
        yield return $"Inotrope dose: {scan.InotropeDose:F2}";
        yield return $"Sedative dose: {scan.SedativeDose:F2}";
        yield return $"Analgesic dose: {scan.AnalgesicDose:F2}";
        yield return $"Neurorestorative dose: {scan.NeurorestorativeDose:F2}";
        yield return $"Anxiolytic dose: {scan.AnxiolyticDose:F2}";
        yield return $"Internal warning: {scan.InternalWarning}";
        yield return $"Neurological warning: {scan.NeurologicalWarning}";
    }

    private static IEnumerable<string> SurgeryLines(PhysiologyScan scan)
    {
        foreach (var site in scan.SurgicalSites)
            yield return $"{site.Category}: access stage {site.Stage}";
        foreach (var site in scan.BoneSites)
            yield return $"{site.Category}: bone stage {site.Stage}";
        foreach (var fracture in scan.Fractures)
            yield return $"Fracture: {fracture}";
        foreach (var vascular in scan.VascularInjuries)
            yield return $"{vascular.Category}: vessel {vascular.Damage:F1}, internal bleeding {vascular.InternalBleeding:F1}";
        foreach (var necrotic in scan.NecroticParts)
            yield return $"Necrotic: {necrotic}";
        foreach (var prosthetic in scan.ProstheticParts)
            yield return $"Prosthetic: {prosthetic}";
        foreach (var storage in scan.StorageInjuries)
            yield return $"Storage {storage.Category}: trauma {storage.Trauma:F1}";
        foreach (var hygiene in scan.WoundHygiene)
            yield return $"Hygiene {hygiene.Category}: contamination {hygiene.Contamination:F1}, infection {hygiene.Infection:F1}";
    }

    private static int CognitionPresetId(int flags)
    {
        return flags switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            4 => 3,
            8 => 4,
            16 => 5,
            _ => 6,
        };
    }

    private static void Fill(BoxContainer container, IEnumerable<string> lines)
    {
        container.RemoveAllChildren();
        var any = false;
        foreach (var line in lines)
        {
            any = true;
            container.AddChild(new Label { Text = line });
        }

        if (!any)
            container.AddChild(new Label { Text = "-" });
    }
}

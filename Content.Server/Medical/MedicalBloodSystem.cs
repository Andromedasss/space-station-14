using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.MedicalScanner;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Medical;

/// <summary>
/// Blood handling of the experimental patient: blood groups, transfusion, drawing blood
/// and analysing it in the laboratory.
/// </summary>
public sealed class MedicalBloodSystem : EntitySystem
{
    /// <summary>The reagent the bloodstream of every human carries.</summary>
    public const string BloodReagent = "CopperBlood";

    [Dependency] private PhysiologySystem _physiology = default!;
    [Dependency] private HealthAnalyzerSystem _health = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysiologyComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<BloodBagComponent, AfterInteractEvent>(OnTransfuse);
        SubscribeLocalEvent<MedicalBloodSampleComponent, AfterInteractEvent>(OnDrawSample);
        SubscribeLocalEvent<MedicalLaboratoryComponent, InteractUsingEvent>(OnAnalyze);
        SubscribeLocalEvent<MedicalBloodSampleComponent, ExaminedEvent>(OnExamine);
    }

    /// <summary>
    /// Every patient is assigned a blood group once, and it never changes.
    /// </summary>
    private void OnComponentInit(Entity<PhysiologyComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.BloodGroup != BloodGroup.ONegative)
            return;

        ent.Comp.BloodGroup = (BloodGroup) _random.Next(0, 8);
        Dirty(ent, ent.Comp);
    }

    /// <summary>
    /// Transfusing a bag raises the blood level of the patient. An incompatible bag breaks
    /// the donor cells apart, taking the patient's own blood with them.
    /// </summary>
    private void OnTransfuse(Entity<BloodBagComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } patient)
            return;

        if (!TryComp<PhysiologyComponent>(patient, out var physiology)
            || !TryComp<BloodstreamComponent>(patient, out var blood))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("medical-blood-no-patient"), ent, args.User);
            return;
        }

        if (ent.Comp.Volume <= 0f)
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("medical-blood-empty-bag"), ent, args.User);
            return;
        }

        args.Handled = true;
        var donor = ent.Comp.Group;
        var recipient = physiology.BloodGroup;
        var current = _health.GetHealthAnalyzerUiState(patient).BloodLevel;

        if (!BloodGroups.CanDonate(donor, recipient))
        {
            // The foreign cells break apart and the patient loses half of their own blood.
            _physiology.TryIntervene(patient, "blood", current * 0.5f);
            _blood.TryModifyBleedAmount((patient, blood), 4f - blood.BleedAmount);
            ent.Comp.Volume = 0f;
            Dirty(ent, ent.Comp);
            _popup.PopupEntity(Loc.GetString("medical-blood-incompatible"), ent, args.User);
            return;
        }

        var target = Math.Clamp(current + ent.Comp.Volume, 0f, 1f);
        _physiology.TryIntervene(patient, "blood", target);

        ent.Comp.Volume = 0f;
        Dirty(ent, ent.Comp);
        _popup.PopupEntity(Loc.GetString("medical-blood-transfused"), ent, args.User);
    }

    /// <summary>
    /// Drawing blood fills the vial with the group and the drug levels of the patient.
    /// </summary>
    private void OnDrawSample(Entity<MedicalBloodSampleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } patient)
            return;

        if (!TryComp<PhysiologyComponent>(patient, out var physiology))
            return;

        var scan = _physiology.Scan(patient, detailed: true);
        if (scan == null)
            return;

        args.Handled = true;
        ent.Comp.Group = physiology.BloodGroup;
        ent.Comp.Analyzed = false;
        ent.Comp.BloodVolume = _health.GetHealthAnalyzerUiState(patient).BloodLevel;
        ent.Comp.Sedative = scan.SedativeDose;
        ent.Comp.Analgesic = scan.AnalgesicDose;
        ent.Comp.Neurorestorative = scan.NeurorestorativeDose;
        ent.Comp.Anxiolytic = scan.AnxiolyticDose;
        ent.Comp.Inotrope = scan.InotropeDose;
        ent.Comp.Contamination = scan.Contamination;
        ent.Comp.Infection = scan.WoundHygiene.Sum(h => h.Infection);
        Dirty(ent, ent.Comp);

        _popup.PopupEntity(Loc.GetString("medical-blood-sample-taken"), ent, args.User);
    }

    /// <summary>
    /// The laboratory turns a drawn sample into a result the player can read.
    /// </summary>
    private void OnAnalyze(Entity<MedicalLaboratoryComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<MedicalBloodSampleComponent>(args.Used, out var sample))
            return;

        args.Handled = true;
        sample.Analyzed = true;
        Dirty(args.Used, sample);
        _popup.PopupEntity(Loc.GetString("medical-blood-sample-analysed"), ent, args.User);
    }

    /// <summary>
    /// Reading an analysed sample shows the result, an unprocessed one shows nothing useful.
    /// </summary>
    private void OnExamine(Entity<MedicalBloodSampleComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Analyzed)
        {
            args.PushMarkup(Loc.GetString("medical-blood-sample-raw"));
            return;
        }

        args.PushMarkup(Loc.GetString("medical-blood-sample-result",
            ("group", BloodGroups.Name(ent.Comp.Group)),
            ("volume", ent.Comp.BloodVolume * 100f),
            ("sedative", ent.Comp.Sedative),
            ("analgesic", ent.Comp.Analgesic),
            ("neurorestorative", ent.Comp.Neurorestorative),
            ("anxiolytic", ent.Comp.Anxiolytic),
            ("inotrope", ent.Comp.Inotrope),
            ("contamination", ent.Comp.Contamination),
            ("infection", ent.Comp.Infection)));
    }
}

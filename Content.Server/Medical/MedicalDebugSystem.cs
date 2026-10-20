using Content.Server.Administration.Managers;
using Content.Shared.Body;
using Content.Shared.Medical;
using Content.Shared.Medical.HealthAnalyzer;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Medical;

/// <summary>
/// Server side of the medical debug window. Lets admins inspect the full experimental
/// physiology of an entity and apply test interventions, mirroring the <c>medical</c>
/// console command but with a UI.
/// </summary>
public sealed class MedicalDebugSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private PhysiologySystem _physiology = default!;
    [Dependency] private MedicalWoundSystem _wounds = default!;
    [Dependency] private MedicalNeurologySystem _neurology = default!;
    [Dependency] private HealthAnalyzerSystem _health = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<MedicalDebugRequestMessage>(OnRequest);
    }

    private void OnRequest(MedicalDebugRequestMessage msg, EntitySessionEventArgs session)
    {
        Log.Debug($"medical debug request {msg.ActionType} target={msg.TargetEntity} p1='{msg.StringParam1}' p2='{msg.StringParam2}' v={msg.FloatParam1}");

        if (!_admins.IsAdmin(session.SenderSession))
        {
            SendState(msg.TargetEntity, session, "Admin rights required.");
            return;
        }

        if (!_entities.TryGetEntity(msg.TargetEntity, out var targetNet) || targetNet is not { } target)
        {
            SendState(msg.TargetEntity, session, $"Entity {msg.TargetEntity} not found.");
            return;
        }

        string? error = null;
        try
        {
            error = Apply(msg, target);
        }
        catch (Exception e)
        {
            error = e.Message;
        }

        SendState(msg.TargetEntity, session, error);
    }

    private string? Apply(MedicalDebugRequestMessage msg, EntityUid target)
    {
        switch (msg.ActionType)
        {
            case MedicalDebugRequestMessage.Action.Refresh:
                return null;

            case MedicalDebugRequestMessage.Action.Intervene:
                if (!_physiology.TryIntervene(target, msg.StringParam1, msg.FloatParam1))
                    return $"Unsupported intervention '{msg.StringParam1}' for this patient.";
                return null;

            case MedicalDebugRequestMessage.Action.InflictWound:
                if (!_wounds.TryInjure(target, msg.StringParam1, msg.StringParam2, msg.FloatParam1))
                    return "Could not apply the wound to that body part.";
                return null;

            case MedicalDebugRequestMessage.Action.ResetNeurology:
                ResetNeurology(target);
                return null;

            case MedicalDebugRequestMessage.Action.ResetAll:
                ResetAll(target);
                return null;

            default:
                return "Unknown action.";
        }
    }

    private void ResetNeurology(EntityUid target)
    {
        foreach (var (_, _, brain) in _body.EnumerateOrgans<CerebralHealthComponent>(target))
        {
            brain.RecoveryRemaining.Clear();
            brain.ColorVision = MedicalColorVision.Normal;
            brain.Aphasia = false;
            brain.MuscleWeakness = false;
            brain.CognitiveSymptoms = MedicalCognitiveSymptoms.None;
            brain.Phobia = MedicalPhobia.None;
            brain.Fear = brain.FearUnconsciousSeconds = 0f;
            brain.FearSampleSeconds = brain.FearRecoverySeconds = 0f;
            brain.VisibleFearTriggers = 0;
        }

        _neurology.Refresh(target);
    }

    private void ResetAll(EntityUid target)
    {
        ResetNeurology(target);

        foreach (var (_, _, organ) in _body.EnumerateOrgans<MedicalOrganComponent>(target))
            organ.Function = 1f;

        foreach (var (_, _, wound) in _body.EnumerateOrgans<MedicalWoundComponent>(target))
        {
            wound.Injury = new Content.Shared.Damage.DamageSpecifier();
            wound.Bleeding = 0f;
            wound.Contamination = wound.Infection = wound.AntisepticSeconds = 0f;
            wound.Fractured = false;
            wound.Necrotic = false;
            wound.VascularDamage = 0f;
        }
    }

    private void SendState(NetEntity target, EntitySessionEventArgs session, string? error)
    {
        var state = new MedicalDebugStateMessage
        {
            TargetEntity = target,
            Error = error,
        };

        // The scan is always sent, even when the action failed, so the window can show it.
        if (_entities.TryGetEntity(target, out var entityNet) && entityNet is { } entity)
        {
            var health = _health.GetHealthAnalyzerUiState(entity, false, true);
            state.Valid = health.Physiology != null;
            state.Temperature = health.Temperature;
            state.BloodLevel = health.BloodLevel;
            state.Bleeding = health.Bleeding;
            state.Unrevivable = health.Unrevivable;
            state.Scan = health.Physiology;
            state.TargetName = _entities.GetComponent<MetaDataComponent>(entity).EntityName;
        }

        RaiseNetworkEvent(state, session.SenderSession);
    }
}

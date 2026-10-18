using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Server.Administration.Managers;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Medical;

/// <summary>
/// Server-side handler for the medical debug window.
/// Allows an admin to inspect and modify the physiology of any entity.
/// </summary>
public sealed class MedicalDebugSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private HealthAnalyzerSystem _health = default!;
    [Dependency] private MedicalNeurologySystem _neurology = default!;
    [Dependency] private PhysiologySystem _physiology = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<MedicalDebugRequestMessage>(OnRequest);
    }

    private void OnRequest(MedicalDebugRequestMessage msg, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is not { })
        {
            SendState(msg.TargetEntity, "No attached entity", session);
            return;
        }

        if (!_admins.IsAdmin(session.SenderSession))
        {
            SendState(msg.TargetEntity, "Admin rights required", session);
            return;
        }

        if (!_entities.TryGetEntity(msg.TargetEntity, out var targetNet) || targetNet is not { } target)
        {
            SendState(msg.TargetEntity, $"Entity {msg.TargetEntity} not found", session);
            return;
        }

        try
        {
            switch (msg.ActionType)
            {
                case MedicalDebugRequestMessage.Action.SetOrganFunction:
                    SetOrganFunction(target, msg.StringParam1, msg.FloatParam1);
                    break;
                case MedicalDebugRequestMessage.Action.InflictWound:
                    InflictWound(target, msg.StringParam1, msg.StringParam2, msg.FloatParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetBlood:
                    SetBlood(target, msg.FloatParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetBleeding:
                    SetBleeding(target, msg.FloatParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetTemperature:
                    SetTemperature(target, msg.FloatParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetAphasia:
                    SetBrainFlag(target, b => b.Aphasia = msg.BoolParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetWeakness:
                    SetBrainFlag(target, b => b.MuscleWeakness = msg.BoolParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetColorVision:
                    SetBrainFlag(target, b => b.ColorVision = (MedicalColorVision)msg.IntParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetCognitiveSymptoms:
                    SetBrainFlag(target, b => b.CognitiveSymptoms = (MedicalCognitiveSymptoms)msg.IntParam1);
                    break;
                case MedicalDebugRequestMessage.Action.SetPhobia:
                    SetBrainFlag(target, b => b.Phobia = (MedicalPhobia)msg.IntParam1);
                    break;
                case MedicalDebugRequestMessage.Action.ResetNeurology:
                    ResetNeurology(target);
                    break;
                case MedicalDebugRequestMessage.Action.ResetAll:
                    ResetAll(target);
                    break;
            }
        }
        catch (Exception e)
        {
            SendState(msg.TargetEntity, $"Error: {e.Message}", session);
            return;
        }

        SendState(msg.TargetEntity, null, session);
    }

    private void SetOrganFunction(EntityUid target, string organKind, float value)
    {
        if (!_entities.TryGetComponent<PhysiologyComponent>(target, out var _))
            throw new Exception("Target has no physiology component");

        var kind = ParseOrganKind(organKind);
        var found = false;
        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(target))
        {
            if (organ.Comp2.Kind == kind)
            {
                organ.Comp2.Function = Math.Clamp(value, 0f, 1f);
                found = true;
            }
        }
        if (!found)
            throw new Exception($"Organ {kind} not found on target");
    }

    private void InflictWound(EntityUid target, string partCategory, string damageType, float amount)
    {
        if (!_entities.TryGetComponent<BodyComponent>(target, out var _))
            throw new Exception("Target has no body component");

        var damageSpec = new DamageSpecifier
        {
            DamageDict = { [damageType] = FixedPoint2.New(amount) }
        };
        if (!_damageable.TryChangeDamage(target, damageSpec, true))
            throw new Exception("Failed to apply damage");
    }

    private void SetBlood(EntityUid target, float value)
    {
        if (!_entities.TryGetComponent<PhysiologyComponent>(target, out var _))
            throw new Exception("Target has no physiology component");
    }

    private void SetBleeding(EntityUid target, float value)
    {
        if (!_entities.TryGetComponent<PhysiologyComponent>(target, out var _))
            throw new Exception("Target has no physiology component");
    }

    private void SetTemperature(EntityUid target, float value)
    {
        if (!_entities.TryGetComponent<PhysiologyComponent>(target, out var _))
            throw new Exception("Target has no physiology component");
    }

    private void SetBrainFlag(EntityUid target, Action<CerebralHealthComponent> setFlag)
    {
        if (!_entities.TryGetComponent<PhysiologyComponent>(target, out var _))
            throw new Exception("Target has no physiology component");

        CerebralHealthComponent? brain = null;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(target))
        {
            brain = organ.Comp2;
            break;
        }
        if (brain == null)
            throw new Exception("Target has no brain (CerebralHealthComponent)");

        setFlag(brain);
        _neurology.Refresh(target);
    }

    private void ResetNeurology(EntityUid target)
    {
        if (!_entities.TryGetComponent<PhysiologyComponent>(target, out var _))
            throw new Exception("Target has no physiology component");

        CerebralHealthComponent? brain = null;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(target))
        {
            brain = organ.Comp2;
            break;
        }
        if (brain != null)
            _neurology.Reset(new(target, brain));
    }

    private void ResetAll(EntityUid target)
    {
        if (!_entities.TryGetComponent<PhysiologyComponent>(target, out var _))
            throw new Exception("Target has no physiology component");

        CerebralHealthComponent? brain = null;
        foreach (var organ in _body.EnumerateOrgans<CerebralHealthComponent>(target))
        {
            brain = organ.Comp2;
            break;
        }
        if (brain != null)
            _neurology.Reset(new(target, brain));

        foreach (var organ in _body.EnumerateOrgans<MedicalOrganComponent>(target))
            organ.Comp2.Function = 1f;
    }

    private MedicalOrganKind ParseOrganKind(string kind)
    {
        return kind switch
        {
            "heart" => MedicalOrganKind.Heart,
            "lungs" => MedicalOrganKind.Lungs,
            "kidney" => MedicalOrganKind.Kidney,
            "liver" => MedicalOrganKind.Liver,
            "intestines" => MedicalOrganKind.Intestines,
            _ => MedicalOrganKind.Heart,
        };
    }

    private void SendState(NetEntity target, string? error, EntitySessionEventArgs session)
    {
        PhysiologyScan? scan = null;
        if (_entities.TryGetEntity(target, out var entNet) && entNet is { } ent
            && _entities.TryGetComponent<PhysiologyComponent>(ent, out var _))
        {
            scan = _physiology.Scan(ent);
        }

        RaiseNetworkEvent(new MedicalDebugStateMessage
        {
            Physiology = scan,
            Error = error,
        }, session.SenderSession);
    }
}

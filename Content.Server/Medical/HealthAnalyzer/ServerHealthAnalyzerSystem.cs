using Content.Server.Medical;
using Content.Shared.Medical.HealthAnalyzer;
using Content.Shared.MedicalScanner;
using Content.Shared.Temperature.Components;

namespace Content.Server.Medical.HealthAnalyzer;

/// <inheritdoc/>
public sealed partial class ServerHealthAnalyzerSystem : HealthAnalyzerSystem
{
    [Dependency] private EntityQuery<TemperatureComponent> _temperatureQuery;

    public override HealthAnalyzerUiState GetHealthAnalyzerUiState(EntityUid? target, bool scanMode, bool detailed = false)
    {
        var state = base.GetHealthAnalyzerUiState(target, scanMode, detailed);

        if (_temperatureQuery.TryComp(target, out var temp))
            state.Temperature = temp.Temperature;

        // Experimental physiology observations are only available server side.
        if (target is { } entity)
            state.Physiology = EntityManager.System<PhysiologySystem>().Scan(entity, detailed);

        return state;
    }
}

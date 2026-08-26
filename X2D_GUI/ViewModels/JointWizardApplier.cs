using System.Linq;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.ViewModels;

public sealed class JointWizardApplyReport
{
    public bool Success { get; init; }
    public int Added { get; init; }
    public bool Updated { get; init; }
    public string SummaryText { get; init; } = "";
}

/// <summary>Commits Joint Wizard polylines onto <see cref="GeometryDefinitionViewModel.GeologicalFeatures"/>.</summary>
public sealed class JointWizardApplier
{
    private readonly GeometryDefinitionViewModel _geometry;

    public JointWizardApplier(GeometryDefinitionViewModel geometry)
    {
        _geometry = geometry;
    }

    public JointWizardApplyReport Apply(JointWizardViewModel wizard)
    {
        if (wizard.IsJointNetwork)
            return ApplyNetwork(wizard);

        var features = wizard.BuildFeatures();
        if (features.Count == 0)
            return new JointWizardApplyReport { SummaryText = "No joint geometry to apply." };

        if (wizard.IsEditing && wizard.SelectedExisting != null && features.Count == 1)
        {
            _geometry.UpdateGeologicalFeature(wizard.SelectedExisting, features[0]);
            return new JointWizardApplyReport
            {
                Success = true,
                Updated = true,
                SummaryText = $"Updated {features[0].Name}."
            };
        }

        foreach (var f in features)
            _geometry.AddGeologicalFeature(f);

        return new JointWizardApplyReport
        {
            Success = true,
            Added = features.Count,
            SummaryText = features.Count == 1
                ? $"Added {features[0].Name}."
                : $"Added {features.Count} joints ({string.Join(", ", features.Select(f => f.Name))})."
        };
    }

    private JointWizardApplyReport ApplyNetwork(JointWizardViewModel wizard)
    {
        if (wizard.SelectedNetwork == null)
            return new JointWizardApplyReport { SummaryText = "No joint network selected." };

        wizard.CommitSelectedNetworkProperties();
        var def = wizard.SelectedNetwork;
        _geometry.UpsertJointNetwork(def);
        var features = wizard.BuildFeatures();
        _geometry.ReplaceJointNetworkTraces(def.Id, features);
        return new JointWizardApplyReport
        {
            Success = true,
            Added = features.Count,
            SummaryText = features.Count == 0
                ? $"Regenerated {def.Name}: no traces in the selected region(s)."
                : $"Regenerated {def.Name}: {features.Count} trace(s) in the selected region(s)."
        };
    }
}

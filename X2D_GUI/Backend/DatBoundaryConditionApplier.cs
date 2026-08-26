using System.Collections.Generic;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Maps p610/p64 <c>node fix_x fix_y</c> triplets (0=restrained, 1=free) to BC DataGrid rows.
/// </summary>
public static class DatBoundaryConditionApplier
{
    public static void Apply(
        BoundaryConditionsViewModel vm,
        IReadOnlyList<(int node, int fx, int fy)> bcs)
    {
        if (vm == null) return;
        vm.BoundaryConditions.Clear();
        foreach (var (node, fx, fy) in bcs)
        {
            if (fx == 0 && fy == 0)
            {
                vm.BoundaryConditions.Add(new BoundaryConditionRow
                {
                    NodeId = node,
                    BCType = "Fixed",
                    AnalysisCategory = BoundaryConditionAnalysisCategories.Structural,
                    BoundaryName = $"Node {node}",
                    Description = "Both DOFs restrained",
                });
            }
            else if (fx == 0)
            {
                vm.BoundaryConditions.Add(new BoundaryConditionRow
                {
                    NodeId = node,
                    BCType = "Roller X",
                    AnalysisCategory = BoundaryConditionAnalysisCategories.Structural,
                    DegreeOfFreedom = 0,
                    BoundaryName = $"Node {node}",
                    Description = "x restrained (roller on y-axis plane)",
                });
            }
            else if (fy == 0)
            {
                vm.BoundaryConditions.Add(new BoundaryConditionRow
                {
                    NodeId = node,
                    BCType = "Roller Y",
                    AnalysisCategory = BoundaryConditionAnalysisCategories.Structural,
                    DegreeOfFreedom = 1,
                    BoundaryName = $"Node {node}",
                    Description = "y restrained (roller on x-axis plane)",
                });
            }
        }
    }
}

using System.Collections.Generic;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class BoundaryConditionFixityMapperTests
{
    [Test]
    public void BuildFixMap_Q8CornerBoundaryRestraints_PropagatesBoundaryMidsides()
    {
        var rows = new List<MeshInformationRow>
        {
            new()
            {
                ElementId = 1,
                Kind = "Q8",
                Node1 = 1, N1X = 0, N1Y = 0,
                Node2 = 2, N2X = 2, N2Y = 0,
                Node3 = 3, N3X = 2, N3Y = 2,
                Node4 = 4, N4X = 0, N4Y = 2,
                Node5 = 5, N5X = 1, N5Y = 0,
                Node6 = 6, N6X = 2, N6Y = 1,
                Node7 = 7, N7X = 1, N7Y = 2,
                Node8 = 8, N8X = 0, N8Y = 1,
            }
        };

        var bcVm = new BoundaryConditionsViewModel();
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 1, BCType = "Fixed", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 2, BCType = "Fixed", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 3, BCType = "Roller X", DegreeOfFreedom = 0 });
        bcVm.BoundaryConditions.Add(new BoundaryConditionRow { NodeId = 4, BCType = "Roller X", DegreeOfFreedom = 0 });

        var map = BoundaryConditionFixityMapper.BuildFixMap(bcVm, rows, nn: 8);

        Assert.That(map[5], Is.EqualTo((0, 0)), "Bottom-edge midside should inherit the fully fixed base restraint.");
        Assert.That(map[6], Is.EqualTo((0, 1)), "Right-edge midside should inherit the shared X restraint.");
        Assert.That(map[7], Is.EqualTo((0, 1)), "Top-edge midside should inherit the shared X restraint.");
        Assert.That(map[8], Is.EqualTo((0, 1)), "Left-edge midside should inherit the shared X restraint.");
        Assert.That(map.Count, Is.EqualTo(8), "All four boundary midsides should be exported as restrained solver nodes.");
    }
}

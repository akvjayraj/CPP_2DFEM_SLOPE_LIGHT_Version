using System.Collections.Generic;
using System.Linq;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Persistence;

/// <summary>Serializes <see cref="P610T3RunResult"/> for .xfem so Results Visualization restores after reload.</summary>
public static class XfemFeRunSnapshot
{
    public static XfemFeRunState? Capture(ResultsViewModel results)
    {
        var run = results.LastP610Run;
        if (run == null) return null;

        return new XfemFeRunState
        {
            Description = run.Description,
            Meta = run.Meta == null ? null : new XfemFeRunMeta
            {
                Cx = run.Meta.Cx,
                Cy = run.Meta.Cy,
                A = run.Meta.A,
                SigmaV_kPa = run.Meta.SigmaV_kPa,
                K0 = run.Meta.K0,
                E_kPa = run.Meta.E_kPa,
                Nu = run.Meta.Nu,
                PhiDeg = run.Meta.PhiDeg,
                C_kPa = run.Meta.C_kPa,
                PsiDeg = run.Meta.PsiDeg,
                Gamma = run.Meta.Gamma,
                SrfFoS = run.Meta.SrfFoS,
            },
            Nodes = run.Nodes.Select(n => new XfemFeNode
            {
                Id = n.Id,
                X = n.X,
                Y = n.Y,
                Ux = n.Displacements.Length > 0 ? n.Displacements[0] : 0,
                Uy = n.Displacements.Length > 1 ? n.Displacements[1] : 0,
            }).ToList(),
            Elements = run.Elements.Select(CaptureElement).ToList(),
            Joints = run.Joints.Select(CaptureJoint).ToList(),
            Stages = run.Stages.Select(s => new XfemFeStage
            {
                Index = s.Index,
                Label = s.Label,
                Subtitle = s.Subtitle,
                ElementsSolid = s.ElementsSolid,
                Displacements = s.DisplacementsByNodeIndex.Select(d => new XfemFeDisp { Ux = d.ux, Uy = d.uy }).ToList(),
                Joints = s.Joints.Select(CaptureJoint).ToList(),
                ElementStresses = s.ElementStresses.Select(st => new XfemFeElementStress
                {
                    ElementId = st.ElementId,
                    StressXX = st.StressXX,
                    StressYY = st.StressYY,
                    StressXY = st.StressXY,
                    PrincipalStressMax = st.PrincipalStressMax,
                    PrincipalStressMin = st.PrincipalStressMin,
                    VonMisesStress = st.VonMisesStress,
                    PorePressure = st.PorePressure,
                    EffectiveStressXX = st.EffectiveStressXX,
                    EffectiveStressYY = st.EffectiveStressYY,
                    EffectiveStressXY = st.EffectiveStressXY,
                    EffectivePrincipalMax = st.EffectivePrincipalMax,
                    EffectivePrincipalMin = st.EffectivePrincipalMin,
                    YieldRatio = st.YieldRatio,
                    PlasticStrainNorm = st.PlasticStrainNorm,
                }).ToList(),
            }).ToList(),
            LastDatContent = results.LastP610RunDatContent,
            FemCppArtifactSummary = results.FemCppArtifactSummary,
            SelectedStageListIndex = ResolveSelectedStageListIndex(results),
        };
    }

    public static void Apply(ResultsViewModel results, XfemFeRunState? dto, MeshViewModel? mesh)
    {
        if (dto == null) return;
        var rebuilt = RebuildRun(dto);
        if (rebuilt == null) return;

        var msg = $"Restored FE results from project ({rebuilt.Nodes.Count} nodes, {rebuilt.Elements.Count} elements).";
        results.LoadP610T3Result(rebuilt, msg, datContent: dto.LastDatContent, meshGeometrySource: mesh);
        if (!string.IsNullOrWhiteSpace(dto.FemCppArtifactSummary))
        {
            results.SetFemCppArtifactSummary(dto.FemCppArtifactSummary);
            results.ApplyP64ResSummaryFromArtifact(dto.FemCppArtifactSummary, dto.LastDatContent);
        }

        bool hasSrfTrials = P64ResFileParser.TryParseTrialsFromArtifactSummary(
            dto.FemCppArtifactSummary, out _);
        if (!hasSrfTrials
            && dto.SelectedStageListIndex is int idx
            && idx >= 0
            && results.AvailableStages.Count > 0)
        {
            idx = Math.Min(idx, results.AvailableStages.Count - 1);
            results.SelectedStageInfo = results.AvailableStages[idx];
        }
    }

    private static int? ResolveSelectedStageListIndex(ResultsViewModel results)
    {
        if (results.SelectedStageInfo is not P610T3ResultsStageListItem item)
            return null;
        return item.StageListIndex;
    }

    private static P610T3RunResult? RebuildRun(XfemFeRunState dto)
    {
        if (dto.Nodes == null || dto.Nodes.Count == 0 || dto.Elements == null)
            return null;

        var nodes = dto.Nodes.Select(n => new Node
        {
            Id = n.Id,
            X = n.X,
            Y = n.Y,
            Displacements = new[] { n.Ux, n.Uy },
        }).ToList();

        var elements = dto.Elements.Select(e => RebuildElement(e)).Where(e => e != null).Cast<ElementBase>().ToList();

        var stages = (dto.Stages ?? new List<XfemFeStage>()).Select(s => new P610T3StageSnapshot
        {
            Index = s.Index,
            Label = s.Label ?? "",
            Subtitle = s.Subtitle,
            ElementsSolid = s.ElementsSolid,
            DisplacementsByNodeIndex = (s.Displacements ?? new List<XfemFeDisp>())
                .Select(d => (d.Ux, d.Uy)).ToList(),
            Joints = (s.Joints ?? new List<XfemFeJoint>()).Select(RebuildJoint).ToList(),
            ElementStresses = (s.ElementStresses ?? new List<XfemFeElementStress>())
                .Select(st => new ElementStress
                {
                    ElementId = st.ElementId,
                    StressXX = st.StressXX,
                    StressYY = st.StressYY,
                    StressXY = st.StressXY,
                    PrincipalStressMax = st.PrincipalStressMax,
                    PrincipalStressMin = st.PrincipalStressMin,
                    VonMisesStress = st.VonMisesStress,
                    PorePressure = st.PorePressure,
                    EffectiveStressXX = st.EffectiveStressXX,
                    EffectiveStressYY = st.EffectiveStressYY,
                    EffectiveStressXY = st.EffectiveStressXY,
                    EffectivePrincipalMax = st.EffectivePrincipalMax,
                    EffectivePrincipalMin = st.EffectivePrincipalMin,
                    YieldRatio = st.YieldRatio,
                    PlasticStrainNorm = st.PlasticStrainNorm,
                }).ToList(),
        }).ToList();

        if (stages.Count == 0)
            stages.Add(new P610T3StageSnapshot { Index = 1, Label = "Restored" });

        P610T3MetaBlock? meta = null;
        if (dto.Meta != null)
        {
            meta = new P610T3MetaBlock(
                dto.Meta.Cx, dto.Meta.Cy, dto.Meta.A, dto.Meta.SigmaV_kPa, dto.Meta.K0,
                dto.Meta.E_kPa, dto.Meta.Nu, dto.Meta.PhiDeg, dto.Meta.C_kPa,
                dto.Meta.PsiDeg, dto.Meta.Gamma, dto.Meta.SrfFoS);
        }

        return new P610T3RunResult
        {
            Nodes = nodes,
            Elements = elements,
            Stages = stages,
            Joints = (dto.Joints ?? new List<XfemFeJoint>()).Select(RebuildJoint).ToList(),
            Description = dto.Description,
            Meta = meta,
        };
    }

    private static XfemFeJoint CaptureJoint(P610T3JointResult j) => new()
    {
        N1 = j.N1,
        N2 = j.N2,
        N3 = j.N3,
        N4 = j.N4,
        Un = j.Un,
        Us = j.Us,
        Closure = j.Closure,
        SnKpa = j.SnKpa,
        TauKpa = j.TauKpa,
        L = j.L,
        Nx = j.Nx,
        Ny = j.Ny,
        Slipped = j.Slipped,
    };

    private static P610T3JointResult RebuildJoint(XfemFeJoint j) => new()
    {
        N1 = j.N1,
        N2 = j.N2,
        N3 = j.N3,
        N4 = j.N4,
        Un = j.Un,
        Us = j.Us,
        Closure = j.Closure,
        SnKpa = j.SnKpa,
        TauKpa = j.TauKpa,
        L = j.L,
        Nx = j.Nx,
        Ny = j.Ny,
        Slipped = j.Slipped,
    };

    private static XfemFeElement CaptureElement(ElementBase e) => new()
    {
        Id = e.Id,
        NodeIds = e.NodeIds?.ToList() ?? new List<int>(),
        IsActive = e.IsActive,
        Kind = e.NodeIds?.Length switch
        {
            8 => "Q8",
            6 => "T6",
            4 => "Q4",
            _ => "T3",
        },
    };

    private static ElementBase? RebuildElement(XfemFeElement e)
    {
        var ids = e.NodeIds?.ToArray() ?? Array.Empty<int>();
        ElementBase el = e.Kind switch
        {
            "Q8" => new QuadraticQuadrilateralElement(),
            "T6" => new QuadraticTriangleElement(),
            "Q4" => new LinearQuadrilateralElement(),
            _ => new LinearTriangleElement(),
        };
        el.Id = e.Id;
        el.NodeIds = ids;
        el.IsActive = e.IsActive;
        return el;
    }

    public class XfemFeRunState
    {
        public string? Description { get; set; }
        public XfemFeRunMeta? Meta { get; set; }
        public List<XfemFeNode>? Nodes { get; set; }
        public List<XfemFeElement>? Elements { get; set; }
        public List<XfemFeJoint>? Joints { get; set; }
        public List<XfemFeStage>? Stages { get; set; }
        public string? LastDatContent { get; set; }
        public string? FemCppArtifactSummary { get; set; }
        public int? SelectedStageListIndex { get; set; }
    }

    public class XfemFeRunMeta
    {
        public double? Cx { get; set; }
        public double? Cy { get; set; }
        public double? A { get; set; }
        public double? SigmaV_kPa { get; set; }
        public double? K0 { get; set; }
        public double? E_kPa { get; set; }
        public double? Nu { get; set; }
        public double? PhiDeg { get; set; }
        public double? C_kPa { get; set; }
        public double? PsiDeg { get; set; }
        public double? Gamma { get; set; }
        public double? SrfFoS { get; set; }
    }

    public class XfemFeNode
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Ux { get; set; }
        public double Uy { get; set; }
    }

    public class XfemFeElement
    {
        public int Id { get; set; }
        public string? Kind { get; set; }
        public List<int>? NodeIds { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class XfemFeStage
    {
        public int Index { get; set; }
        public string? Label { get; set; }
        public string? Subtitle { get; set; }
        public int ElementsSolid { get; set; } = -1;
        public List<XfemFeDisp>? Displacements { get; set; }
        public List<XfemFeJoint>? Joints { get; set; }
        public List<XfemFeElementStress>? ElementStresses { get; set; }
    }

    public class XfemFeJoint
    {
        public int N1 { get; set; }
        public int N2 { get; set; }
        public int N3 { get; set; }
        public int N4 { get; set; }
        public double Un { get; set; }
        public double Us { get; set; }
        public double Closure { get; set; }
        public double SnKpa { get; set; }
        public double TauKpa { get; set; }
        public double L { get; set; }
        public double Nx { get; set; }
        public double Ny { get; set; }
        public bool Slipped { get; set; }
    }

    public class XfemFeDisp
    {
        public double Ux { get; set; }
        public double Uy { get; set; }
    }

    public class XfemFeElementStress
    {
        public int ElementId { get; set; }
        public double StressXX { get; set; }
        public double StressYY { get; set; }
        public double StressXY { get; set; }
        public double PrincipalStressMax { get; set; }
        public double PrincipalStressMin { get; set; }
        public double VonMisesStress { get; set; }
        public double PorePressure { get; set; }
        public double EffectiveStressXX { get; set; }
        public double EffectiveStressYY { get; set; }
        public double EffectiveStressXY { get; set; }
        public double EffectivePrincipalMax { get; set; }
        public double EffectivePrincipalMin { get; set; }
        public double YieldRatio { get; set; }
        public double PlasticStrainNorm { get; set; }
    }
}

using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Services.Wave3;

public static class FemMaterialAccess
{
    public static IReadOnlyList<MaterialStatPropertyId> SensitivityProperties { get; } =
    [
        MaterialStatPropertyId.Cohesion,
        MaterialStatPropertyId.FrictionAngle,
        MaterialStatPropertyId.UnitWeight,
        MaterialStatPropertyId.YoungsModulus,
    ];

    public static double Get(MaterialModel m, MaterialStatPropertyId id) => id switch
    {
        MaterialStatPropertyId.YoungsModulus => m.YoungsModulus,
        MaterialStatPropertyId.PoissonsRatio => m.PoissonsRatio,
        MaterialStatPropertyId.UnitWeight => m.UnitWeight,
        MaterialStatPropertyId.SaturatedUnitWeight => m.SaturatedUnitWeight,
        MaterialStatPropertyId.Cohesion => m.Cohesion,
        MaterialStatPropertyId.FrictionAngle => m.FrictionAngle,
        MaterialStatPropertyId.DilationAngle => m.DilationAngle,
        MaterialStatPropertyId.TensileCutoff => m.TensileCutoff,
        MaterialStatPropertyId.Ucs => m.Ucs,
        MaterialStatPropertyId.HoekBrownMi => m.HoekBrownMi,
        MaterialStatPropertyId.Gsi => m.Gsi,
        MaterialStatPropertyId.Disturbance => m.Disturbance,
        _ => 0,
    };

    public static void Set(MaterialModel m, MaterialStatPropertyId id, double value)
    {
        switch (id)
        {
            case MaterialStatPropertyId.YoungsModulus: m.YoungsModulus = value; break;
            case MaterialStatPropertyId.PoissonsRatio: m.PoissonsRatio = value; break;
            case MaterialStatPropertyId.UnitWeight: m.UnitWeight = value; break;
            case MaterialStatPropertyId.SaturatedUnitWeight: m.SaturatedUnitWeight = value; break;
            case MaterialStatPropertyId.Cohesion: m.Cohesion = value; break;
            case MaterialStatPropertyId.FrictionAngle: m.FrictionAngle = value; break;
            case MaterialStatPropertyId.DilationAngle: m.DilationAngle = value; break;
            case MaterialStatPropertyId.TensileCutoff: m.TensileCutoff = value; break;
            case MaterialStatPropertyId.Ucs: m.Ucs = value; break;
            case MaterialStatPropertyId.HoekBrownMi: m.HoekBrownMi = value; break;
            case MaterialStatPropertyId.Gsi: m.Gsi = value; break;
            case MaterialStatPropertyId.Disturbance: m.Disturbance = value; break;
        }
    }

    public static List<MaterialModel> CloneAll(IEnumerable<MaterialModel> materials)
        => materials.Select(m => m.Clone()).ToList();

    public static void CopyValues(IReadOnlyList<MaterialModel> source, IList<MaterialModel> dest)
    {
        var byId = dest.ToDictionary(m => m.Id);
        foreach (var s in source)
        {
            if (!byId.TryGetValue(s.Id, out var d)) continue;
            foreach (MaterialStatPropertyId id in Enum.GetValues<MaterialStatPropertyId>())
                Set(d, id, Get(s, id));
        }
    }

    public static MaterialPropertyStatistics GetStats(MaterialModel m, MaterialStatPropertyId id)
    {
        m.EnsurePropertyStatistics();
        if (!m.PropertyStatistics.TryGetValue(id, out var stats))
        {
            stats = MaterialPropertyStatistics.CreateDefault(id);
            m.PropertyStatistics[id] = stats;
        }
        return stats;
    }

    public static bool HasActiveRandomVariables(MaterialModel m) =>
        m.EnableProbabilisticVariation
        && m.PropertyStatistics.Values.Any(s => s.IsActiveRv);

    /// <summary>
    /// First-order Mohr–Coulomb strength used to scale a stored SRF when a full
    /// re-run is not available: c + 10 kPa · tan φ.
    /// </summary>
    public static double ReferenceStrength(MaterialModel m)
    {
        var phi = m.FrictionAngle * Math.PI / 180.0;
        return Math.Max(0, m.Cohesion) + 10.0 * Math.Tan(phi);
    }

    public static double TotalReferenceStrength(IEnumerable<MaterialModel> materials)
        => Math.Max(1e-9, materials.Sum(ReferenceStrength));
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeoStrataFEA;

/// <summary>
/// C# wrapper around native GeometryEngineBridge.dll (Open CASCADE Geom2d/gp + CGAL EPICK/CDT).
/// Build: native/GeometryEngineBridge with CMake, vcpkg install cgal opencascade.
/// Copy GeometryEngineBridge.dll next to GeoStrataFEA.exe or under native/.
/// </summary>
internal static class GeometryEngineBridge
{
    private const string DllName = "GeometryEngineBridge.dll";
    private const int JsonCap = 512 * 1024;

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int ge_point_metrics(double x, double y, IntPtr outJson, int outCap);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int ge_polyline_metrics([In] double[] xy, int nPts, IntPtr outJson, int outCap);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int ge_polygon_triangulate([In] double[] xy, int nPts, IntPtr outJson, int outCap);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int ge_circle_triangulate(double cx, double cy, double r, int segments, IntPtr outJson, int outCap);

    public static bool IsAvailable { get; private set; }

    static GeometryEngineBridge()
    {
        if (ResolveDllPath() is { } path)
        {
            try
            {
                NativeLibrary.Load(path);
                IsAvailable = true;
            }
            catch
            {
                IsAvailable = false;
            }
        }
    }

    private static string? ResolveDllPath()
    {
        string? dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(dir))
            return null;
        string[] candidates =
        [
            Path.Combine(dir, DllName),
            Path.Combine(dir, "native", DllName),
        ];
        foreach (var p in candidates)
        {
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    private static string? RunNative(Func<IntPtr, int, int> nativeCall)
    {
        if (!IsAvailable)
            return null;
        IntPtr buf = Marshal.AllocHGlobal(JsonCap);
        try
        {
            int n = nativeCall(buf, JsonCap);
            if (n < 0)
                return null;
            return Marshal.PtrToStringUTF8(buf);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static string ProcessGeometry(JsonElement root)
    {
        int id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;

        if (!IsAvailable)
        {
            return JsonSerializer.Serialize(new
            {
                cmd = "geometryEngineResult",
                id,
                ok = false,
                reason = "GeometryEngineBridge.dll not loaded — build native/GeometryEngineBridge (CGAL + Open CASCADE via vcpkg) and copy the DLL next to the executable."
            });
        }

        string shape = root.GetProperty("shapeType").GetString() ?? "";

        try
        {
            string? native = shape switch
            {
                "point" => RunNative((buf, cap) =>
                    ge_point_metrics(root.GetProperty("x").GetDouble(), root.GetProperty("y").GetDouble(), buf, cap)),
                "line" or "polyline" => RunPolyline(root),
                "surface" or "rect" or "polygon" => RunPolygon(root),
                "circle" => RunCircle(root),
                _ => null
            };

            if (native == null)
            {
                if (shape is not ("point" or "line" or "polyline" or "surface" or "rect" or "polygon" or "circle"))
                    return JsonSerializer.Serialize(new { cmd = "geometryEngineResult", id, ok = false, reason = "unknown shapeType" });
                return JsonSerializer.Serialize(new { cmd = "geometryEngineResult", id, ok = false, reason = "native geometry call failed or invalid geometry" });
            }

            return WrapResult(id, native);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { cmd = "geometryEngineResult", id, ok = false, reason = ex.Message });
        }
    }

    private static string? RunPolyline(JsonElement root)
    {
        var pts = root.GetProperty("points");
        int n = pts.GetArrayLength();
        if (n < 2)
            return null;
        var xy = new double[n * 2];
        int i = 0;
        foreach (var p in pts.EnumerateArray())
        {
            xy[i++] = p[0].GetDouble();
            xy[i++] = p[1].GetDouble();
        }
        return RunNative((buf, cap) => ge_polyline_metrics(xy, n, buf, cap));
    }

    private static string? RunPolygon(JsonElement root)
    {
        var pts = root.GetProperty("points");
        int n = pts.GetArrayLength();
        if (n < 3)
            return null;
        var xy = new double[n * 2];
        int i = 0;
        foreach (var p in pts.EnumerateArray())
        {
            xy[i++] = p[0].GetDouble();
            xy[i++] = p[1].GetDouble();
        }
        return RunNative((buf, cap) => ge_polygon_triangulate(xy, n, buf, cap));
    }

    private static string? RunCircle(JsonElement root)
    {
        double cx = root.GetProperty("cx").GetDouble();
        double cy = root.GetProperty("cy").GetDouble();
        double r = root.GetProperty("r").GetDouble();
        int seg = root.TryGetProperty("segments", out var s) ? s.GetInt32() : 64;
        return RunNative((buf, cap) => ge_circle_triangulate(cx, cy, r, seg, buf, cap));
    }

    private static string WrapResult(int id, string? nativeJson)
    {
        if (string.IsNullOrEmpty(nativeJson))
            return JsonSerializer.Serialize(new { cmd = "geometryEngineResult", id, ok = false, reason = "empty native response" });

        var inner = JsonNode.Parse(nativeJson)!.AsObject();
        var o = new JsonObject
        {
            ["cmd"] = "geometryEngineResult",
            ["id"] = id
        };
        foreach (var kv in inner)
            o[kv.Key] = kv.Value is null ? null : JsonNode.Parse(kv.Value.ToJsonString());
        return o.ToJsonString();
    }
}

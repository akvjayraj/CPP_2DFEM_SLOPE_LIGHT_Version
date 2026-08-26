using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GeotechSlopeApp.Scripting;

/// <summary>Line-delimited JSON-RPC over a named pipe. Python connects; WPF stays STA.</summary>
public sealed class FeProjectRpcHost : IDisposable
{
    private readonly FeProjectApi _api;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public FeProjectRpcHost(FeProjectApi api, string pipeName)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        PipeName = string.IsNullOrWhiteSpace(pipeName)
            ? "xfem-" + Guid.NewGuid().ToString("N")
            : pipeName;
    }

    public string PipeName { get; }

    public void Start()
    {
        _loop = Task.Run(() => ServeAsync(_cts.Token));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        using var server = new NamedPipeServerStream(
            PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server) { AutoFlush = true, NewLine = "\n" };
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            writer.WriteLine(Dispatch(line));
        }
    }

    public string Dispatch(string line)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(line).RootElement.Clone();
        }
        catch (Exception ex)
        {
            return Error(null, "invalid json: " + ex.Message);
        }

        var id = root.TryGetProperty("id", out var idEl) ? idEl.Clone() : default;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        var args = root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : default;
        try
        {
            var result = Invoke(method, args);
            return Ok(id, result);
        }
        catch (Exception ex)
        {
            return Error(id, ex.Message);
        }
    }

    private object? Invoke(string? method, JsonElement args) => method switch
    {
        "clear" => Call(() => _api.Clear()),
        "set_title" => Call(() => _api.SetTitle(Str(args, "name"))),
        "geometry.set_outline" => Call(() => _api.SetOutline(Points(args, "points"))),
        "geometry.get_outline" => _api.GetOutline().Select(p => new[] { p.X, p.Y }).ToList(),
        "geometry.add_layer" => Call(() => _api.AddLayer(Num(args, "elevation"))),
        "materials.add" => _api.AddMaterial(
            Str(args, "name"),
            Num(args, "c_kpa"),
            Num(args, "phi_deg"),
            Num(args, "e_kpa"),
            Num(args, "nu", 0.3),
            Num(args, "gamma", 20)),
        "mesh.generate" => _api.GenerateMesh(OptStr(args, "element") ?? "Q4", OptNum(args, "size")),
        "bcs.apply_slope_defaults" => _api.ApplySlopeBoundaryDefaults(),
        "bcs.clear" => Call(() => _api.ClearBoundaryConditions()),
        "fe.set_ssr" => Call(() => _api.SetSsr(Flag(args, "on"))),
        "fe.ssr_on" => _api.IsSsrOn(),
        "fe.run" => _api.RunFe(),
        "fe.srf" => _api.Srf(),
        "project.save" => Call(() => _api.Save(Str(args, "path"))),
        "project.load" => Call(() => _api.Load(Str(args, "path"))),
        _ => throw new InvalidOperationException("Unknown method: " + method)
    };

    private static object Call(Action action)
    {
        action();
        return true;
    }

    private static string Str(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var el))
            throw new ArgumentException("Missing " + name);
        return el.GetString() ?? "";
    }

    private static string? OptStr(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var el)
            ? el.GetString()
            : null;

    private static bool Flag(JsonElement args, string name, bool fallback = false)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var el))
            return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.GetDouble() != 0,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b,
            _ => fallback
        };
    }

    private static double? OptNum(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number)
            return el.GetDouble();
        if (el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    private static double Num(JsonElement args, string name, double fallback = double.NaN)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var el))
        {
            if (!double.IsNaN(fallback)) return fallback;
            throw new ArgumentException("Missing " + name);
        }
        return el.GetDouble();
    }

    private static List<(double X, double Y)> Points(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Missing " + name);
        var list = new List<(double X, double Y)>();
        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2)
                throw new ArgumentException("Each point must be [x, y].");
            list.Add((row[0].GetDouble(), row[1].GetDouble()));
        }
        return list;
    }

    private static string Ok(JsonElement id, object? result)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            WriteId(w, id);
            w.WriteBoolean("ok", true);
            w.WritePropertyName("result");
            JsonSerializer.Serialize(w, result);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Error(JsonElement? id, string message)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            if (id is { } idEl)
                WriteId(w, idEl);
            w.WriteBoolean("ok", false);
            w.WriteString("error", message);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteId(Utf8JsonWriter w, JsonElement id)
    {
        w.WritePropertyName("id");
        id.WriteTo(w);
    }
}

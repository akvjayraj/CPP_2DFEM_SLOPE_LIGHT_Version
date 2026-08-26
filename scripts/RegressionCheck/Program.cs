using System.Globalization;
using System.Text.Json;

namespace RegressionCheck;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var metrics = Metrics.Load(options.JsonPath);
            var failures = new List<string>();

            CheckExact(failures, "node_count", metrics.NodeCount, options.ExpectedNodeCount);
            CheckExact(failures, "element_count", metrics.ElementCount, options.ExpectedElementCount);
            CheckClose(failures, "srf.fos", metrics.SrfFoS, options.ExpectedSrfFoS, options.AbsTol, options.RelTol);
            CheckClose(failures, "srf.max_disp", metrics.SrfMaxDisp, options.ExpectedSrfMaxDisp, options.AbsTol, options.RelTol);
            CheckClose(failures, "max_node_disp", metrics.MaxNodeDisp, options.ExpectedMaxNodeDisp, options.AbsTol, options.RelTol);
            CheckExact(failures, "srf.max_iters_used", metrics.SrfMaxItersUsed, options.ExpectedSrfMaxItersUsed);

            if (failures.Count > 0)
            {
                Console.Error.WriteLine($"FAILED {options.CaseName}: {options.JsonPath}");
                foreach (var failure in failures)
                    Console.Error.WriteLine("  " + failure);
                Console.Error.WriteLine(metrics.Describe());
                return 1;
            }

            Console.WriteLine($"OK {options.CaseName}: {metrics.Describe()}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED regression check:");
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static void CheckExact(List<string> failures, string name, int? actual, int? expected)
    {
        if (!expected.HasValue)
            return;
        if (actual != expected)
            failures.Add($"{name}: actual {Format(actual)} != expected {expected.Value}");
    }

    private static void CheckClose(List<string> failures, string name, double? actual, double? expected, double absTol, double relTol)
    {
        if (!expected.HasValue)
            return;
        if (!actual.HasValue || !double.IsFinite(actual.Value))
        {
            failures.Add($"{name}: actual {Format(actual)} is not finite; expected {expected.Value:R}");
            return;
        }

        var diff = Math.Abs(actual.Value - expected.Value);
        var allowed = Math.Max(absTol, relTol * Math.Max(1.0, Math.Abs(expected.Value)));
        if (diff > allowed)
            failures.Add($"{name}: actual {actual.Value:R}, expected {expected.Value:R}, diff {diff:R} > tol {allowed:R}");
    }

    private static string Format(int? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "missing";
    private static string Format(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "missing";

    private sealed class Metrics
    {
        public int? NodeCount { get; init; }
        public int? ElementCount { get; init; }
        public double? SrfFoS { get; init; }
        public double? SrfMaxDisp { get; init; }
        public int? SrfMaxItersUsed { get; init; }
        public double? MaxNodeDisp { get; init; }

        public static Metrics Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"JSON result not found: {path}", path);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            var maxNodeDisp = ReadMaxNodeDisplacement(root);
            JsonElement srf = default;
            var hasSrf = root.TryGetProperty("meta", out var meta)
                         && meta.ValueKind == JsonValueKind.Object
                         && meta.TryGetProperty("srf", out srf)
                         && srf.ValueKind == JsonValueKind.Object;

            return new Metrics
            {
                NodeCount = TryInt(root, "node_count"),
                ElementCount = TryInt(root, "element_count"),
                SrfFoS = hasSrf ? TryDouble(srf, "fos") : null,
                SrfMaxDisp = hasSrf ? TryDouble(srf, "max_disp") : null,
                SrfMaxItersUsed = hasSrf ? TryInt(srf, "max_iters_used") : null,
                MaxNodeDisp = maxNodeDisp,
            };
        }

        public string Describe()
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"nodes={Format(NodeCount)}, elements={Format(ElementCount)}, srf.fos={Format(SrfFoS)}, srf.max_disp={Format(SrfMaxDisp)}, max_node_disp={Format(MaxNodeDisp)}, srf.max_iters={Format(SrfMaxItersUsed)}");
        }

        private static double? ReadMaxNodeDisplacement(JsonElement root)
        {
            if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                return null;

            double max = 0.0;
            var sawNode = false;
            foreach (var node in nodes.EnumerateArray())
            {
                var ux = TryDouble(node, "ux") ?? 0.0;
                var uy = TryDouble(node, "uy") ?? 0.0;
                max = Math.Max(max, Math.Sqrt(ux * ux + uy * uy));
                sawNode = true;
            }
            return sawNode ? max : null;
        }

        private static int? TryInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i))
                return i;
            return null;
        }

        private static double? TryDouble(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d))
                return d;
            return null;
        }
    }

    private sealed class Options
    {
        public string CaseName { get; private init; } = "case";
        public string JsonPath { get; private init; } = "";
        public int? ExpectedNodeCount { get; private init; }
        public int? ExpectedElementCount { get; private init; }
        public double? ExpectedSrfFoS { get; private init; }
        public double? ExpectedSrfMaxDisp { get; private init; }
        public int? ExpectedSrfMaxItersUsed { get; private init; }
        public double? ExpectedMaxNodeDisp { get; private init; }
        public double AbsTol { get; private init; } = 1e-8;
        public double RelTol { get; private init; } = 1e-5;

        public static Options Parse(string[] args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Unexpected argument '{arg}'. Use --name value pairs.");
                var key = arg[2..];
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for --{key}.");
                map[key] = args[++i];
            }

            if (!map.TryGetValue("json", out var jsonPath) || string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("Required argument missing: --json <path>");

            return new Options
            {
                CaseName = GetString(map, "case", Path.GetFileNameWithoutExtension(jsonPath)),
                JsonPath = jsonPath,
                ExpectedNodeCount = GetInt(map, "nodes"),
                ExpectedElementCount = GetInt(map, "elements"),
                ExpectedSrfFoS = GetDouble(map, "srf-fos"),
                ExpectedSrfMaxDisp = GetDouble(map, "srf-max-disp"),
                ExpectedSrfMaxItersUsed = GetInt(map, "srf-max-iters"),
                ExpectedMaxNodeDisp = GetDouble(map, "max-node-disp"),
                AbsTol = GetDouble(map, "abs-tol") ?? 1e-8,
                RelTol = GetDouble(map, "rel-tol") ?? 1e-5,
            };
        }

        private static string GetString(Dictionary<string, string> map, string key, string fallback)
            => map.TryGetValue(key, out var value) ? value : fallback;

        private static int? GetInt(Dictionary<string, string> map, string key)
            => map.TryGetValue(key, out var value)
                ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
                : null;

        private static double? GetDouble(Dictionary<string, string> map, string key)
            => map.TryGetValue(key, out var value)
                ? double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
                : null;
    }
}

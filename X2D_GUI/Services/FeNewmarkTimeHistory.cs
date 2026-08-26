using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Services;

public static class FeNewmarkTimeHistory
{
    private const double Gravity = 9.81;

    public sealed class Motion
    {
        public required double TimeStep { get; init; }
        public required IReadOnlyList<double> Accelerations { get; init; }
        public string SourceName { get; init; } = "";
        public double PeakAccelerationG => Accelerations.Count == 0
            ? 0.0
            : Accelerations.Max(Math.Abs) / Gravity;
    }

    public readonly struct IntegrationResult
    {
        public IntegrationResult(double displacementCm, int exceedanceCount, double peakAccelerationG)
        {
            DisplacementCm = displacementCm;
            ExceedanceCount = exceedanceCount;
            PeakAccelerationG = peakAccelerationG;
        }

        public double DisplacementCm { get; }
        public int ExceedanceCount { get; }
        public double PeakAccelerationG { get; }
    }

    public static Motion Load(
        string path,
        FeNewmarkAccelerationUnits units,
        double scale = 1.0,
        double fallbackTimeStep = 0.01)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Acceleration time-history file not found.", path);

        var lines = File.ReadAllLines(path);
        var factor = (units == FeNewmarkAccelerationUnits.G ? Gravity : 1.0) * (scale > 0 ? scale : 1.0);
        var dt = TryReadPeerTimeStep(lines);
        var isPeer = dt > 0;
        if (!isPeer) dt = fallbackTimeStep;

        var times = new List<double>();
        var values = new List<double>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || !StartsLikeNumber(line)) continue;
            var tokens = line.Split(new[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (isPeer)
            {
                foreach (var token in tokens)
                    if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                        values.Add(a * factor);
                continue;
            }
            if (tokens.Length >= 2
                && double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                && double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var acc))
            {
                times.Add(t);
                values.Add(acc * factor);
            }
            else if (double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var only))
            {
                values.Add(only * factor);
            }
        }

        if (values.Count < 2)
            throw new InvalidDataException($"No acceleration samples in {Path.GetFileName(path)}.");
        if (times.Count == values.Count && times.Count >= 2)
        {
            var mean = Enumerable.Range(1, times.Count - 1).Average(i => times[i] - times[i - 1]);
            if (mean > 0) dt = mean;
        }
        if (!(dt > 0))
            throw new InvalidDataException("Could not determine the time step.");

        return new Motion { TimeStep = dt, Accelerations = values, SourceName = Path.GetFileName(path) };
    }

    public static IntegrationResult Integrate(Motion motion, double ky, bool twoWay = false)
    {
        if (motion == null) throw new ArgumentNullException(nameof(motion));
        if (!(ky > 0))
            return new IntegrationResult(double.PositiveInfinity, 0, motion.PeakAccelerationG);

        var threshold = ky * Gravity;
        var dt = motion.TimeStep;
        var velocity = 0.0;
        var displacement = 0.0;
        var episodes = 0;
        var sliding = false;
        for (var i = 1; i < motion.Accelerations.Count; i++)
        {
            var relative = Relative(motion.Accelerations[i], threshold, velocity, twoWay);
            var relativePrev = Relative(motion.Accelerations[i - 1], threshold, velocity, twoWay);
            if (relative == 0.0 && relativePrev == 0.0 && velocity == 0.0)
            {
                sliding = false;
                continue;
            }
            if (!sliding) { sliding = true; episodes++; }
            var newVelocity = velocity + 0.5 * (relative + relativePrev) * dt;
            if (velocity > 0 && newVelocity < 0 || velocity < 0 && newVelocity > 0)
            {
                var fraction = Math.Abs(velocity) / Math.Abs(newVelocity - velocity);
                displacement += 0.5 * velocity * fraction * dt;
                velocity = 0.0;
                sliding = false;
                continue;
            }
            displacement += 0.5 * (velocity + newVelocity) * dt;
            velocity = newVelocity;
        }
        return new IntegrationResult(Math.Abs(displacement) * 100.0, episodes, motion.PeakAccelerationG);
    }

    private static double Relative(double a, double threshold, double velocity, bool twoWay)
    {
        if (a > threshold) return a - threshold;
        if (twoWay && a < -threshold) return a + threshold;
        if (velocity > 0) return a - threshold;
        if (velocity < 0) return a + threshold;
        return 0.0;
    }

    private static double TryReadPeerTimeStep(IReadOnlyList<string> lines)
    {
        foreach (var line in lines.Take(8))
        {
            var index = line.IndexOf("DT", StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var tail = line[(index + 2)..].TrimStart('=', ' ', ':');
            var token = new string(tail.TakeWhile(c => char.IsDigit(c) || c is '.' or 'e' or 'E' or '-' or '+').ToArray());
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var dt) && dt > 0)
                return dt;
        }
        return 0.0;
    }

    private static bool StartsLikeNumber(string line)
    {
        var c = line[0];
        return char.IsDigit(c) || c is '-' or '+' or '.';
    }
}

namespace GameEngine.Testing.Visual;

using System.Text.Json;

public sealed record VisualVerificationResult(
    string CaptureId,
    bool Passed,
    bool BaselineUpdated,
    string? Message);

public static class VisualBaselineVerifier
{
    public static IReadOnlyList<VisualVerificationResult> Process(
        IEnumerable<VisualCapture> captures,
        string baselineRoot,
        string artifactRoot,
        bool updateBaselines,
        PixelComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        var results = new List<VisualVerificationResult>();
        foreach (var capture in captures)
        {
            string fileName = Sanitize(capture.Id) + ".png";
            string baselinePath = Path.Combine(baselineRoot, fileName);
            if (updateBaselines)
            {
                PngFrameCodec.Save(capture.Frame, baselinePath);
                results.Add(new VisualVerificationResult(capture.Id, true, true, baselinePath));
                continue;
            }

            if (!File.Exists(baselinePath))
            {
                string actualPath = Path.Combine(artifactRoot, Sanitize(capture.Id) + ".actual.png");
                PngFrameCodec.Save(capture.Frame, actualPath);
                results.Add(new VisualVerificationResult(
                    capture.Id,
                    false,
                    false,
                    $"Baseline is missing: {baselinePath}. Actual: {actualPath}."));
                continue;
            }

            var expected = PngFrameCodec.Load(baselinePath);
            var comparison = PixelComparer.Compare(
                expected,
                capture.Frame,
                capture.ComparisonOptions ?? options);
            if (comparison.IsMatch)
            {
                results.Add(new VisualVerificationResult(capture.Id, true, false, null));
                continue;
            }

            string prefix = Path.Combine(artifactRoot, Sanitize(capture.Id));
            PngFrameCodec.Save(expected, prefix + ".expected.png");
            PngFrameCodec.Save(capture.Frame, prefix + ".actual.png");
            if (comparison.DifferenceFrame is not null)
                PngFrameCodec.Save(comparison.DifferenceFrame, prefix + ".diff.png");
            var report = new
            {
                capture.Id,
                comparison.IsMatch,
                comparison.TotalPixels,
                comparison.DifferentPixels,
                comparison.DifferentPixelRatio,
                comparison.MaximumChannelDelta,
                comparison.FailureReason
            };
            Directory.CreateDirectory(artifactRoot);
            File.WriteAllText(
                prefix + ".json",
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            results.Add(new VisualVerificationResult(
                capture.Id,
                false,
                false,
                comparison.FailureReason));
        }
        return results;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
    }
}

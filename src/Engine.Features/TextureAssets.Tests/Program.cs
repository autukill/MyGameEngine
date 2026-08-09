namespace TextureAssets.Tests;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using SkiaSharp;
using System.Text;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Texture Assets Feature Smoke Test ===\n");
        VerifyRegistrationAndOwnership();
        VerifyPngAndWebpDecoding();
        VerifyManifestLoading();
        VerifyAtomicReplacement();
        VerifyValidation();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Texture Assets smoke tests passed ==="
            : $"=== {_failures} Texture Assets test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyRegistrationAndOwnership()
    {
        Console.WriteLine("1. Registration, resolution, and GPU ownership");
        var backend = new FakeTextureBackend();
        using var library = new TextureLibrary(backend, new RejectingDecoder());

        var pixels = new byte[2 * 3 * 4];
        var texture = library.RegisterRgba(
            "test.rgba", 2, 3, pixels, TextureSampler.PixelArt);

        Check(library.TryGetMetadata(texture, out var metadata) &&
              metadata.Width == 2 && metadata.Height == 3,
            "Metadata is resolved by logical reference");
        Check(library.TryResolve(texture, out var resolved) && resolved.Handle == 1,
            "GPU handle is hidden behind the resolver");
        Check(backend.Uploads.Count == 1 &&
              backend.Uploads[0].Sampler == TextureSampler.PixelArt,
            "Sampler state reaches the backend");
        var diagnostics = library.CaptureDiagnostics();
        Check(diagnostics.TextureCount == 1 && diagnostics.EstimatedBytes == 24 &&
              diagnostics.Textures[0] == new TextureMemoryDiagnostics("test.rgba", 2, 3, 24),
            "Texture diagnostics estimate RGBA8 bytes without exposing handles");
        Check(!library.TryResolve(new TextureRef("missing"), out _),
            "Unknown texture resolves safely");

        Check(library.Remove(texture) && backend.Deleted.SequenceEqual(new[] { 1u }),
            "Remove deletes the owned handle exactly once");
        Check(!library.Remove(texture), "Removing an absent texture is a no-op");

        library.RegisterRgba("first", 1, 1, new byte[4]);
        library.RegisterRgba("second", 1, 1, new byte[4]);
        library.Dispose();
        Check(backend.Deleted.SequenceEqual(new[] { 1u, 2u, 3u }),
            "Dispose deletes all remaining handles exactly once");
    }

    private static void VerifyPngAndWebpDecoding()
    {
        Console.WriteLine("2. PNG and WebP decoding");
        VerifyEncodedFormat(SKEncodedImageFormat.Png, "PNG");
        VerifyEncodedFormat(SKEncodedImageFormat.Webp, "WebP");
    }

    private static void VerifyEncodedFormat(SKEncodedImageFormat format, string name)
    {
        byte[] encoded = CreateEncodedImage(format);
        var backend = new FakeTextureBackend();
        using var library = new TextureLibrary(backend, new SkiaImageDecoder());
        using var stream = new MemoryStream(encoded);

        var texture = library.Load($"test.{name.ToLowerInvariant()}", stream);

        Check(stream.CanRead, $"{name} load leaves caller stream open");
        Check(library.TryGetMetadata(texture, out var metadata) &&
              metadata.Width == 2 && metadata.Height == 1,
            $"{name} dimensions are decoded");
        Check(backend.Uploads.Count == 1 && backend.Uploads[0].Pixels.Length == 8,
            $"{name} becomes RGBA8 upload data");
    }

    private static byte[] CreateEncodedImage(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Lime);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100)
            ?? throw new InvalidOperationException($"Could not encode {format} fixture.");
        return data.ToArray();
    }

    private static void VerifyValidation()
    {
        Console.WriteLine("4. Assembly-time validation");
        var backend = new FakeTextureBackend();
        using var library = new TextureLibrary(backend, new RejectingDecoder());

        library.RegisterRgba("duplicate", 1, 1, new byte[4]);
        CheckThrows<ArgumentException>(() =>
            library.RegisterRgba("duplicate", 1, 1, new byte[4]),
            "Duplicate names are rejected before upload");
        Check(backend.Uploads.Count == 1, "Rejected duplicate does not leak a handle");
        CheckThrows<ArgumentException>(() =>
            library.RegisterRgba("bad-length", 2, 2, new byte[4]),
            "Invalid RGBA length is rejected");
        CheckThrows<ArgumentOutOfRangeException>(() =>
            library.RegisterRgba("bad-size", 0, 1, Array.Empty<byte>()),
            "Invalid dimensions are rejected");

        using var invalid = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var decoder = new SkiaImageDecoder();
        CheckThrows<InvalidDataException>(() => decoder.Decode(invalid),
            "Unsupported image data is rejected");
    }

    private static void VerifyAtomicReplacement()
    {
        Console.WriteLine("4. Atomic Texture replacement");
        var backend = new FakeTextureBackend();
        using var library = new TextureLibrary(backend, new RejectingDecoder());
        var texture = library.RegisterRgba("live", 1, 1, new byte[4]);

        using (var replacement = library.BeginReplacement(
                   new[] { "live" },
                   new[] { new TextureReplacementSource(
                       "live", 2, 2, new byte[16], TextureSampler.PixelArt) }))
        {
            library.TryResolve(texture, out var before);
            Check(before.Handle == 1, "Staged Texture is invisible before activation");
            replacement.Activate();
            library.TryResolve(texture, out var active);
            Check(active.Handle == 2 && active.Metadata.Width == 2,
                "Activation switches the logical reference without changing TextureRef");
        }

        library.TryResolve(texture, out var rolledBack);
        Check(rolledBack.Handle == 1 && backend.Deleted.SequenceEqual(new[] { 2u }),
            "Uncommitted replacement restores old mapping and releases staged GPU data");

        using (var replacement = library.BeginReplacement(
                   new[] { "live" },
                   new[] { new TextureReplacementSource(
                       "live", 3, 1, new byte[12], TextureSampler.Smooth) }))
        {
            replacement.Activate();
            replacement.Commit();
        }
        library.TryResolve(texture, out var committed);
        Check(committed.Handle == 3 && committed.Metadata.Width == 3 &&
              backend.Deleted.SequenceEqual(new[] { 2u, 1u }),
            "Commit keeps the new mapping and releases the previous GPU handle once");
    }

    private static void VerifyManifestLoading()
    {
        Console.WriteLine("3. Manifest parsing, path boundary, and rollback");
        string contentRoot = Directory.CreateTempSubdirectory("mygame-textures-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(contentRoot, "first.webp"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(contentRoot, "second.png"), new byte[] { 2 });
            const string json = """
                {
                  "textures": [
                    { "name": "first", "path": "first.webp", "sampling": "pixelArt" },
                    { "name": "second", "path": "second.png", "sampling": "smooth" }
                  ]
                }
                """;

            using var manifestStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var manifest = TextureManifestLoader.Parse(manifestStream);
            var backend = new FakeTextureBackend();
            using var library = new TextureLibrary(backend, new ConstantDecoder());
            var loaded = TextureManifestLoader.LoadInto(library, manifest, contentRoot);

            Check(loaded.Count == 2 && library.Count == 2,
                "Manifest loads all logical textures");
            Check(backend.Uploads[0].Sampler == TextureSampler.PixelArt &&
                  backend.Uploads[1].Sampler == TextureSampler.Smooth,
                "Manifest sampling presets are applied");

            var unsafeManifest = new TextureAssetManifest(new[]
            {
                new TextureAssetDefinition("escape", "../escape.webp", TextureSampler.Smooth)
            });
            CheckThrows<InvalidDataException>(() =>
                TextureManifestLoader.LoadInto(library, unsafeManifest, contentRoot),
                "Manifest paths cannot escape the content root");

            var rollbackBackend = new FakeTextureBackend();
            using var rollbackLibrary = new TextureLibrary(
                rollbackBackend, new FailOnSecondDecoder());
            CheckThrows<InvalidDataException>(() =>
                TextureManifestLoader.LoadInto(rollbackLibrary, manifest, contentRoot),
                "Manifest failure is surfaced");
            Check(rollbackLibrary.Count == 0 &&
                  rollbackBackend.Deleted.SequenceEqual(new[] { 1u }),
                "Partial manifest load rolls back owned textures");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (condition) Console.WriteLine($"  [PASS] {name}");
        else { _failures++; Console.WriteLine($"  [FAIL] {name}"); }
    }

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); Check(false, name); }
        catch (T) { Check(true, name); }
    }

    private sealed class FakeTextureBackend : ITextureBackend
    {
        private uint _nextHandle = 1;

        public List<Upload> Uploads { get; } = [];
        public List<uint> Deleted { get; } = [];

        public uint CreateTexture(
            int width,
            int height,
            ReadOnlySpan<byte> rgbaPixels,
            TextureSampler sampler)
        {
            Uploads.Add(new Upload(width, height, rgbaPixels.ToArray(), sampler));
            return _nextHandle++;
        }

        public void DeleteTexture(uint handle) => Deleted.Add(handle);
    }

    private sealed record Upload(
        int Width,
        int Height,
        byte[] Pixels,
        TextureSampler Sampler);

    private sealed class RejectingDecoder : IImageDecoder
    {
        public DecodedImage Decode(Stream stream) =>
            throw new InvalidOperationException("Decoder should not be called by this test.");
    }

    private sealed class ConstantDecoder : IImageDecoder
    {
        public DecodedImage Decode(Stream stream) =>
            new(1, 1, new byte[] { 255, 255, 255, 255 });
    }

    private sealed class FailOnSecondDecoder : IImageDecoder
    {
        private int _calls;

        public DecodedImage Decode(Stream stream)
        {
            if (++_calls == 2)
                throw new InvalidDataException("Synthetic second-file failure.");
            return new DecodedImage(1, 1, new byte[] { 255, 255, 255, 255 });
        }
    }
}

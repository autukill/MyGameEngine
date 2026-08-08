namespace GameEngine.Features.TextureAtlas.Infrastructure;

using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.TextureAtlas.Domain;

/// <summary>Deterministic, rotation-free shelf atlas builder for RGBA8 Sprite frames.</summary>
public sealed class TextureAtlasBuilder
{
    private sealed class Shelf(int y, int height)
    {
        public int Y { get; } = y;
        public int Height { get; } = height;
        public int NextX { get; set; }
    }

    private sealed class PendingPlacement(
        AtlasSourceFrame frame,
        int contentX,
        int contentY)
    {
        public AtlasSourceFrame Frame { get; } = frame;
        public int ContentX { get; } = contentX;
        public int ContentY { get; } = contentY;
    }

    private sealed class PendingPage
    {
        public List<Shelf> Shelves { get; } = [];
        public List<PendingPlacement> Placements { get; } = [];
        public int UsedWidth { get; set; }
        public int UsedHeight { get; set; }
    }

    public TextureAtlasBuildResult Build(
        IEnumerable<AtlasSourceFrame> sourceFrames,
        AtlasBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(sourceFrames);
        ValidateOptions(options);

        var frames = sourceFrames.ToArray();
        ValidateFrames(frames);
        int border = checked(options.Padding + options.Extrude);

        var ordered = frames
            .OrderByDescending(frame => frame.Height)
            .ThenByDescending(frame => frame.Width)
            .ThenBy(frame => frame.Key, StringComparer.Ordinal)
            .ToArray();
        var pages = new List<PendingPage>();
        var passthrough = new List<string>();

        foreach (var frame in ordered)
        {
            int cellWidth = checked(frame.Width + border * 2);
            int cellHeight = checked(frame.Height + border * 2);
            if (cellWidth > options.MaxPageWidth || cellHeight > options.MaxPageHeight)
            {
                passthrough.Add(frame.Key);
                continue;
            }

            bool placed = false;
            foreach (var page in pages)
            {
                if (TryPlace(page, frame, cellWidth, cellHeight, border, options))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                var page = new PendingPage();
                pages.Add(page);
                if (!TryPlace(page, frame, cellWidth, cellHeight, border, options))
                    throw new InvalidOperationException("A validated Atlas frame could not be placed on an empty page.");
            }
        }

        return Materialize(pages, passthrough, options.Extrude);
    }

    private static bool TryPlace(
        PendingPage page,
        AtlasSourceFrame frame,
        int cellWidth,
        int cellHeight,
        int border,
        AtlasBuildOptions options)
    {
        foreach (var shelf in page.Shelves)
        {
            if (cellHeight > shelf.Height || shelf.NextX + cellWidth > options.MaxPageWidth)
                continue;

            AddPlacement(page, shelf, frame, cellWidth, cellHeight, border);
            return true;
        }

        int shelfY = page.Shelves.Count == 0
            ? 0
            : page.Shelves[^1].Y + page.Shelves[^1].Height;
        if (shelfY + cellHeight > options.MaxPageHeight)
            return false;

        var newShelf = new Shelf(shelfY, cellHeight);
        page.Shelves.Add(newShelf);
        AddPlacement(page, newShelf, frame, cellWidth, cellHeight, border);
        return true;
    }

    private static void AddPlacement(
        PendingPage page,
        Shelf shelf,
        AtlasSourceFrame frame,
        int cellWidth,
        int cellHeight,
        int border)
    {
        int cellX = shelf.NextX;
        int contentX = cellX + border;
        int contentY = shelf.Y + border;
        shelf.NextX += cellWidth;

        page.UsedWidth = Math.Max(page.UsedWidth, cellX + cellWidth);
        page.UsedHeight = Math.Max(page.UsedHeight, shelf.Y + cellHeight);
        page.Placements.Add(new PendingPlacement(
            frame,
            contentX,
            contentY));
    }

    private static TextureAtlasBuildResult Materialize(
        IReadOnlyList<PendingPage> pendingPages,
        IReadOnlyList<string> passthrough,
        int extrude)
    {
        var pages = new AtlasPage[pendingPages.Count];
        var placements = new List<AtlasFramePlacement>();

        for (int pageIndex = 0; pageIndex < pendingPages.Count; pageIndex++)
        {
            var pending = pendingPages[pageIndex];
            var pixels = new byte[checked(pending.UsedWidth * pending.UsedHeight * 4)];
            foreach (var item in pending.Placements)
            {
                CopyWithExtrude(
                    item.Frame,
                    pixels,
                    pending.UsedWidth,
                    pending.UsedHeight,
                    item.ContentX,
                    item.ContentY,
                    extrude);
                placements.Add(new AtlasFramePlacement(
                    item.Frame.Key,
                    pageIndex,
                    new PixelRectI(
                        item.ContentX,
                        item.ContentY,
                        item.Frame.Width,
                        item.Frame.Height)));
            }
            pages[pageIndex] = new AtlasPage(pending.UsedWidth, pending.UsedHeight, pixels);
        }

        return new TextureAtlasBuildResult(pages, placements, passthrough);
    }

    private static void CopyWithExtrude(
        AtlasSourceFrame frame,
        byte[] destination,
        int destinationWidth,
        int destinationHeight,
        int contentX,
        int contentY,
        int extrude)
    {
        var source = frame.RgbaPixels.Span;
        for (int targetY = -extrude; targetY < frame.Height + extrude; targetY++)
        {
            int sourceY = Math.Clamp(targetY, 0, frame.Height - 1);
            int pageY = contentY + targetY;
            if ((uint)pageY >= (uint)destinationHeight) continue;

            for (int targetX = -extrude; targetX < frame.Width + extrude; targetX++)
            {
                int sourceX = Math.Clamp(targetX, 0, frame.Width - 1);
                int pageX = contentX + targetX;
                if ((uint)pageX >= (uint)destinationWidth) continue;

                int sourceOffset = (sourceY * frame.Width + sourceX) * 4;
                int targetOffset = (pageY * destinationWidth + pageX) * 4;
                source.Slice(sourceOffset, 4).CopyTo(destination.AsSpan(targetOffset, 4));
            }
        }
    }

    private static void ValidateOptions(AtlasBuildOptions options)
    {
        if (options.MaxPageWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Atlas page width must be positive.");
        if (options.MaxPageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Atlas page height must be positive.");
        if (options.Padding < 0 || options.Extrude < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Atlas padding and extrude must be non-negative.");
        checked
        {
            _ = options.Padding + options.Extrude;
        }
    }

    private static void ValidateFrames(IReadOnlyList<AtlasSourceFrame> frames)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
            if (string.IsNullOrWhiteSpace(frame.Key))
                throw new ArgumentException("Atlas frame keys cannot be empty.", nameof(frames));
            if (!keys.Add(frame.Key))
                throw new ArgumentException($"Atlas frame key '{frame.Key}' is duplicated.", nameof(frames));
            if (frame.Width <= 0 || frame.Height <= 0)
                throw new ArgumentException($"Atlas frame '{frame.Key}' has invalid dimensions.", nameof(frames));
            int expectedLength = checked(frame.Width * frame.Height * 4);
            if (frame.RgbaPixels.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"Atlas frame '{frame.Key}' requires exactly {expectedLength} RGBA8 bytes.",
                    nameof(frames));
            }
        }
    }
}

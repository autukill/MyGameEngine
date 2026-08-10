namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.TextRendering.Domain;

/// <summary>Prepares logical text and submits cached glyph quads to the current SpriteBatch projection.</summary>
public sealed class TextRenderer
{
    private readonly SingleLineTextLayouter _layouter;
    private readonly DynamicGlyphAtlas _atlas;
    private readonly ITextureResolver _textures;

    public TextRenderer(
        SingleLineTextLayouter layouter,
        DynamicGlyphAtlas atlas,
        ITextureResolver textures)
    {
        _layouter = layouter ?? throw new ArgumentNullException(nameof(layouter));
        _atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
    }

    public PreparedTextLayout Prepare(FontFamily fonts, string text, float pixelSize) =>
        _atlas.Prepare(_layouter.Layout(fonts, text, pixelSize));

    public PreparedTextLayout Prepare(in TextDrawCommand command) =>
        _atlas.Prepare(_layouter.Layout(command));

    public void Draw(ISpriteBatch batch, in TextDrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(batch);
        PreparedTextLayout prepared = Prepare(command);
        Draw(batch, prepared, command.Position, command.Color);
    }

    public void Draw(
        ISpriteBatch batch,
        PreparedTextLayout prepared,
        Vector2 position,
        Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(prepared);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) ||
            !float.IsFinite(color.Z) || !float.IsFinite(color.W))
            throw new ArgumentOutOfRangeException(nameof(color));

        foreach (PreparedGlyph glyph in prepared.Glyphs)
        {
            if (!glyph.Atlas.HasPixels) continue;
            if (!_textures.TryResolve(glyph.Atlas.Texture, out ResolvedTexture texture)) continue;
            batch.Draw(
                texture.Handle,
                position + glyph.Placement.Position,
                new Vector2(glyph.Placement.Metrics.Width, glyph.Placement.Metrics.Height),
                color,
                glyph.Atlas.UvBounds);
        }
    }
}

public static class TextDrawingExtensions
{
    public static void DrawText(
        this ISpriteBatch batch,
        TextRenderer renderer,
        FontFamily fonts,
        string text,
        Vector2 position,
        float pixelSize,
        Vector4? color = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var command = new TextDrawCommand(
            fonts,
            text,
            position,
            pixelSize,
            color ?? Vector4.One);
        renderer.Draw(batch, command);
    }

    public static void DrawText(
        this ISpriteBatch batch,
        TextRenderer renderer,
        PreparedTextLayout prepared,
        Vector2 position,
        Vector4? color = null) =>
        renderer.Draw(batch, prepared, position, color ?? Vector4.One);
}

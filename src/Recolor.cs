using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace TeamPlus;

/// <summary>How a sheet is recoloured, and what it was recoloured FROM.</summary>
internal enum Tint
{
    /// <summary>Gold, from the archer's blue team sheet (the yellow team).</summary>
    GoldFromTeamSheet,

    /// <summary>White, from the archer's blue team sheet (a lone wolf).</summary>
    WhiteFromTeamSheet,

    /// <summary>
    /// White, from the archer's own sprite — the fallback for an archer that
    /// ships no team textures at all, where there is no blue to single out.
    /// </summary>
    WhiteFromArcherSheet,
}

/// <summary>
/// Builds the yellow team's gold sheets and the lone wolves' white sheets at
/// runtime, by reading the archer's existing sheet out of the atlas and
/// recolouring the pixels.
///
/// This is done live rather than shipped as pre-baked PNGs for one reason:
/// **custom archers**. A modded archer's textures are registered by its own mod
/// into its own atlas, so no art we could ship in advance would cover it. Doing
/// the recolour from whatever is in the atlas means every archer — vanilla,
/// workshop, or a FortRise archer pack — gets a gold and a white version for
/// free, and none of it goes stale when the game or a mod updates its art.
///
/// WHICH pixels belong to the team is not guessed. TowerFall ships a blue AND a
/// red sheet for every archer, hand-painted, and the pixels where those two
/// differ are — by the game's own authorship — the team's part of the sprite.
/// So the pair is diffed and only those pixels are recoloured; everything the
/// two sheets agree on is the archer's own identity and is left exactly alone.
/// (There is no red-from-blue formula to copy: the correspondence is not even a
/// bijection — one blue shade maps to three different reds in body2, and body3
/// contains a lone #000000 -> #FFFFFF pixel.)
///
/// Without a pair to diff (an archer that ships only one team sheet) it falls
/// back to a hue window, and for a lone wolf with no team art at all, to
/// draining the archer's own sprite.
///
/// Each result is cached by (source texture, source rect, pair, tint), so a
/// sheet is converted once per session no matter how many rounds are played.
/// </summary>
internal static class Recolor
{
    private static readonly Dictionary<(Texture2D, Rectangle, Rectangle, Tint), Subtexture?> cache
        = new();

    public static int Converted { get; private set; }

    /// <summary>
    /// A recoloured copy of <paramref name="source"/>, or null if the pixels
    /// could not be read (in which case the caller leaves the sprite alone).
    /// </summary>
    /// <param name="source">The sheet to recolour — one of the archer's team sheets.</param>
    /// <param name="pair">
    /// The archer's OTHER team sheet, if it has one. Where the two disagree is
    /// the team's part of the sprite, and only those pixels are touched.
    /// </param>
    public static Subtexture? Make(Subtexture? source, Subtexture? pair, Tint tint)
    {
        if (source == null)
        {
            return null;
        }
        Texture2D? texture = source.Texture2D;
        if (texture == null || texture.IsDisposed)
        {
            return null;
        }

        Rectangle rect = source.Rect;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        Rectangle pairRect = (pair != null && pair.Texture2D != null && !pair.Texture2D.IsDisposed
                              && pair.Rect.Width == rect.Width && pair.Rect.Height == rect.Height)
            ? pair.Rect
            : Rectangle.Empty;

        var key = (texture, rect, pairRect, tint);
        if (cache.TryGetValue(key, out Subtexture? cached))
        {
            return cached;
        }

        Subtexture? result = null;
        try
        {
            var pixels = new Color[rect.Width * rect.Height];
            texture.GetData(0, rect, pixels, 0, pixels.Length);

            Color[]? other = null;
            if (pairRect != Rectangle.Empty)
            {
                other = new Color[pixels.Length];
                pair!.Texture2D!.GetData(0, pairRect, other, 0, other.Length);
            }

            int touched = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                // With a pair, the mask decides and the hue gate is not needed.
                // Without one, fall back to judging each pixel by its hue.
                bool teamPixel = other == null || pixels[i] != other[i];
                if (!teamPixel)
                {
                    continue;
                }
                Color before = pixels[i];
                pixels[i] = Apply(pixels[i], tint, gated: other == null);
                if (pixels[i] != before)
                {
                    touched++;
                }
            }
            if (other != null)
            {
                ThreeTeamModule.Log($"recoloured {touched} team pixel(s) of "
                                    + $"{pixels.Length} ({tint}, masked by the archer's "
                                    + "own blue/red pair)");
            }

            var made = new Texture2D(Engine.Instance.GraphicsDevice, rect.Width, rect.Height);
            made.SetData(pixels);
            result = new Subtexture(new Monocle.Texture(made));
            Converted++;
        }
        catch (Exception e)
        {
            ThreeTeamModule.Log($"could not recolour a {rect.Width}x{rect.Height} sheet "
                                + $"({tint}): {e.Message}");
        }

        cache[key] = result;
        return result;
    }

    // The atlases are drawn with BlendState.AlphaBlend, i.e. premultiplied
    // alpha. TowerFall's art is hard-edged so this almost never bites, but a
    // semi-transparent pixel has to be un-premultiplied before its hue means
    // anything, and re-premultiplied afterwards.
    private static Color Apply(Color pixel, Tint tint, bool gated)
    {
        byte a = pixel.A;
        if (a == 0)
        {
            return pixel;
        }

        float r = pixel.R / 255f;
        float g = pixel.G / 255f;
        float b = pixel.B / 255f;
        float alpha = a / 255f;
        if (a < 255)
        {
            r = Math.Min(1f, r / alpha);
            g = Math.Min(1f, g / alpha);
            b = Math.Min(1f, b / alpha);
        }

        RgbToHsl(r, g, b, out float h, out float s, out float l);
        bool changed = tint switch
        {
            Tint.GoldFromTeamSheet => Gold(ref h, ref s, ref l, gated),
            Tint.WhiteFromTeamSheet => WhiteFromBlue(ref h, ref s, ref l, gated),
            _ => WhiteFromAnything(ref h, ref s, ref l),
        };
        if (!changed)
        {
            return pixel;
        }

        HslToRgb(h, s, l, out r, out g, out b);
        if (a < 255)
        {
            r *= alpha;
            g *= alpha;
            b *= alpha;
        }
        return new Color((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), a);
    }

    /// <summary>
    /// Fallback hue band for archers that ship only ONE team sheet, so there is
    /// no pair to diff. Not used when a pair exists.
    ///
    /// Measured off the shipped blue sheets rather than guessed: the team colour
    /// itself sits at ~190 degrees, and every sheet also carries a 280-300
    /// purple band, which is the outfit's shadow shade. The archer's own
    /// identity colours sit outside it — skin and hair at 20-40, personal
    /// accents around 330 — and vanilla's blue art leaves those alone too, so
    /// this window is what "the team's part of the sprite" actually means.
    ///
    /// (The Switch build's generator stopped at 280 and left purple trousers on
    /// some archers. Don't sync it back.)
    /// </summary>
    private const float TeamHueMin = 150f;
    private const float TeamHueMax = 310f;

    /// <summary>
    /// Blue team sheet -> the team gold FCE83C/FCF4A4. Hue is interpolated by
    /// lightness (warm in shadow, bright at the top) and saturation is forced
    /// up, or the shading comes out olive.
    /// </summary>
    private static bool Gold(ref float h, ref float s, ref float l, bool gated)
    {
        float degrees = h * 360f;
        if (gated && (degrees < TeamHueMin || degrees > TeamHueMax || s <= 0.15f))
        {
            return false;
        }
        h = (42f + 16f * Math.Min(l, 0.6f)) / 360f;
        s = Math.Min(1f, s * 1.5f + 0.2f);
        l = Math.Min(0.92f, l * 1.15f + 0.05f);
        return true;
    }

    /// <summary>Blue team sheet -> white/silver, again leaving skin alone.</summary>
    private static bool WhiteFromBlue(ref float h, ref float s, ref float l, bool gated)
    {
        float degrees = h * 360f;
        if (gated && (degrees < TeamHueMin || degrees > TeamHueMax || s <= 0.10f))
        {
            return false;
        }
        Bleach(ref h, ref s, ref l);
        return true;
    }

    /// <summary>
    /// No team sheet to work from, so drain the colour out of everything with
    /// any saturation at all. Broader than the blue-only pass — it also cools
    /// hair and clothing — but it is the one transform that works on an archer
    /// whose palette we know nothing about, which is exactly the custom-archer
    /// case.
    /// </summary>
    private static bool WhiteFromAnything(ref float h, ref float s, ref float l)
    {
        if (s <= 0.08f)
        {
            return false;
        }
        Bleach(ref h, ref s, ref l);
        return true;
    }

    private static void Bleach(ref float h, ref float s, ref float l)
    {
        h = 210f / 360f;                            // a hint of cold blue in the shadows
        s = Math.Max(0f, 0.14f - 0.10f * l);
        l = Math.Min(0.97f, 0.34f + 0.66f * l);
    }

    private static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        l = (max + min) * 0.5f;
        if (max - min < 1e-6f)
        {
            h = 0f;
            s = 0f;
            return;
        }
        float d = max - min;
        s = (l > 0.5f) ? d / (2f - max - min) : d / (max + min);
        if (max == r)
        {
            h = ((g - b) / d + ((g < b) ? 6f : 0f)) / 6f;
        }
        else if (max == g)
        {
            h = ((b - r) / d + 2f) / 6f;
        }
        else
        {
            h = ((r - g) / d + 4f) / 6f;
        }
    }

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        if (s <= 1e-6f)
        {
            r = g = b = l;
            return;
        }
        float q = (l < 0.5f) ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        r = HueToChannel(p, q, h + 1f / 3f);
        g = HueToChannel(p, q, h);
        b = HueToChannel(p, q, h - 1f / 3f);
    }

    private static float HueToChannel(float p, float q, float t)
    {
        if (t < 0f)
        {
            t += 1f;
        }
        if (t > 1f)
        {
            t -= 1f;
        }
        if (t < 1f / 6f)
        {
            return p + (q - p) * 6f * t;
        }
        if (t < 0.5f)
        {
            return q;
        }
        if (t < 2f / 3f)
        {
            return p + (q - p) * (2f / 3f - t) * 6f;
        }
        return p;
    }
}

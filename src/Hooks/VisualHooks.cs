using System;
using System.Xml;
using FortRise;
using HarmonyLib;
using Monocle;
using TowerFall;

namespace TeamsPlus.Hooks;

/// <summary>
/// What a yellow archer and a lone wolf look like.
///
/// A team's look in TowerFall is not a tint: the game swaps in a fully
/// recoloured sprite sheet, named by &lt;BlueTexture&gt;/&lt;RedTexture&gt; in
/// spriteData.xml. (The Switch build could never do this — its AOT-compiled
/// binary never texture-swaps players, which is why that version marks teams
/// with a ring instead.)
///
///   * yellow team -> the archer's team sheet, its team pixels shifted to gold
///   * lone wolf   -> the same, bleached white
///
/// Both are produced at runtime by <see cref="Recolor"/>, so a modded archer is
/// handled exactly like a vanilla one. What counts as a "team pixel" comes from
/// the game's own art: an archer ships a blue AND a red sheet, and the pixels
/// where they differ are the ones the team owns. So both are handed to the
/// recolourer — the blue one to convert, the red one as the mask.
///
/// Order of preference:
///
///   1. BlueTexture + RedTexture — the pair, so the mask is exact
///   2. whichever single team sheet exists, judged by hue instead
///   3. for wolves only: the archer's own sprite, drained of colour
///
/// An archer that ships no team textures cannot go gold (there is nothing to
/// recolour without wrecking its palette) and keeps its own sheet. Such an
/// archer already cannot play blue or red in ANY team mode: vanilla's own Player
/// constructor reads BlueTexture unconditionally and throws without it.
/// </summary>
internal static class VisualHooks
{
    private static readonly AccessTools.FieldRef<Player, Sprite<string>> BodySprite =
        AccessTools.FieldRefAccess<Player, Sprite<string>>("bodySprite");
    private static readonly AccessTools.FieldRef<Player, Sprite<string>> BowSprite =
        AccessTools.FieldRefAccess<Player, Sprite<string>>("bowSprite");
    private static readonly AccessTools.FieldRef<Player, Sprite<string>> HeadSprite =
        AccessTools.FieldRefAccess<Player, Sprite<string>>("headSprite");

    /// <summary>Dev switch: pretend no archer ships team textures, to exercise the fallback.</summary>
    private static readonly bool ForceArcherSource =
        Environment.GetEnvironmentVariable("THREETEAM_FORCE_ARCHER_SOURCE") == "1";

    public static void Load(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredConstructor(typeof(Player), new[]
            {
                typeof(int), typeof(Microsoft.Xna.Framework.Vector2), typeof(Allegiance),
                typeof(Allegiance), typeof(PlayerInventory), typeof(Player.HatStates),
                typeof(bool), typeof(bool), typeof(bool)
            }),
            postfix: new HarmonyMethod(typeof(VisualHooks), nameof(PlayerCtor_Postfix)));

        // hats change mid-round (crown, no-hat), and InitHead re-applies the
        // team texture each time it rebuilds the head sprite
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(Player), "InitHead"),
            postfix: new HarmonyMethod(typeof(VisualHooks), nameof(InitHead_Postfix)));

        if (ForceArcherSource)
        {
            ThreeTeamModule.Log("DEV: ignoring every archer's team textures, "
                                + "recolouring from their own sprites instead");
        }
    }

    private static void PlayerCtor_Postfix(Player __instance)
    {
        if (!Recoloured(__instance.TeamColor))
        {
            return;
        }
        int applied = 0;
        applied += Swap(BodySprite(__instance), __instance.ArcherData.Sprites.Body,
                        __instance.TeamColor) ? 1 : 0;
        applied += Swap(BowSprite(__instance), __instance.ArcherData.Sprites.Bow,
                        __instance.TeamColor) ? 1 : 0;
        applied += SwapHead(__instance) ? 1 : 0;

        string set = Teams3.IsWolfColor(__instance.TeamColor)
            ? "white (lone wolf)" : "gold (yellow team)";
        ThreeTeamModule.Log($"{set}: P{__instance.PlayerIndex + 1} recoloured {applied}/3 "
                            + "(body, bow, head)");
    }

    private static void InitHead_Postfix(Player __instance)
    {
        if (Recoloured(__instance.TeamColor))
        {
            SwapHead(__instance);
        }
    }

    /// <summary>Team colours we recolour for: the yellow team and lone wolves.</summary>
    private static bool Recoloured(Allegiance teamColor) =>
        teamColor == Teams3.Yellow || Teams3.IsWolfColor(teamColor);

    private static bool SwapHead(Player player)
    {
        if (player.HatState != Player.HatStates.Normal)
        {
            return false;   // vanilla only recolours the normal head, so neither do we
        }
        return Swap(HeadSprite(player), player.ArcherData.Sprites[player.HatState],
                    player.TeamColor);
    }

    private static bool Swap(Sprite<string>? sprite, string? spriteId, Allegiance teamColor)
    {
        if (sprite == null || string.IsNullOrEmpty(spriteId))
        {
            return false;
        }

        bool wolf = Teams3.IsWolfColor(teamColor);
        (Subtexture? source, Subtexture? pair) = TeamSheets(spriteId);
        Tint tint;
        if (source != null)
        {
            tint = wolf ? Tint.WhiteFromTeamSheet : Tint.GoldFromTeamSheet;
        }
        else if (wolf)
        {
            // no team art: bleach the archer's own sheet, whatever it looks like
            source = new Subtexture(sprite.Texture, sprite.ClipRect);
            pair = null;
            tint = Tint.WhiteFromArcherSheet;
        }
        else
        {
            return false;   // yellow team on an archer with no team art: leave it be
        }

        Subtexture? recoloured = Recolor.Make(source, pair, tint);
        if (recoloured == null)
        {
            return false;
        }
        sprite.SwapSubtexture(recoloured);
        return true;
    }

    /// <summary>
    /// The archer's team sheets: the one to recolour, and its opposite number to
    /// diff against. Either may be absent — a modded archer might ship only one,
    /// or none.
    /// </summary>
    private static (Subtexture? source, Subtexture? pair) TeamSheets(string spriteId)
    {
        if (ForceArcherSource)
        {
            return (null, null);
        }
        XmlElement xml = TFGame.SpriteData.GetXML(spriteId);
        if (xml == null)
        {
            return (null, null);
        }
        Subtexture? blue = Sheet(xml, "BlueTexture");
        Subtexture? red = Sheet(xml, "RedTexture");
        // prefer blue as the source (that is what vanilla's own team art starts
        // from); with only red, use it and let blue be the missing mask
        return blue != null ? (blue, red) : (red, blue);
    }

    private static Subtexture? Sheet(XmlElement xml, string child)
    {
        if (!xml.HasChild(child))
        {
            return null;
        }
        string name = xml.ChildText(child);
        // a modded archer's sheets are registered into TFGame.Atlas by FortRise,
        // same as the vanilla ones, so one lookup covers both
        return (!string.IsNullOrEmpty(name) && TFGame.Atlas.Contains(name))
            ? TFGame.Atlas[name]
            : null;
    }
}

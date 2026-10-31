using System;
using FortRise;

namespace ThreeTeamMode;

/// <summary>
/// Every image the mod ships, registered through FortRise's texture registry so
/// nothing in the game's own Content folder is touched.
///
/// Just two images: the select-screen banner and the versus-carousel icon. The
/// gold and white player sheets are NOT shipped — they are recoloured from
/// whatever is in the atlas at spawn time (see Recolor), which is what lets
/// custom archers work.
/// </summary>
internal static class Assets3
{
    public static ISubtextureEntry ModeIcon { get; private set; } = null!;
    public static ISubtextureEntry Banner { get; private set; } = null!;

    public static void Register(IModContent content, IModRegistry registry, Action<string> warn)
    {
        ModeIcon = registry.Subtextures.RegisterTexture(
            "gameModes/threeteam",
            content.Root.GetRelativePath("Content/gameModes/threeteam.png"));

        Banner = registry.Subtextures.RegisterTexture(
            "menu/teamC2x",
            content.Root.GetRelativePath("Content/menu/teamC2x.png"),
            SubtextureAtlasDestination.MenuAtlas);
    }
}

using FortRise;
using HarmonyLib;
using Microsoft.Xna.Framework;
using ThreeTeamMode.UI;
using TowerFall;

namespace ThreeTeamMode.Hooks;

/// <summary>
/// Builds our own team-select screen when the mode is ours, and leaves vanilla's
/// completely alone otherwise. FortRise already routes us here: because the mode
/// reports IsTeamMode, VersusBeginButton sends the player to MenuState.TeamSelect
/// exactly as it does for Team Deathmatch.
/// </summary>
internal static class MenuHooks
{
    public static void Load(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(MainMenu), nameof(MainMenu.CreateTeamSelect)),
            prefix: new HarmonyMethod(typeof(MenuHooks), nameof(CreateTeamSelect_Prefix)));
    }

    private static bool CreateTeamSelect_Prefix(MainMenu __instance)
    {
        if (!Teams3.MenuActive)
        {
            return true;
        }

        var offLeft = new Vector2(-40f, 50f);
        var offRight = new Vector2(360f, 50f);

        // Wide (8-player) geometry. WiderSet's own transpiler lifts the vanilla
        // banners to y=60, but it only has two of them out at the screen edges —
        // our third sits near the middle, where 60 would put it through the
        // "TEAMS" title. So the banners stay at 80 and the eight rows tighten up
        // instead, ending clear of the FIGHT prompt at 225.
        bool wide = Wide.IsWide;
        float bannerY = 80f;
        int firstRow = wide ? 122 : 145;
        int rowStep = wide ? 13 : 20;
        float[] x = new float[4];
        for (int c = 0; c < 4; c++)
        {
            x[c] = ThreeTeamSelector.ColumnX(c);
        }

        // Column headers. The neutral column gets a word instead of a banner —
        // it is the lone-wolf column, not a team.
        __instance.Add<MenuItem>(
            new ColumnBanner(new Vector2(x[0], bannerY), offLeft, null, "SOLO", Color.White),
            new ColumnBanner(new Vector2(x[1], bannerY), offLeft, TFGame.MenuAtlas["teamA2x"]),
            new ColumnBanner(new Vector2(x[2], bannerY), offRight, Assets3.Banner?.Subtexture,
                             Assets3.Banner?.Subtexture == null ? "YELLOW" : null,
                             Teams3.YellowColorA),
            new ColumnBanner(new Vector2(x[3], bannerY), offRight, TFGame.MenuAtlas["teamB2x"]));

        __instance.Add(new ReadyBanner());

        int row = 0;
        for (int i = 0; i < Teams3.PlayerCap; i++)
        {
            if (!Teams3.Joined(i))
            {
                continue;
            }
            var position = new Vector2(x[0], firstRow + rowStep * row);
            var tweenFrom = new Vector2((i % 2 == 0) ? -40f : 360f, position.Y - 10f);
            __instance.Add(new ThreeTeamSelector(position, tweenFrom, i));
            row++;
        }

        __instance.ToStartSelected = null;
        __instance.BackState = MainMenu.MenuState.VersusOptions;
        __instance.TweenBGCameraToY(3);
        return false;
    }
}

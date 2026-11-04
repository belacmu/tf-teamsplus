using FortRise;

namespace ThreeTeamMode;

/// <summary>
/// Optional 8-player support.
///
/// WiderSet (the maintained FortRise 8-player mod) widens TFGame.Players to 8
/// and grows the play-field to 420px when its wide level set is picked. Nothing
/// here is required: with WiderSet absent every call degrades to the vanilla
/// 4-player numbers.
///
/// Where we deliberately overlap with WiderSet:
///   * MatchTeams' indexer — they add slots 4..7 with Harmony finalizers, so we
///     always go through the indexer and never touch a slot for a player who
///     has not joined.
///   * MainMenu.CreateTeamSelect — they transpile the vanilla body, we replace
///     it, so OUR screen has to do the wide layout itself (see MenuHooks).
///   * RoundLogic.SpawnPlayersTeams — they transpile it, we never call it (our
///     RoundLogic spawns its own groups), so the two do not interact.
/// </summary>
internal static class Wide
{
    private const string ModName = "Teuria.WiderSet";

    private static Teuria.WiderSet.IWiderSetModApi? api;
    private static IModuleContext? context;
    private static bool asked;

    public static void Load(IModuleContext moduleContext)
    {
        context = moduleContext;
        ThreeTeamModule.Log(Api() == null
            ? "WiderSet not loaded yet — looked up again when first needed (8 players if it is installed)"
            : "WiderSet detected — 8-player support enabled");
    }

    /// <summary>
    /// Resolve WiderSet's API, retrying later if it was not loaded yet. WiderSet is
    /// deliberately NOT an optional dependency in meta.json: that would make FortRise
    /// load Team+ after WiderSet, and so after TF.EX, whose GAME MODE patch stops the
    /// scroll at its Netplay mode (TEAM+ out of reach). Team+ loads first (priority
    /// Lowest) and finds WiderSet here on first use.
    /// </summary>
    private static Teuria.WiderSet.IWiderSetModApi? Api()
    {
        if (api != null || context == null)
        {
            return api;
        }
        try
        {
            api = context.Interop.GetApi<Teuria.WiderSet.IWiderSetModApi>(ModName);
        }
        catch
        {
            api = null;
        }
        if (api == null && asked)
        {
            return null;      // absent; stop paying for the lookup every frame
        }
        asked = true;
        return api;
    }

    public static bool Installed => Api() != null;

    /// <summary>True while the wide (8-player) set is active.</summary>
    public static bool IsWide
    {
        get
        {
            try
            {
                Teuria.WiderSet.IWiderSetModApi? widerSet = Api();
                return widerSet != null && widerSet.IsWide;
            }
            catch
            {
                return false;   // never let an interop hiccup break a match
            }
        }
    }

    /// <summary>Play-field width, for the spawner's wrap-around distances.</summary>
    public static float LevelWidth => IsWide ? 420f : 320f;

    /// <summary>Dev only: force the wide set on for the scripted self-test.</summary>
    public static bool TrySetWide(bool value)
    {
        Teuria.WiderSet.IWiderSetModApi? widerSet = Api();
        if (widerSet == null)
        {
            return false;
        }
        try
        {
            widerSet.IsWide = value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

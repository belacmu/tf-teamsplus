using FortRise;
using Microsoft.Extensions.Logging;
using ThreeTeamMode.Hooks;

namespace ThreeTeamMode;

/// <summary>
/// Mod entry point. Registers the versus mode and installs the patches.
///
/// This is the PC (FortRise 5) sibling of the Switch mod in
/// ../exlaunch-overlay: same design, same yellow, same lone wolves — but the
/// framework owns the parts that were hand-built there (the mode itself, the
/// team-mode flag, texture loading), so what is left is the rules and the art.
/// </summary>
internal sealed class ThreeTeamModule : Mod
{
    public static ThreeTeamModule Instance { get; private set; } = null!;

    /// <summary>Our registry entry — carries the Modes value FortRise minted for us.</summary>
    public static IVersusGameModeEntry ModeEntry { get; private set; } = null!;

    public ThreeTeamModule(IModContent content, IModuleContext context, ILogger logger)
        : base(content, context, logger)
    {
        Instance = this;

        Wide.Load(context);
        Assets3.Register(content, context.Registry, message => logger.LogWarning("{message}", message));

        DataHooks.Load(context.Harmony);
        RulesHooks.Load(context.Harmony);
        VisualHooks.Load(context.Harmony);
        ResultsHooks.Load(context.Harmony);
        MenuHooks.Load(context.Harmony);
        LevelHooks.Load(context.Harmony);
        SafetyHooks.Load(context.Harmony);
        ReviveHooks.Load(context.Harmony);

        ModeEntry = context.Registry.GameModes.RegisterVersusGameMode(new ThreeTeamGameMode());

        // dev-only scripted playtest: THREETEAM_SELFTEST=1 ./FortRise
        SelfTest.Enabled = System.Environment.GetEnvironmentVariable("THREETEAM_SELFTEST") == "1";
        SelfTest.Load(context.Harmony);
        if (SelfTest.Enabled)
        {
            logger.LogWarning("SELFTEST enabled — the mod will drive the menus itself");
        }

        logger.LogInformation("Three Team Mode ready (sheets recoloured on demand, 8-player: {wide})",
                              Wide.Installed ? "WiderSet" : "no");
    }

    public static void Log(string message)
    {
        Instance?.Logger.LogInformation("{message}", message);
    }
}

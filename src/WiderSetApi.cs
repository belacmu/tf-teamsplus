namespace Teuria.WiderSet;

/// <summary>
/// A local copy of the slice of WiderSet's exported API we consume. FortRise
/// proxies mod APIs member by member (Pintail), so declaring a subset is both
/// allowed and the recommended pattern — see BaronMode in FortRise's example
/// mods, which does exactly this.
/// </summary>
public interface IWiderSetModApi
{
    /// <summary>True while the game is in the 8-player "wide" set.</summary>
    bool IsWide { get; set; }

    /// <summary>Set while the pads are transitioning in or out of wide.</summary>
    bool IsHoveringWide { get; set; }

    /// <summary>0 in the standard set, 50 in wide.</summary>
    float UIXOffset { get; }
}


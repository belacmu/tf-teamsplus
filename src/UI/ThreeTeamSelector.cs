using System;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TeamsPlus.UI;

/// <summary>
/// The four-column team selector: SOLO | BLUE | YELLOW | RED.
///
/// Vanilla's TeamSelector is a two-way switch — left is Blue, right is Red, and
/// the middle is the "hasn't picked yet" limbo that flashes at you. Here the
/// neutral column is the leftmost, deliberate choice (a lone wolf), so the
/// flashing and the nagging rumble are gone and the cursor walks four columns.
///
/// Column order and the neutral-first layout are the design locked in for the
/// Switch build (three-team-mod/DESIGN.md); the X positions are re-spaced for
/// the 320-wide PC play-field.
/// </summary>
public class ThreeTeamSelector : MenuItem
{
    /// <summary>SOLO, BLUE, YELLOW, RED — evenly spaced, centred on the screen.</summary>
    private static readonly float[] StandardX = { 40f, 120f, 200f, 280f };

    /// <summary>
    /// The wide (8-player) set draws menus over roughly x -50..370, so the four
    /// columns spread out to use it.
    /// </summary>
    private static readonly float[] WideX = { 25f, 120f, 215f, 310f };

    public static float ColumnX(int column) =>
        (Wide.IsWide ? WideX : StandardX)[column];

    private static readonly Allegiance[] ColumnTeam =
    {
        Allegiance.Neutral, Allegiance.Blue, Teams3.Yellow, Allegiance.Red
    };

    private readonly int playerIndex;
    private readonly string text;
    private readonly SineWave sine;
    private readonly Color color;
    private Vector2 tweenFrom;
    private readonly Vector2 tweenTo;
    private Vector2 targetPosition;

    public ThreeTeamSelector(Vector2 position, Vector2 tweenFrom, int playerIndex)
        : base(position)
    {
        tweenTo = position;
        this.tweenFrom = tweenFrom;
        this.playerIndex = playerIndex;
        text = "P" + (playerIndex + 1);
        Add(sine = new SineWave(60));
        color = ArcherData.Archers[TFGame.Characters[playerIndex]].ColorA;
    }

    private static MatchSettings Settings => TowerFall.MainMenu.VersusMatchSettings;

    private int Column
    {
        get
        {
            Allegiance team = Settings.Teams![playerIndex];
            for (int i = 0; i < ColumnTeam.Length; i++)
            {
                if (ColumnTeam[i] == team)
                {
                    return i;
                }
            }
            return 0;
        }
        set
        {
            Settings.Teams![playerIndex] = ColumnTeam[value];
        }
    }

    public override void Update()
    {
        base.Update();

        // A joined player normally has a device, but not always (a controller
        // yanked mid-menu, or a scripted line-up), and vanilla's TeamSelector
        // would null-deref here.
        PlayerInput? input = TFGame.PlayerInputs[playerIndex];
        int column = Column;
        if (input != null && column > 0 && input.MenuLeft)
        {
            Column = column - 1;
            tweenFrom.X = -80f;
            Sounds.ui_move2.Play();
        }
        else if (input != null && column < ColumnTeam.Length - 1 && input.MenuRight)
        {
            Column = column + 1;
            tweenFrom.X = 400f;
            Sounds.ui_move2.Play();
        }

        targetPosition.X = ColumnX(Column);
        base.X = Calc.Approach(base.X, targetPosition.X, 25f * Engine.TimeMult);

        if (input == null || !input.MenuConfirmOrStart || !Settings.CanStartWithTeams)
        {
            return;
        }
        Sounds.ui_startGameType.Play();
        // `MainMenu` inside a MenuItem is the *property* (this menu), so the
        // static entry points need their namespace spelled out.
        base.MainMenu.State = TowerFall.MainMenu.MenuState.Fade;
        base.MainMenu.FadeAction = TowerFall.MainMenu.GotoVersusLevelSelect;
        foreach (ThreeTeamSelector selector in base.MainMenu.Layers[-1].GetList<ThreeTeamSelector>())
        {
            selector.Active = false;
        }
    }

    public override void Render()
    {
        base.Render();
        Draw.OutlineTextCentered(TFGame.Font, text, Position, color, 2f);
        int column = Column;
        if (column > 0)
        {
            Draw.OutlineTextCentered(TFGame.Font, "<",
                Position - Vector2.UnitX * (16f + 2f * sine.Value), color, 2f);
        }
        if (column < ColumnTeam.Length - 1)
        {
            Draw.OutlineTextCentered(TFGame.Font, ">",
                Position + Vector2.UnitX * (16f + 2f * sine.Value), color, 2f);
        }
    }

    public override void TweenIn()
    {
        Position = tweenFrom;
        targetPosition = tweenTo;
        targetPosition.X = ColumnX(Column);
    }

    public override void TweenOut()
    {
        targetPosition = tweenFrom;
    }

    protected override void OnSelect() => throw new NotImplementedException();

    protected override void OnDeselect() => throw new NotImplementedException();

    protected override void OnConfirm() => throw new NotImplementedException();
}

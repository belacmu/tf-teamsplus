using System;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TeamsPlus.UI;

/// <summary>
/// A select-screen column header that tweens in like the vanilla TeamBanner but
/// draws a subtexture we own (the gold banner) or a word (the neutral column's
/// "SOLO"). Vanilla's TeamBanner looks its art up by name in the menu atlas;
/// taking the Subtexture directly keeps our art independent of atlas keys.
/// </summary>
public class ColumnBanner : MenuItem
{
    private readonly Subtexture? subtexture;
    private readonly string? text;
    private readonly Color color;
    private Vector2 tweenFrom;
    private readonly Vector2 tweenTo;

    public ColumnBanner(Vector2 position, Vector2 tweenFrom, Subtexture? subtexture,
                        string? text = null, Color? color = null)
        : base(position)
    {
        this.tweenFrom = tweenFrom;
        tweenTo = position;
        this.subtexture = subtexture;
        this.text = text;
        this.color = color ?? Color.White;
    }

    public override void Render()
    {
        if (subtexture != null)
        {
            Draw.TextureCentered(subtexture, Position, Color.White);
        }
        if (text != null)
        {
            Draw.OutlineTextCentered(TFGame.Font, text, Position, color, 2f);
        }
    }

    public override void TweenIn()
    {
        Position = tweenFrom;
        Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeOut, 20, start: true);
        tween.OnUpdate = delegate(Tween t)
        {
            Position = Vector2.Lerp(tweenFrom, tweenTo, t.Eased);
        };
        Add(tween);
    }

    public override void TweenOut()
    {
        Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeIn, 12, start: true);
        tween.OnUpdate = delegate(Tween t)
        {
            Position = Vector2.Lerp(tweenTo, new Vector2(tweenTo.X, tweenTo.Y - 120f), t.Eased);
        };
        Add(tween);
    }

    protected override void OnSelect() => throw new NotImplementedException();

    protected override void OnDeselect() => throw new NotImplementedException();

    protected override void OnConfirm() => throw new NotImplementedException();
}

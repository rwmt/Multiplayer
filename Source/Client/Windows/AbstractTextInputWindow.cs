using Multiplayer.Client.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public abstract class AbstractTextInputWindow : Window
{
    public override Vector2 InitialSize => new(350f, 175f);

    protected string curText;
    protected string title;
    protected string acceptBtnLabel;
    protected string closeBtnLabel;
    protected bool passwordField;

    private bool opened;

    public AbstractTextInputWindow()
    {
        closeOnClickedOutside = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnAccept = true;
        acceptBtnLabel = "OK".Translate();
    }

    public override void DoWindowContents(Rect inRect)
    {
        Widgets.Label(new Rect(0, 15f, inRect.width, 35f), title);

        if (passwordField)
        {
            // Doesn't call Validate currently
            MpUI.DoPasswordField(new Rect(0, 25 + 15f, inRect.width, 35f), "RenameField", ref curText);
        }
        else
        {
            string text = Widgets.TextField(new Rect(0, 25 + 15f, inRect.width, 35f), curText);
            if ((curText != text || !opened) && Validate(text))
                curText = text;
        }

        DrawExtra(inRect);

        if (!opened)
        {
            UI.FocusControl("RenameField", this);
            opened = true;
        }

        var btnsRect = new Rect(0f, inRect.height - 35f - 5f, closeBtnLabel != null ? 210 : 120, 35f).CenteredOnXIn(inRect);

        if (Widgets.ButtonText(btnsRect.LeftPartPixels(closeBtnLabel != null ? 100 : 120), acceptBtnLabel, true, false))
            Accept();

        if (closeBtnLabel != null)
            if (Widgets.ButtonText(btnsRect.RightPartPixels(100), closeBtnLabel, true, false))
                OnCloseButton();
    }

    public virtual void OnCloseButton()
    {
        Close();
    }

    public override void OnAcceptKeyPressed()
    {
        if (Accept())
            base.OnAcceptKeyPressed();
    }

    public abstract bool Accept();

    public virtual bool Validate(string str) => true;

    public virtual void DrawExtra(Rect inRect) { }
}

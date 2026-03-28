using Multiplayer.Common;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public class BootstrapConfiguratorWindow : Window
{
    private readonly ConnectionBase connection;

    public override Vector2 InitialSize => new(480f, 220f);

    public BootstrapConfiguratorWindow(ConnectionBase connection)
    {
        this.connection = connection;
        doCloseX = true;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = true;
    }

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(inRect.TopPartPixels(35f), "Server Bootstrap");

        Text.Font = GameFont.Small;
        var bodyRect = new Rect(inRect.x, inRect.y + 45f, inRect.width, inRect.height - 90f);
        var message = Multiplayer.session.serverBootstrapSettingsMissing
            ? "Bootstrap mode detected. The dedicated configurator flow will be added in the next push."
            : "Bootstrap mode detected. This slice only wires protocol detection and state routing.";
        Widgets.Label(bodyRect, message);

        if (Widgets.ButtonText(new Rect((inRect.width - 160f) / 2f, inRect.height - 40f, 160f, 35f), "Close"))
            Close();
    }
}
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public partial class BootstrapConfiguratorWindow : Window
{
    private readonly ConnectionBase connection;

    private ServerSettings settings;

    private enum Step
    {
        Settings,
        GenerateMap
    }

    private enum Tab
    {
        Connecting,
        Gameplay,
        Preview
    }

    private Step step;
    private Tab tab;
    private readonly ServerSettingsUI.BufferSet settingsUiBuffers = new();

    private string tomlPreview;
    private Vector2 tomlScroll;

    private bool isUploadingToml;
    private float uploadProgress;
    private string statusText;
    private bool settingsUploaded;

    private const float LabelWidth = 110f;
    private const int MaxGameNameLength = 70;

    public override Vector2 InitialSize => new(550f, 620f);

    public BootstrapConfiguratorWindow(ConnectionBase connection)
    {
        this.connection = connection;
        doCloseX = true;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = true;

        settings = MpUtil.ShallowCopy(Multiplayer.settings.PreferredLocalServerSettings, new ServerSettings());
        settings.gameName ??= $"{Multiplayer.username}'s Server";
        if (settings.gameName.NullOrEmpty())
            settings.gameName = $"{Multiplayer.username}'s Server";
        settings.lanAddress = Endpoints.GetLocalIpAddress() ?? settings.lanAddress ?? "127.0.0.1";
        settings.steam = false;
        settings.arbiter = false;

        settingsUiBuffers.MaxPlayersBuffer = settings.maxPlayers.ToString();
        settingsUiBuffers.AutosaveBuffer = settings.autosaveInterval.ToString();

        step = Multiplayer.session?.serverBootstrapSettingsMissing == true ? Step.Settings : Step.GenerateMap;
        statusText = step == Step.Settings
            ? "Server settings.toml is missing. Configure it and upload it first."
            : "Server settings.toml is already configured. Map generation will be enabled in the next slice.";

        RebuildTomlPreview();
    }

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Text.Anchor = TextAnchor.UpperCenter;
        Widgets.Label(inRect.Down(0f), "Server Bootstrap Configuration");
        Text.Anchor = TextAnchor.UpperLeft;

        Text.Font = GameFont.Small;
        var entry = new Rect(0f, 45f, inRect.width, 30f);
        entry.xMin += 4f;

        settings.gameName = MpUI.TextEntryLabeled(entry, $"{"MpGameName".Translate()}:  ", settings.gameName, LabelWidth);
        if (settings.gameName.Length > MaxGameNameLength)
            settings.gameName = settings.gameName.Substring(0, MaxGameNameLength);

        entry = entry.Down(40f);

        if (step == Step.Settings)
            DrawSettings(entry, inRect);
        else
            DrawGenerateMap(entry, inRect);
    }

    private void DrawGenerateMap(Rect entry, Rect inRect)
    {
        var statusHeight = Text.CalcHeight(statusText ?? string.Empty, entry.width);
        Widgets.Label(entry.Height(statusHeight), statusText ?? string.Empty);

        var noticeRect = new Rect(0f, inRect.height - 55f, inRect.width, 40f);
        Widgets.Label(noticeRect, "The world generation and save upload flow will be added in the next slice.");
    }
}
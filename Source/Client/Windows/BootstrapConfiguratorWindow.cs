using System.IO;
using Multiplayer.Client.Networking;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public partial class BootstrapConfiguratorWindow : Window, IConnectionStatusListener
{
    public static BootstrapConfiguratorWindow Instance { get; private set; }
    public static bool AwaitingBootstrapMapInit { get; set; }

    private readonly ConnectionBase connection;
    private readonly IConnector reconnectConnector;
    private BootstrapServerState bootstrapState = BootstrapServerState.None;
    private float height = 620f;

    private ServerSettings settings;
    private bool retainInstanceOnClose;

    private enum Step
    {
        Settings,
        GenerateMap
    }

    private enum Tab
    {
        Connecting,
        Gameplay
    }

    private Step step;
    private Tab tab;
    private readonly ServerSettingsUI.BufferSet settingsUiBuffers = new();

    private bool isUploadingToml;
    private float uploadProgress;
    private string statusText;
    private bool settingsUploaded;
    private bool saveUploadRequestedByServer;
    private bool saveGenerationStarted;

    private static PendingUploadState pendingUploadState;

    public override Vector2 InitialSize => new(550f, height);

    public BootstrapConfiguratorWindow(ConnectionBase connection, BootstrapServerState bootstrapState)
    {
        this.connection = connection;
        reconnectConnector = Multiplayer.session?.connector;
        doCloseX = true;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = true;

        Instance = this;

        settings = MpUtil.ShallowCopy(Multiplayer.settings.PreferredLocalServerSettings, new ServerSettings());
        if (settings.gameName.NullOrEmpty())
            settings.gameName = $"{Multiplayer.username}'s Server";

        settings.lan = false;
        settings.lanAddress = Endpoints.GetLocalIpAddress() ?? settings.lanAddress ?? "127.0.0.1";

        settings.direct = true;
        if (settings.directAddress.NullOrEmpty())
            settings.directAddress = $"0.0.0.0:{MultiplayerServer.DefaultPort}";

        settings.steam = false;
        settings.arbiter = false;

        settingsUiBuffers.MaxPlayersBuffer = settings.maxPlayers.ToString();
        settingsUiBuffers.AutosaveBuffer = settings.autosaveInterval.ToString();

        saveGenerationStarted = false;

        if (pendingUploadState == null && Current.ProgramState != ProgramState.Playing)
            ResetTransientUiState();

        ApplyBootstrapState(bootstrapState, preserveTransientState: false);

        if (pendingUploadState != null && File.Exists(pendingUploadState.SavePath))
        {
            settings = pendingUploadState.Settings;
            step = Step.GenerateMap;
            settingsUploaded = true;
            saveReady = true;
            savedReplayPath = pendingUploadState.SavePath;
            statusText = pendingUploadState.StatusText;
            saveUploadStatus = "Save created. Reconnected to upload save.zip...";
            pendingUploadState = null;
        }
    }

    public override void PostClose()
    {
        base.PostClose();

        if (!retainInstanceOnClose)
            ResetTransientUiState();

        if (ReferenceEquals(Instance, this) && !retainInstanceOnClose)
            Instance = null;
    }

    internal void ResetTransientUiState(bool resetServerDrivenState = false)
    {
        AwaitingBootstrapMapInit = false;
        hideWindowDuringMapGen = false;
        autoAdvanceArmed = false;
        bootstrapSaveQueued = false;
        awaitingControllablePawns = false;
        isUploadingSave = false;
        saveUploadAutoStarted = false;
        postMapEnterSaveDelayRemaining = 0f;

        if (resetServerDrivenState)
        {
            saveUploadRequestedByServer = false;
            saveGenerationStarted = false;
        }
    }

    internal void ApplyBootstrapState(BootstrapServerState state, bool preserveTransientState = true)
    {
        bootstrapState = state;
        saveUploadRequestedByServer = state.RequiresSaveUpload;

        if (state.RequiresSettingsUpload)
        {
            step = Step.Settings;
            settingsUploaded = false;
            statusText = "Server settings.toml is missing. Configure it and upload it first.";
            return;
        }

        step = Step.GenerateMap;

        if (preserveTransientState && (saveReady || isUploadingSave || saveGenerationStarted || autoAdvanceArmed || AwaitingBootstrapMapInit || bootstrapSaveQueued || awaitingControllablePawns))
            return;

        statusText = saveUploadRequestedByServer
            ? "Server settings.toml already exists. Review the warning below, then create and upload save.zip."
            : "Waiting for the server to request save.zip generation.";
    }

    public override void DoWindowContents(Rect inRect)
    {
        var desiredHeight = GetDesiredWindowHeight();
        if (Event.current.type == EventType.Layout && !Mathf.Approximately(height, desiredHeight))
        {
            height = desiredHeight;
            SetInitialSizeAndPosition();
        }

        Text.Font = GameFont.Medium;
        Text.Anchor = TextAnchor.UpperCenter;
        Widgets.Label(inRect.Down(0f), "Server Bootstrap Configuration");
        Text.Anchor = TextAnchor.UpperLeft;

        Text.Font = GameFont.Small;
        var entry = new Rect(0f, 45f, inRect.width, 30f);
        entry.xMin += 4f;

        if (step == Step.Settings)
            DrawSettings(entry, inRect);
        else
            DrawGenerateMap(entry, inRect);
    }

    private float GetDesiredWindowHeight()
    {
        const float topPadding = 45f;
        const float bottomPadding = 55f;
        const float minHeight = 240f;
        const float maxHeight = 700f;

        var desired = topPadding + bottomPadding;

        if (step == Step.Settings)
            desired += 40f;

        if (step == Step.GenerateMap)
        {
            desired += GetGenerateMapStepHeight();
        }
        else
        {
            desired += GetSettingsStepHeight();
        }

        return Mathf.Clamp(desired, minHeight, maxHeight);
    }

    private float GetSettingsStepHeight()
    {
        var contentHeight = 0f;

        if (!string.IsNullOrEmpty(statusText))
            contentHeight += Text.CalcHeight(statusText, 500f) + 4f;

        if (isUploadingToml)
            contentHeight += 24f;

        contentHeight += 140f;
        contentHeight += Mathf.Max(GetActiveTabContentHeight(), 150f);
        contentHeight += 40f;

        return contentHeight;
    }

    private float GetActiveTabContentHeight()
    {
        if (tab == Tab.Connecting)
            return 5 * 30f;

        if (tab == Tab.Gameplay)
            return (MpVersion.IsDebug ? 9 : 8) * 30f;

        return 260f;
    }

    private sealed class PendingUploadState
    {
        public ServerSettings Settings;
        public string SavePath;
        public string StatusText;
    }

    public void Connected()
    {
    }

    public void Disconnected(SessionDisconnectInfo info)
    {
        ResetTransientUiState(resetServerDrivenState: true);
        Find.WindowStack.TryRemove(this);
    }
}

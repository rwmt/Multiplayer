using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Networking;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Multiplayer.Client
{
    /// <summary>
    /// Shown when connecting to a server that's in bootstrap/configuration mode.
    /// This window will guide the user through uploading settings.toml (if needed)
    /// and then save.zip.
    /// 
    /// NOTE: This is currently a minimal placeholder to wire the new join-flow.
    /// </summary>
    public class BootstrapConfiguratorWindow : Window
    {
        private readonly ConnectionBase connection;
        private string serverAddress;
        private int serverPort;
        private bool isReconnecting;
        private int reconnectCheckTimer;
        private ConnectionBase reconnectingConn;

        private ServerSettings settings;
        private ServerSettings serverSettings => settings;

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

        private Vector2 scroll;

        // UI buffers
        private ServerSettingsUI.BufferSet settingsUiBuffers = new();

        // toml preview
        private string tomlPreview;
        private Vector2 tomlScroll;

    private bool isUploadingToml;
    private float uploadProgress;
    private string statusText;
    private bool settingsUploaded;

        // Save.zip upload
        private bool isUploadingSave;
    private float saveUploadProgress;
    private string saveUploadStatus;
    private static string lastSavedReplayPath;
    private static bool lastSaveReady;

        // Autosave trigger (once) during bootstrap map generation
        private bool saveReady;
        private string savedReplayPath;

    private const string BootstrapSaveName = "Bootstrap";
    private bool saveUploadAutoStarted;
    private bool autoUploadAttempted;
        
        // Vanilla page auto-advance during bootstrap
        private bool autoAdvanceArmed;
        private float nextPressCooldown;
        private float randomTileCooldown;
        private const float NextPressCooldownSeconds = 0.45f;
        private const float RandomTileCooldownSeconds = 0.9f;
        private const float AutoAdvanceTimeoutSeconds = 180f;
        private float autoAdvanceElapsed;
    private bool worldGenDetected;
    private float worldGenDelayRemaining;
    private const float WorldGenDelaySeconds = 1f;

    // Diagnostics for the vanilla page driver. Throttled to avoid spamming logs.
    // Kept always-on because bootstrap issues are commonly hit by non-DevMode users.
    private float autoAdvanceDiagCooldown;
    private const float AutoAdvanceDiagCooldownSeconds = 2.0f;

    // Comprehensive tracing (opt-in via Prefs.DevMode OR always-on during bootstrap if needed).
    // We keep it enabled during bootstrap because it's a one-off flow and helps diagnose stuck states.
    private bool bootstrapTraceEnabled = false;
    private float bootstrapTraceSnapshotCooldown;
    private const float BootstrapTraceSnapshotSeconds = 2.5f;
    private string lastTraceKey;
    private string lastPageName;

    // Delay before saving after entering the map
    private float postMapEnterSaveDelayRemaining;
    private const float PostMapEnterSaveDelaySeconds = 1f;

    // Ensure we don't queue multiple saves.
    private bool bootstrapSaveQueued;

    // After entering a map, also wait until at least one controllable colonist pawn exists.
    // This is a more reliable "we're really in the map" signal than FinalizeInit alone,
    // especially with heavy modlists/long spawns.
    private bool awaitingControllablePawns;
    private float awaitingControllablePawnsElapsed;
    private const float AwaitControllablePawnsTimeoutSeconds = 30f;
        private bool startingLettersCleared;
        private bool landingDialogsCleared;
    
        // Static flag to track bootstrap map initialization
        public static bool AwaitingBootstrapMapInit = false;
        public static BootstrapConfiguratorWindow Instance;

        // Hide window during map generation/tile selection
        private bool hideWindowDuringMapGen = false;

        private const float LabelWidth = 110f;
        private const int MaxGameNameLength = 70;

        public override Vector2 InitialSize => new(550f, 620f);

        public BootstrapConfiguratorWindow(ConnectionBase connection)
        {
            this.connection = connection;
                Instance = this;

               // Save server address for reconnection after world generation
               serverAddress = Multiplayer.session?.address;
               serverPort = Multiplayer.session?.port ?? 0;

            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            forcePause = false;

            // Initialize with reasonable defaults for standalone/headless server
            settings = new ServerSettings
            {
                gameName = $"{Multiplayer.username}'s Server",
                direct = true,
                lan = false,
                steam = false,
                arbiter = false,
                maxPlayers = 8,
                autosaveInterval = 1,
                autosaveUnit = AutosaveUnit.Days
            };

            // Initialize UI buffers
            settingsUiBuffers.MaxPlayersBuffer = settings.maxPlayers.ToString();
            settingsUiBuffers.AutosaveBuffer = settings.autosaveInterval.ToString();

            // Choose the initial step based on what the server told us.
            step = Multiplayer.session?.serverBootstrapSettingsMissing == true ? Step.Settings : Step.GenerateMap;

            statusText = step == Step.Settings
                ? "Server settings.toml is missing. Configure and upload it."
                : "Server settings.toml is already configured.";

            if (Prefs.DevMode)
            {
                Log.Message($"[Bootstrap UI] Window created - step={step}, serverBootstrapSettingsMissing={Multiplayer.session?.serverBootstrapSettingsMissing}");
                Log.Message($"[Bootstrap UI] Initial status: {statusText}");
            }

            Trace("WindowCreated");

            // Check if we have a previously saved Bootstrap.zip from this session (reconnect case)
            if (!autoUploadAttempted && lastSaveReady && !string.IsNullOrEmpty(lastSavedReplayPath) && File.Exists(lastSavedReplayPath))
            {
                Log.Message($"[Bootstrap] Found previous Bootstrap.zip at {lastSavedReplayPath}, auto-uploading...");
                savedReplayPath = lastSavedReplayPath;
                saveReady = true;
                saveUploadStatus = "Save ready from previous session. Uploading...";
                saveUploadAutoStarted = true;
                autoUploadAttempted = true;
                StartUploadSaveZip();
            }

            RebuildTomlPreview();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;

            // Title
            Widgets.Label(inRect.Down(0), "Server Bootstrap Configuration");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            var entry = new Rect(0, 45, inRect.width, 30f);
            entry.xMin += 4;

            // Game name
            serverSettings.gameName = MpUI.TextEntryLabeled(entry, $"{"MpGameName".Translate()}:  ", serverSettings.gameName, LabelWidth);
            if (serverSettings.gameName.Length > MaxGameNameLength)
                serverSettings.gameName = serverSettings.gameName.Substring(0, MaxGameNameLength);

            entry = entry.Down(40);

            if (step == Step.Settings)
            {
                DrawSettings(entry, inRect);
            }
            else
            {
                DrawGenerateMap(entry, inRect);
            }
        }

        private void DrawGenerateMap(Rect entry, Rect inRect)
        {
            // Status text
            Text.Font = GameFont.Small;
            var statusHeight = Text.CalcHeight(statusText ?? "", entry.width);
            Widgets.Label(entry.Height(statusHeight), statusText ?? "");
            entry = entry.Down(statusHeight + 10);

            // Important notice about faction ownership
            if (!AwaitingBootstrapMapInit && !saveReady && !isUploadingSave && !isReconnecting)
            {
                var noticeRect = entry.Height(100f);
                GUI.color = new Color(1f, 0.85f, 0.5f);
                Widgets.DrawBoxSolid(noticeRect, new Color(0.3f, 0.25f, 0.1f, 0.5f));
                GUI.color = Color.white;

                var noticeTextRect = noticeRect.ContractedBy(8f);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(1f, 0.9f, 0.6f);
                Widgets.Label(noticeTextRect,
                    "IMPORTANT: The user who generates this map will own the main faction (colony).\n" +
                    "When setting up the server, make sure this user's username is listed as the host.\n" +
                    "Other players connecting to the server will be assigned as spectators or secondary factions.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                entry = entry.Down(110);
            }

            // Save upload status
            if (!string.IsNullOrEmpty(saveUploadStatus))
            {
                var saveStatusHeight = Text.CalcHeight(saveUploadStatus, entry.width);
                Widgets.Label(entry.Height(saveStatusHeight), saveUploadStatus);
                entry = entry.Down(saveStatusHeight + 4);
            }

            // Progress bar
            if (autoAdvanceArmed || isUploadingSave)
            {
                var barRect = entry.Height(18f);
                Widgets.FillableBar(barRect, isUploadingSave ? saveUploadProgress : 0.1f);
                entry = entry.Down(24);
            }

            // Generate map button
            bool showGenerateButton = !(autoAdvanceArmed || AwaitingBootstrapMapInit || saveReady || isUploadingSave || isReconnecting);
            if (showGenerateButton)
            {
                var buttonRect = new Rect((inRect.width - 200f) / 2f, inRect.height - 45f, 200f, 40f);
                if (Widgets.ButtonText(buttonRect, "Generate map"))
                {
                    saveUploadAutoStarted = false;
                    hideWindowDuringMapGen = true;
                    StartVanillaNewColonyFlow();
                }
            }

            // Auto-start upload when save is ready
            if (saveReady && !isUploadingSave && !saveUploadAutoStarted)
            {
                saveUploadAutoStarted = true;
                ReconnectAndUploadSave();
            }
        }

        private void DrawSettings(Rect entry, Rect inRect)
        {
            // Status + progress
            if (!string.IsNullOrEmpty(statusText))
            {
                var statusHeight = Text.CalcHeight(statusText, entry.width);
                Widgets.Label(entry.Height(statusHeight), statusText);
                entry = entry.Down(statusHeight + 4);
            }

            if (isUploadingToml)
            {
                var barRect = entry.Height(20f);
                Widgets.FillableBar(barRect, uploadProgress);
                entry = entry.Down(24);
            }

            // Tab buttons
            using (MpStyle.Set(TextAnchor.MiddleLeft))
            {
                DoTabButton(entry.Width(140).Height(40f), Tab.Connecting);
                DoTabButton(entry.Down(50f).Width(140).Height(40f), Tab.Gameplay);
                if (Prefs.DevMode)
                    DoTabButton(entry.Down(100f).Width(140).Height(40f), Tab.Preview);
            }

            // Content based on selected tab
            var contentRect = entry.MinX(entry.xMin + 150);
            var buffers = new ServerSettingsUI.BufferSet
            {
                MaxPlayersBuffer = settingsUiBuffers.MaxPlayersBuffer,
                AutosaveBuffer = settingsUiBuffers.AutosaveBuffer
            };

            if (tab == Tab.Connecting)
                ServerSettingsUI.DrawNetworkingSettings(contentRect, settings, buffers);
            else if (tab == Tab.Gameplay)
                ServerSettingsUI.DrawGameplaySettingsOnly(contentRect, settings, buffers);
            else if (tab == Tab.Preview)
            {
                RebuildTomlPreview();
                var previewRect = new Rect(contentRect.x, contentRect.y, contentRect.width, inRect.height - contentRect.y - 50f);
                DrawTomlPreview(previewRect);
            }
            
            // Sync buffers back
            settingsUiBuffers.MaxPlayersBuffer = buffers.MaxPlayersBuffer;
            settingsUiBuffers.AutosaveBuffer = buffers.AutosaveBuffer;

            // Buttons at bottom
            DrawSettingsButtons(new Rect(0, inRect.height - 40f, inRect.width, 35f));
        }

        private void DoTabButton(Rect r, Tab tab)
        {
            Widgets.DrawOptionBackground(r, tab == this.tab);
            if (Widgets.ButtonInvisible(r, true))
            {
                this.tab = tab;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            float num = r.x + 10f;
            Texture2D icon = null;
            string label;
            
            if (tab == Tab.Connecting)
            {
                icon = MultiplayerStatic.OptionsGeneral;
                label = "MpHostTabConnecting".Translate();
            }
            else if (tab == Tab.Gameplay)
            {
                icon = MultiplayerStatic.OptionsGameplay;
                label = "MpHostTabGameplay".Translate();
            }
            else
            {
                // No icon for preview tab, just label
                label = "Preview";
            }
            
            if (icon != null)
            {
                Rect rect = new Rect(num, r.y + (r.height - 20f) / 2f, 20f, 20f);
                GUI.DrawTexture(rect, icon);
                num += 30f;
            }
            
            Widgets.Label(new Rect(num, r.y, r.width - num, r.height), label);
        }

        private void DrawSettingsButtons(Rect inRect)
        {
            // Copy TOML button only in dev mode
            Rect nextRect;
            if (Prefs.DevMode)
            {
                var copyRect = new Rect(inRect.x, inRect.y, 150f, inRect.height);
                if (Widgets.ButtonText(copyRect, "Copy TOML"))
                {
                    RebuildTomlPreview();
                    GUIUtility.systemCopyBuffer = tomlPreview;
                    Messages.Message("Copied settings.toml to clipboard", MessageTypeDefOf.SilentInput, false);
                }
                nextRect = new Rect(inRect.xMax - 150f, inRect.y, 150f, inRect.height);
            }
            else
            {
                nextRect = new Rect((inRect.width - 150f) / 2f, inRect.y, 150f, inRect.height);
            }
            
            var nextLabel = settingsUploaded ? "Uploaded" : "Next";
            var nextEnabled = !isUploadingToml && !settingsUploaded;
            
            var prevColor = GUI.color;
            if (!nextEnabled)
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
            
            if (Widgets.ButtonText(nextRect, nextLabel))
            {
                if (nextEnabled)
                {
                    RebuildTomlPreview();
                    StartUploadSettingsToml(tomlPreview);
                }
            }
            
            GUI.color = prevColor;
        }

        private void StartUploadSettingsToml(string tomlText)
        {
            isUploadingToml = true;
            uploadProgress = 0f;
            statusText = "Uploading settings.toml...";

            // Upload on a background thread; network send is safe (it will be queued by the underlying net impl).
            var bytes = Encoding.UTF8.GetBytes(tomlText);
            var fileName = "settings.toml";
            byte[] sha256Hash;
            using (var hasher = SHA256.Create())
                sha256Hash = hasher.ComputeHash(bytes);

            new System.Threading.Thread(() =>
            {
                try
                {
                    connection.Send(new ClientBootstrapSettingsStartPacket(bytes.Length));

                    // Let ConnectionBase fragment internally (MaxFragmentPacketTotalSize ~32 MiB).
                    connection.SendFragmented(new ClientBootstrapSettingsDataPacket(bytes).Serialize());
                    OnMainThread.Enqueue(() => uploadProgress = 1f);

                    connection.Send(new ClientBootstrapSettingsEndPacket(sha256Hash));

                    OnMainThread.Enqueue(() =>
                    {
                        isUploadingToml = false;
                        settingsUploaded = true;
                        statusText = "Server settings configured correctly. Proceed with map generation.";
                        step = Step.GenerateMap;
                    });
                }
                catch (Exception e)
                {
                    OnMainThread.Enqueue(() =>
                    {
                        isUploadingToml = false;
                        statusText = $"Failed to upload settings.toml: {e.GetType().Name}: {e.Message}";
                    });
                }
            }) { IsBackground = true, Name = "MP Bootstrap TOML upload" }.Start();
        }

        private void StartVanillaNewColonyFlow()
        {
               // Disconnect from server before world generation to avoid sync conflicts.
               // We'll reconnect after the autosave is complete to upload save.zip.
               if (Multiplayer.session != null)
               {
                   Multiplayer.session.Stop();
                   Multiplayer.session = null;
               }

               // Start the vanilla flow offline
            try
            {
                // Ensure InitData exists for the page flow; RimWorld uses this heavily during new game setup.
                Current.Game ??= new Game();
                Current.Game.InitData ??= new GameInitData { startedFromEntry = true };
                
                // Ensure BootstrapCoordinator is added to the game components for tick reliability
                if (Current.Game.components.All(c => c is not BootstrapCoordinator))
                {
                    Current.Game.components.Add(new BootstrapCoordinator(Current.Game));
                    UnityEngine.Debug.Log("[Bootstrap] BootstrapCoordinator GameComponent added to Current.Game");
                }

                // Do NOT change programState; let vanilla handle it during the page flow
                var scenarioPage = new Page_SelectScenario();

                // StitchedPages handles correct "Next" navigation between Page(s).
                Find.WindowStack.Add(PageUtility.StitchedPages(new System.Collections.Generic.List<Page> { scenarioPage }));

                // Start watching for page flow + map entry.
                saveReady = false;
                savedReplayPath = null;
                saveUploadStatus = "After the save is uploaded, the server will automatically shut down. You will need to restart the server manually to complete the setup.";

                // Arm the vanilla page auto-advance driver
                autoAdvanceArmed = true;
                nextPressCooldown = 0f;
                randomTileCooldown = 0f;
                autoAdvanceElapsed = 0f;
                worldGenDetected = false;
                worldGenDelayRemaining = WorldGenDelaySeconds;
                autoAdvanceDiagCooldown = 0f;
                startingLettersCleared = false;
                landingDialogsCleared = false;
                AwaitingBootstrapMapInit = true;
                saveUploadStatus = "Generating map...";


                Trace("StartVanillaNewColonyFlow");
            }
            catch (Exception e)
            {
                Messages.Message($"Failed to start New Colony flow: {e.GetType().Name}: {e.Message}", MessageTypeDefOf.RejectInput, false);
                Trace($"StartVanillaNewColonyFlow:EX:{e.GetType().Name}");
            }
        }

        private void TryArmAwaitingBootstrapMapInit(string source)
        {
            // This is safe to call repeatedly.
            if (AwaitingBootstrapMapInit)
                return;

            // Avoid arming while long events are still running. During heavy initialization
            // we can briefly observe Playing+map before MapComponentUtility.FinalizeInit
            // runs; arming too early risks missing the FinalizeInit signal.
            try
            {
                if (LongEventHandler.AnyEventNowOrWaiting)
                {
                    if (bootstrapTraceEnabled)
                        Log.Message($"[BootstrapTrace] mapInit not armed yet ({source}): long event running");
                    return;
                }
            }
            catch
            {
                // If the API isn't available in a specific RW version, fail open.
            }

            if (Current.ProgramState != ProgramState.Playing)
            {
                if (bootstrapTraceEnabled)
                    Log.Message($"[BootstrapTrace] mapInit not armed yet ({source}): ProgramState={Current.ProgramState}");
                return;
            }

            if (Find.Maps == null || Find.Maps.Count == 0)
            {
                if (bootstrapTraceEnabled)
                    Log.Message($"[BootstrapTrace] mapInit not armed yet ({source}): no maps");
                return;
            }

            AwaitingBootstrapMapInit = true;
            saveUploadStatus = "Entered map. Waiting for initialization to complete...";
            // Keep this log lightweight (avoid Verse.Log stack traces).
            UnityEngine.Debug.Log($"[Bootstrap] Entered map detected via {source}. maps={Find.Maps.Count}");
            Trace("EnteredPlaying");

            // Stop page driver at this point.
            autoAdvanceArmed = false;
        }

        // Called from Root_Play.Start postfix (outside of the window update loop)
        internal void TryArmAwaitingBootstrapMapInit_FromRootPlay()
        {
            Trace("RootPlayStart");
            TryArmAwaitingBootstrapMapInit("Root_Play.Start");
        }

        // Called from Root_Play.Update postfix. This is the main reliable arming mechanism.
        internal void TryArmAwaitingBootstrapMapInit_FromRootPlayUpdate()
        {
            // If we're not in bootstrap flow there is nothing to do.
            // We treat the existence of the window as "bootstrap active".
            TryArmAwaitingBootstrapMapInit("Root_Play.Update");

            // Also drive the post-map save pipeline from this reliable update loop.
            TickPostMapEnterSaveDelayAndMaybeSave();

            // Once we have a reliable arming mechanism, we can reduce noisy periodic snapshots.
            // (We still keep event logs.)
            if (AwaitingBootstrapMapInit || postMapEnterSaveDelayRemaining > 0f || saveReady || isUploadingSave || isReconnecting)
                bootstrapTraceSnapshotCooldown = BootstrapTraceSnapshotSeconds; // delay next snapshot
        }
        
        public void OnBootstrapMapInitialized()
        {
            UnityEngine.Debug.Log($"[Bootstrap] OnBootstrapMapInitialized CALLED - AwaitingBootstrapMapInit={AwaitingBootstrapMapInit}");
            
            if (!AwaitingBootstrapMapInit)
            {
                UnityEngine.Debug.Log("[Bootstrap] OnBootstrapMapInitialized called but AwaitingBootstrapMapInit is false - ignoring");
                return;
            }
            
            // Show window again now that we're in the map
            hideWindowDuringMapGen = false;
            
            AwaitingBootstrapMapInit = false;
            // Wait a bit after entering the map before saving, to let final UI/world settle.
            postMapEnterSaveDelayRemaining = PostMapEnterSaveDelaySeconds;
            awaitingControllablePawns = true;
            awaitingControllablePawnsElapsed = 0f;
            bootstrapSaveQueued = false;
            saveUploadStatus = "Map initialized. Waiting before saving...";
            Trace("FinalizeInit");
        
            UnityEngine.Debug.Log($"[Bootstrap] Map initialized - postMapEnterSaveDelayRemaining={postMapEnterSaveDelayRemaining:F2}s, awaiting colonists");
            // Saving is driven by a tick loop (WindowUpdate + BootstrapCoordinator + Root_Play.Update).
            // Do not assume WindowUpdate keeps ticking during/after long events.
        }

        private void TickPostMapEnterSaveDelayAndMaybeSave()
        {
            // This is called from multiple tick sources; keep it idempotent.
            if (bootstrapSaveQueued || saveReady || isUploadingSave || isReconnecting)
                return;

            // Only run once we have been signalled by FinalizeInit.
            if (postMapEnterSaveDelayRemaining <= 0f)
                return;

            TraceSnapshotTick();

            // Drive the post-map delay. Use real time, not game ticks; during map init we still want
            // the save to happen shortly after the map becomes controllable.
            var prevRemaining = postMapEnterSaveDelayRemaining;
            postMapEnterSaveDelayRemaining -= Time.deltaTime;
            
            // Debug logging for delay countdown
            if (Mathf.FloorToInt(prevRemaining * 2) != Mathf.FloorToInt(postMapEnterSaveDelayRemaining * 2))
            {
                UnityEngine.Debug.Log($"[Bootstrap] Save delay countdown: {postMapEnterSaveDelayRemaining:F2}s remaining");
            }
            
            if (postMapEnterSaveDelayRemaining > 0f)
                return;

            // We reached the post-map-entry delay, now wait until we actually have spawned pawns.
            // This avoids saving too early in cases where the map exists but the colony isn't ready.
            if (awaitingControllablePawns)
            {
                awaitingControllablePawnsElapsed += Time.deltaTime;

                if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null)
                {
                    var anyColonist = false;
                    try
                    {
                        // Prefer FreeColonists: these are player controllable pawns.
                        // (Some versions/modlists may temporarily have an empty list during generation.)
                        anyColonist = Find.CurrentMap.mapPawns?.FreeColonistsSpawned != null &&
                                      Find.CurrentMap.mapPawns.FreeColonistsSpawned.Count > 0;
                        
                        if (!anyColonist && awaitingControllablePawnsElapsed < AwaitControllablePawnsTimeoutSeconds)
                        {
                            // Log periodically while waiting
                            if (Mathf.FloorToInt(awaitingControllablePawnsElapsed) != Mathf.FloorToInt(awaitingControllablePawnsElapsed - Time.deltaTime))
                            {
                                UnityEngine.Debug.Log($"[Bootstrap] Waiting for colonists... elapsed={awaitingControllablePawnsElapsed:F1}s");
                            }
                        }
                    }
                    catch
                    {
                        // ignored; we'll just keep waiting
                    }

                    if (anyColonist)
                    {
                        awaitingControllablePawns = false;

                        // Pause the game as soon as colonists are controllable so the snapshot is stable
                        try { Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; } catch { }

                        UnityEngine.Debug.Log("[Bootstrap] Controllable colonists detected, starting save");
                    }
                }

                if (awaitingControllablePawns)
                {
                    if (awaitingControllablePawnsElapsed > AwaitControllablePawnsTimeoutSeconds)
                    {
                        // Fallback: don't block forever; save anyway.
                        awaitingControllablePawns = false;
                        UnityEngine.Debug.LogWarning("[Bootstrap] Timed out waiting for controllable pawns; saving anyway");
                    }
                    else
                    {
                        saveUploadStatus = "Waiting for controllable colonists to spawn...";
                        Trace("WaitColonists");
                        return;
                    }
                }
            }

            // Ensure we don't re-enter this function multiple times and queue multiple saves.
            postMapEnterSaveDelayRemaining = 0f;
            bootstrapSaveQueued = true;

            saveUploadStatus = "Map initialized. Starting hosted MP session...";
            Trace("StartHost");
            
            UnityEngine.Debug.Log("[Bootstrap] All conditions met, initiating save sequence");

            // NEW FLOW: instead of vanilla save + manual repackaging,
            // 1) Host a local MP game programmatically (random port to avoid conflicts)
            // 2) Call standard MP save (SaveGameToFile_Overwrite) which produces a proper replay
            // 3) Close session and return to menu
            // Result: clean replay.zip ready to upload

            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    // 1. Host multiplayer game on random free port (OS assigns it)
                    var hostSettings = new ServerSettings
                    {
                        gameName = settings.gameName,
                        maxPlayers = 2,
                        direct = true,
                        directAddress = "0.0.0.0:0", // OS assigns free port
                        lan = false,
                        steam = false,
                    };

                    bool hosted = HostWindow.HostProgrammatically(hostSettings, file: null, randomDirectPort: false);
                    if (!hosted)
                    {
                        OnMainThread.Enqueue(() =>
                        {
                            saveUploadStatus = "Failed to host MP session.";
                            Log.Error("[Bootstrap] HostProgrammatically failed");
                            Trace("HostFailed");
                            bootstrapSaveQueued = false;
                        });
                        return;
                    }

                    Log.Message("[Bootstrap] Hosted MP session successfully. Now saving replay...");

                    OnMainThread.Enqueue(() =>
                    {
                        saveUploadStatus = "Hosted. Saving replay...";
                        Trace("HostSuccess");

                        // 2. Save as multiplayer replay (this uses the standard MP snapshot which includes maps correctly)
                        LongEventHandler.QueueLongEvent(() =>
                        {
                            try
                            {
                                Autosaving.SaveGameToFile_Overwrite(BootstrapSaveName, currentReplay: false);

                                var path = System.IO.Path.Combine(Multiplayer.ReplaysDir, $"{BootstrapSaveName}.zip");

                                OnMainThread.Enqueue(() =>
                                {
                                    savedReplayPath = path;
                                    saveReady = System.IO.File.Exists(savedReplayPath);
                                    lastSavedReplayPath = savedReplayPath;
                                    lastSaveReady = saveReady;

                                    if (saveReady)
                                    {
                                        saveUploadStatus = "Uploaded";
                                        Trace("SaveComplete");

                                        // 3. Exit to main menu (this also cleans up the local server)
                                        LongEventHandler.QueueLongEvent(() =>
                                        {
                                            GenScene.GoToMainMenu();

                                            OnMainThread.Enqueue(() =>
                                            {
                                                saveUploadStatus = "Reconnecting to upload save...";
                                                Trace("GoToMenuComplete");
                                                ReconnectAndUploadSave();
                                            });
                                        }, "Returning to menu", false, null);
                                    }
                                    else
                                    {
                                        saveUploadStatus = "Failed to upload settings.toml: {0}: {1}";
                                        Log.Error($"[Bootstrap] Save finished but file missing: {savedReplayPath}");
                                        Trace("SaveMissingFile");
                                        bootstrapSaveQueued = false;
                                    }
                                });
                            }
                            catch (Exception e)
                            {
                                OnMainThread.Enqueue(() =>
                                {
                                    saveUploadStatus = $"Save failed: {e.GetType().Name}: {e.Message}";
                                    Log.Error($"[Bootstrap] Save failed: {e}");
                                    Trace($"SaveEX:{e.GetType().Name}");
                                    bootstrapSaveQueued = false;
                                });
                            }
                        }, "Saving", false, null);
                    });
                }
                catch (Exception e)
                {
                    OnMainThread.Enqueue(() =>
                    {
                        saveUploadStatus = $"Host failed: {e.GetType().Name}: {e.Message}";
                        Log.Error($"[Bootstrap] Host exception: {e}");
                        Trace($"HostEX:{e.GetType().Name}");
                        bootstrapSaveQueued = false;
                    });
                }
            }, "Starting host", false, null);
        }

        public override void PreOpen()
        {
            base.PreOpen();
            UpdateWindowVisibility();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            UpdateWindowVisibility();

            // Debug logging
            if (AwaitingBootstrapMapInit && Time.frameCount % 120 == 0)
            {
                UnityEngine.Debug.Log($"[Bootstrap] WindowUpdate: AwaitingBootstrapMapInit={AwaitingBootstrapMapInit}, postMapDelay={postMapEnterSaveDelayRemaining:F2}, saveReady={saveReady}, programState={Current.ProgramState}");
            }

            // Always try to drive the save delay, even if BootstrapCoordinator isn't ticking
            // This ensures the autosave triggers even in edge cases
            TickPostMapEnterSaveDelayAndMaybeSave();

           if (isReconnecting)
               CheckReconnectionState();
        }

        private void UpdateWindowVisibility()
        {
            if (hideWindowDuringMapGen)
            {
                // Make window invisible by setting size to 0
                windowRect.width = 0;
                windowRect.height = 0;
            }
            else
            {
                // Restore normal size
                var size = InitialSize;
                if (windowRect.width == 0)
                {
                    windowRect.width = size.x;
                    windowRect.height = size.y;
                    // Center on screen
                    windowRect.x = (UI.screenWidth - size.x) / 2f;
                    windowRect.y = (UI.screenHeight - size.y) / 2f;
                }
            }
        }

        /// <summary>
        /// Called by <see cref="BootstrapCoordinator"/> once per second while the bootstrap window exists.
        /// This survives long events / MapInitializing where WindowUpdate may not tick reliably.
        /// </summary>
        internal void BootstrapCoordinatorTick()
        {
            // Try to arm map init reliably once the game has actually entered Playing.
            if (!AwaitingBootstrapMapInit)
                TryArmAwaitingBootstrapMapInit("BootstrapCoordinator");

            // Drive the post-map-entry save delay even if the window update isn't running smoothly.
            TickPostMapEnterSaveDelayAndMaybeSave();
        }

        private void TraceSnapshotTick()
        {
            if (!bootstrapTraceEnabled)
                return;

            if (bootstrapTraceSnapshotCooldown > 0f)
            {
                bootstrapTraceSnapshotCooldown -= Time.deltaTime;
                return;
            }

            bootstrapTraceSnapshotCooldown = BootstrapTraceSnapshotSeconds;

            var pageName = GetTopPageName();
            var mapCount = Find.Maps?.Count ?? 0;
            var curMap = Find.CurrentMap;
            var colonists = 0;
            try
            {
                colonists = curMap?.mapPawns?.FreeColonistsSpawned?.Count ?? 0;
            }
            catch
            {
                // ignored
            }

            Log.Message(
                $"[BootstrapTrace] state={Current.ProgramState} " +
                $"autoAdvance={autoAdvanceArmed} elapsed={autoAdvanceElapsed:0.0}s " +
                $"world={(Find.World != null ? "Y" : "N")} " +
                $"page={pageName} " +
                $"maps={mapCount} colonists={colonists} " +
                $"awaitMapInit={AwaitingBootstrapMapInit} postDelay={postMapEnterSaveDelayRemaining:0.00} " +
                $"saveReady={saveReady} uploading={isUploadingSave} reconnecting={isReconnecting}");
        }

        private void Trace(string key)
        {
            if (!bootstrapTraceEnabled)
                return;

            // Only print on transitions to keep logs readable.
            if (lastTraceKey == key)
                return;

            lastTraceKey = key;
            var pageName = GetTopPageName();
            Log.Message($"[BootstrapTrace] event={key} state={Current.ProgramState} page={pageName}");
        }

        private static string GetTopPageName()
        {
            try
            {
                var windows = Find.WindowStack?.Windows;
                if (windows == null)
                    return "<null>";

                for (int i = windows.Count - 1; i >= 0; i--)
                    if (windows[i] is Page p)
                        return p.GetType().Name;

                return "<none>";
            }
            catch
            {
                return "<ex>";
            }
        }

            // Legacy polling method removed: we now use the vanilla page flow + auto Next.

           private void ReconnectAndUploadSave()
           {
               saveUploadStatus = "Reconnecting to server...";

               try
               {
                   // Reconnect to the server (playerId will always be 0 in bootstrap)
                   Multiplayer.StopMultiplayer();

                   Multiplayer.session = new MultiplayerSession();
                   Multiplayer.session.address = serverAddress;
                   Multiplayer.session.port = serverPort;

                   var conn = ClientLiteNetConnection.Connect(serverAddress, serverPort);
                   conn.username = Multiplayer.username;
                   Multiplayer.session.client = conn;

                   // Start polling in WindowUpdate
                   isReconnecting = true;
                   reconnectCheckTimer = 0;
                   reconnectingConn = conn;
               }
               catch (Exception e)
               {
                   saveUploadStatus = $"Reconnection failed: {e.GetType().Name}: {e.Message}";
                   isUploadingSave = false;
               }
           }

       private void CheckReconnectionState()
       {
           reconnectCheckTimer++;
           
           if (reconnectingConn.State == ConnectionStateEnum.ClientBootstrap)
           {
               saveUploadStatus = "Reconnected. Starting upload...";
               isReconnecting = false;
               reconnectingConn = null;
               reconnectCheckTimer = 0;
               StartUploadSaveZip();
           }
           else if (reconnectingConn.State == ConnectionStateEnum.Disconnected)
           {
               saveUploadStatus = "Reconnection failed. Cannot upload save.zip.";
               isReconnecting = false;
               reconnectingConn = null;
               reconnectCheckTimer = 0;
               isUploadingSave = false;
           }
           else if (reconnectCheckTimer > 600) // 10 seconds at 60fps
           {
               saveUploadStatus = "Reconnection timeout. Cannot upload save.zip.";
               isReconnecting = false;
               reconnectingConn = null;
               reconnectCheckTimer = 0;
               isUploadingSave = false;
           }
       }

        private void StartUploadSaveZip()
        {
            if (string.IsNullOrWhiteSpace(savedReplayPath) || !System.IO.File.Exists(savedReplayPath))
            {
                saveUploadStatus = "Can't upload: autosave file not found.";
                return;
            }

            isUploadingSave = true;
            saveUploadProgress = 0f;
            saveUploadStatus = "Uploading save.zip...";

            byte[] bytes;
            try
            {
                bytes = System.IO.File.ReadAllBytes(savedReplayPath);
            }
            catch (Exception e)
            {
                isUploadingSave = false;
                saveUploadStatus = $"Failed to read autosave: {e.GetType().Name}: {e.Message}";
                return;
            }

            string sha256;
            using (var hasher = SHA256.Create())
                sha256 = hasher.ComputeHash(bytes).ToHexString();

            new System.Threading.Thread(() =>
            {
                try
                {
                    // Use reconnectingConn if we're in the reconnection flow, otherwise use the initial connection
                    var targetConn = isReconnecting && reconnectingConn != null ? reconnectingConn : connection;

                    targetConn.Send(new ClientBootstrapSaveStartPacket("save.zip", bytes.Length));

                    const int chunk = 256 * 1024;
                    var sent = 0;
                    while (sent < bytes.Length)
                    {
                        var len = Math.Min(chunk, bytes.Length - sent);
                        var part = new byte[len];
                        Buffer.BlockCopy(bytes, sent, part, 0, len);
                        targetConn.SendFragmented(new ClientBootstrapSaveDataPacket(part).Serialize());
                        sent += len;
                        var progress = bytes.Length == 0 ? 1f : (float)sent / bytes.Length;
                        OnMainThread.Enqueue(() => saveUploadProgress = Mathf.Clamp01(progress));
                    }

                    byte[] sha256Hash;
                    using (var hasher = SHA256.Create())
                        sha256Hash = hasher.ComputeHash(bytes);

                    targetConn.Send(new ClientBootstrapSaveEndPacket(sha256Hash));

                    OnMainThread.Enqueue(() =>
                    {
                        // Server will send ServerBootstrapCompletePacket and close connections.
                        saveUploadStatus = "Upload finished. Waiting for server to confirm and shut down...";
                    });
                }
                catch (Exception e)
                {
                    OnMainThread.Enqueue(() =>
                    {
                        isUploadingSave = false;
                        saveUploadStatus = $"Failed to upload save.zip: {e.GetType().Name}: {e.Message}";
                    });
                }
            }) { IsBackground = true, Name = "MP Bootstrap save upload" }.Start();
        }

        private void DrawTomlPreview(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);
            var inner = inRect.ContractedBy(10f);

            Text.Font = GameFont.Small;
            Widgets.Label(inner.TopPartPixels(22f), "settings.toml preview");

            var previewRect = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            var content = tomlPreview ?? "";

            var viewRect = new Rect(0f, 0f, previewRect.width - 16f, Mathf.Max(previewRect.height, Text.CalcHeight(content, previewRect.width - 16f) + 20f));
            Widgets.BeginScrollView(previewRect, ref tomlScroll, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), content);
            Widgets.EndScrollView();
        }

        private void RebuildTomlPreview()
        {
            var sb = new StringBuilder();

            // Important: This must mirror ServerSettings.ExposeData() keys.
            sb.AppendLine("# Generated by Multiplayer bootstrap configurator");
            sb.AppendLine("# Keys must match ServerSettings.ExposeData()\n");

            // ExposeData() order
            AppendKv(sb, "directAddress", settings.directAddress);
            AppendKv(sb, "maxPlayers", settings.maxPlayers);
            AppendKv(sb, "autosaveInterval", settings.autosaveInterval);
            AppendKv(sb, "autosaveUnit", settings.autosaveUnit.ToString());
            AppendKv(sb, "steam", settings.steam);
            AppendKv(sb, "direct", settings.direct);
            AppendKv(sb, "lan", settings.lan);
            AppendKv(sb, "asyncTime", settings.asyncTime);
            AppendKv(sb, "multifaction", settings.multifaction);
            AppendKv(sb, "debugMode", settings.debugMode);
            AppendKv(sb, "desyncTraces", settings.desyncTraces);
            AppendKv(sb, "syncConfigs", settings.syncConfigs);
            AppendKv(sb, "autoJoinPoint", settings.autoJoinPoint.ToString());
            AppendKv(sb, "devModeScope", settings.devModeScope.ToString());
            AppendKv(sb, "hasPassword", settings.hasPassword);
            AppendKv(sb, "password", settings.password ?? "");
            AppendKv(sb, "pauseOnLetter", settings.pauseOnLetter.ToString());
            AppendKv(sb, "pauseOnJoin", settings.pauseOnJoin);
            AppendKv(sb, "pauseOnDesync", settings.pauseOnDesync);
            AppendKv(sb, "timeControl", settings.timeControl.ToString());

            tomlPreview = sb.ToString();
        }

        private static void AppendKv(StringBuilder sb, string key, string value)
        {
            sb.Append(key);
            sb.Append(" = ");

            // Basic TOML escaping for strings
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.Append('"').Append(escaped).Append('"');
            sb.AppendLine();
        }

        private static void AppendKv(StringBuilder sb, string key, bool value)
        {
            sb.Append(key);
            sb.Append(" = ");
            sb.AppendLine(value ? "true" : "false");
        }

        private static void AppendKv(StringBuilder sb, string key, int value)
        {
            sb.Append(key);
            sb.Append(" = ");
            sb.AppendLine(value.ToString());
        }

        private static void AppendKv(StringBuilder sb, string key, float value)
        {
            // TOML uses '.' decimal separator
            sb.Append(key);
            sb.Append(" = ");
            sb.AppendLine(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

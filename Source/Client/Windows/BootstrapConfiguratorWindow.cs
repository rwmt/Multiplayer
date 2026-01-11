using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Multiplayer.Client.Comp;
using Multiplayer.Client.Networking;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

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

        private enum Step
        {
            Settings,
            GenerateMap
        }

        private Step step;

        private Vector2 scroll;

        // numeric buffers
        private string maxPlayersBuffer;
        private string autosaveIntervalBuffer;

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

        private const float LabelWidth = 210f;
        private const float RowHeight = 28f;
        private const float GapY = 6f;

        public override Vector2 InitialSize => new(700f, 520f);

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

            // Defaults aimed at standalone/headless:
            settings = new ServerSettings
            {
                direct = true,
                lan = false,
                steam = false,
                arbiter = false
            };

            // Choose the initial step based on what the server told us.
            // If we don't have an explicit "settings missing" signal, assume settings are already configured
            // and proceed to map generation.
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
            var headerRect = inRect.TopPartPixels(120f);
            Rect bodyRect;
            Rect buttonsRect = default;

            if (step == Step.Settings)
            {
                buttonsRect = inRect.BottomPartPixels(40f);
                bodyRect = new Rect(inRect.x, headerRect.yMax + 6f, inRect.width, inRect.height - headerRect.height - buttonsRect.height - 12f);
            }
            else
            {
                bodyRect = new Rect(inRect.x, headerRect.yMax + 6f, inRect.width, inRect.height - headerRect.height - 6f);
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect.TopPartPixels(32f), "Server bootstrap configuration");
            Text.Font = GameFont.Small;

            var infoRect = headerRect.BottomPartPixels(80f);
            var info = "The server is running in bootstrap mode (no settings.toml and/or save.zip).\n" +
                       "Fill out the settings below to generate a complete settings.toml.\n" +
                       "After applying settings, you'll upload save.zip in the next step.";
            Widgets.Label(infoRect, info);

            Rect leftRect;
            Rect rightRect;

            if (step == Step.Settings)
            {
                leftRect = bodyRect.LeftPart(0.58f).ContractedBy(4f);
                rightRect = bodyRect.RightPart(0.42f).ContractedBy(4f);

                DrawSettings(leftRect);
                DrawTomlPreview(rightRect);
                DrawSettingsButtons(buttonsRect);
            }
            else
            {
                // Single-column layout for map generation; remove the right-side steps box
                leftRect = bodyRect.ContractedBy(4f);
                rightRect = Rect.zero;
                DrawGenerateMap(leftRect, rightRect);
            }
        }

        private void DrawGenerateMap(Rect leftRect, Rect rightRect)
        {
            Widgets.DrawMenuSection(leftRect);

            var left = leftRect.ContractedBy(10f);

            Text.Font = GameFont.Medium;
            Widgets.Label(left.TopPartPixels(32f), "Server settings configured");
            Text.Font = GameFont.Small;

            // Important notice about faction ownership
            var noticeRect = new Rect(left.x, left.y + 40f, left.width, 80f);
            GUI.color = new Color(1f, 0.85f, 0.5f); // Warning yellow
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

            Widgets.Label(new Rect(left.x, noticeRect.yMax + 10f, left.width, 110f),
                "After the save is uploaded, the server will automatically shut down. You will need to restart the server manually to complete the setup.");

            // Hide the 'Generate map' button once the vanilla generation flow has started
            var btn = new Rect(left.x, noticeRect.yMax + 130f, 200f, 40f);
            bool showGenerateButton = !(autoAdvanceArmed || AwaitingBootstrapMapInit || saveReady || isUploadingSave || isReconnecting);
            if (showGenerateButton && Widgets.ButtonText(btn, "Generate map"))
            {
                saveUploadAutoStarted = false;
                StartVanillaNewColonyFlow();
            }

            var saveStatusY = (showGenerateButton ? btn.yMax : btn.y) + 10f;
            var statusRect = new Rect(left.x, saveStatusY, left.width, 60f);
            Widgets.Label(statusRect, saveUploadStatus ?? statusText ?? "");

            if (autoAdvanceArmed)
            {
                var barRect = new Rect(left.x, statusRect.yMax + 4f, left.width, 18f);
                Widgets.FillableBar(barRect, 0.1f);
            }

            if (isUploadingSave)
            {
                var barRect = new Rect(left.x, statusRect.yMax + 4f, left.width, 18f);
                Widgets.FillableBar(barRect, saveUploadProgress);
            }

            // Auto-start upload when save is ready
            if (saveReady && !isUploadingSave && !saveUploadAutoStarted)
            {
                saveUploadAutoStarted = true;
                   ReconnectAndUploadSave();
            }

            // Right-side steps box removed per request
        }

        private void DrawSettings(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);
            var inner = inRect.ContractedBy(10f);

            // Status + progress
            var statusRect = new Rect(inner.x, inner.y, inner.width, 54f);
            Widgets.Label(statusRect.TopPartPixels(28f), statusText ?? "");
            if (isUploadingToml)
            {
                var barRect = statusRect.BottomPartPixels(20f);
                Widgets.FillableBar(barRect, uploadProgress);
            }

            var contentRect = new Rect(inner.x, inner.y + 60f, inner.width, inner.height - 60f);

            // Keep the layout stable with a scroll view.
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, 760f);
            
            Widgets.BeginScrollView(contentRect, ref scroll, viewRect);

            float y = 0f;
            void Gap() => y += GapY;
            Rect Row() => new Rect(0f, y, viewRect.width, RowHeight);

            void Header(string label)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, y, viewRect.width, 32f), label);
                Text.Font = GameFont.Small;
                y += 34f;
            }

            Header("Networking");

            // direct
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Enable Direct hosting (recommended for standalone/headless).");
                CheckboxLabeled(r, "Direct", ref settings.direct);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "One or more endpoints, separated by ';'. Example: 0.0.0.0:30502");
                TextFieldLabeled(r, "Direct address", ref settings.directAddress);
                y += RowHeight;
                Gap();
            }

            // lan
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Enable LAN broadcasting (typically off for headless servers).");
                CheckboxLabeled(r, "LAN", ref settings.lan);
                y += RowHeight;
                Gap();
            }

            // steam
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Steam hosting is not supported by the standalone server.");
                CheckboxLabeled(r, "Steam", ref settings.steam);
                y += RowHeight;
                Gap();
            }

            Header("Server limits");

            // max players
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Maximum number of players allowed to connect.");
                TextFieldNumericLabeled(r, "Max players", ref settings.maxPlayers, ref maxPlayersBuffer, 1, 999);
                y += RowHeight;
                Gap();
            }

            // password
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Require a password to join.");
                CheckboxLabeled(r, "Has password", ref settings.hasPassword);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "Password (only used if Has password is enabled).");
                TextFieldLabeled(r, "Password", ref settings.password);
                y += RowHeight;
                Gap();
            }

            Header("Saves / autosaves");

            // autosave interval + unit
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Autosave interval. Unit is configured separately below.");
                TextFieldNumericLabeled(r, "Autosave interval", ref settings.autosaveInterval, ref autosaveIntervalBuffer, 0f, 999f);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "Autosave unit.");
                EnumDropdownLabeled(r, "Autosave unit", settings.autosaveUnit, v => settings.autosaveUnit = v);
                y += RowHeight;
                Gap();
            }

            Header("Gameplay options");

            // async time
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Allow async time. (Once enabled in a save, usually can't be disabled.)");
                CheckboxLabeled(r, "Async time", ref settings.asyncTime);
                y += RowHeight;
            }

            // multifaction
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Enable multi-faction play.");
                CheckboxLabeled(r, "Multi-faction", ref settings.multifaction);
                y += RowHeight;
                Gap();
            }

            // time control
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Who controls game speed.");
                EnumDropdownLabeled(r, "Time control", settings.timeControl, v => settings.timeControl = v);
                y += RowHeight;
                Gap();
            }

            // auto join point
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "When clients automatically join (flags). Stored as a string in TOML.");
                TextFieldLabeled(r, "When clients automatically join (flags). Stored as a string in TOML.", ref settings.autoJoinPoint);
                y += RowHeight;
                Gap();
            }

            // pause behavior
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "When to automatically pause on letters.");
                EnumDropdownLabeled(r, "When to automatically pause on letters.", settings.pauseOnLetter, v => settings.pauseOnLetter = v);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "Pause when a player joins.");
                CheckboxLabeled(r, "Pause when a player joins.", ref settings.pauseOnJoin);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "Pause on desync.");
                CheckboxLabeled(r, "Pause on desync.", ref settings.pauseOnDesync);
                y += RowHeight;
                Gap();
            }

            Header("Debug / development");

            // debug mode
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Enable debug mode.");
                CheckboxLabeled(r, "Enable debug mode.", ref settings.debugMode);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "Include desync traces to help debugging.");
                CheckboxLabeled(r, "Include desync traces to help debugging.", ref settings.desyncTraces);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "Sync mod configs to clients.");
                CheckboxLabeled(r, "Sync mod configs to clients.", ref settings.syncConfigs);
                y += RowHeight;

                r = Row();
                TooltipHandler.TipRegion(r, "Dev mode scope.");
                EnumDropdownLabeled(r, "Dev mode scope.", settings.devModeScope, v => settings.devModeScope = v);
                y += RowHeight;
                Gap();
            }

            // unsupported settings but still in schema
            Header("Standalone limitations");
            {
                var r = Row();
                TooltipHandler.TipRegion(r, "Arbiter is not supported in standalone server.");
                CheckboxLabeled(r, "Arbiter is not supported in standalone server.", ref settings.arbiter);
                y += RowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawSettingsButtons(Rect inRect)
        {
            var buttons = inRect.ContractedBy(4f);
            
            var copyRect = buttons.LeftPart(0.5f).ContractedBy(2f);
            if (Widgets.ButtonText(copyRect, "Copy TOML"))
            {
                RebuildTomlPreview();
                GUIUtility.systemCopyBuffer = tomlPreview;
                Messages.Message("Copied settings.toml to clipboard", MessageTypeDefOf.SilentInput, false);
            }

            var nextRect = buttons.RightPart(0.5f).ContractedBy(2f);
            var nextLabel = settingsUploaded ? "Uploaded" : "Next";
            var nextEnabled = !isUploadingToml && !settingsUploaded;
            
            // Always show the button, just change color when disabled
            var prevColor = GUI.color;
            if (!nextEnabled)
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
            
            if (Widgets.ButtonText(nextRect, nextLabel))
            {
                if (nextEnabled)
                {
                    // Upload generated settings.toml to the server.
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
            string sha256;
            using (var hasher = SHA256.Create())
                sha256 = hasher.ComputeHash(bytes).ToHexString();

            new System.Threading.Thread(() =>
            {
                try
                {
                    connection.Send(new ClientBootstrapSettingsUploadStartPacket(fileName, bytes.Length));

                    const int chunk = 64 * 1024; // safe: packet will be fragmented by ConnectionBase
                    var sent = 0;
                    while (sent < bytes.Length)
                    {
                        var len = Math.Min(chunk, bytes.Length - sent);
                        var part = new byte[len];
                        Buffer.BlockCopy(bytes, sent, part, 0, len);
                        connection.SendFragmented(new ClientBootstrapSettingsUploadDataPacket(part).Serialize());
                        sent += len;
                        var progress = bytes.Length == 0 ? 1f : (float)sent / bytes.Length;
                        OnMainThread.Enqueue(() => uploadProgress = Mathf.Clamp01(progress));
                    }

                    connection.Send(new ClientBootstrapSettingsUploadFinishPacket(sha256));

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
            if (!AwaitingBootstrapMapInit)
            {
                UnityEngine.Debug.Log("[Bootstrap] OnBootstrapMapInitialized called but AwaitingBootstrapMapInit is false - ignoring");
                return;
            }
            
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
                    // 1. Host multiplayer game on random free port (avoid collisions with user's server)
                    int freePort = HostWindow.GetFreeUdpPort();
                    var hostSettings = new ServerSettings
                    {
                        gameName = "BootstrapHost",
                        maxPlayers = 2,
                        direct = true,
                        directPort = freePort,
                        directAddress = $"0.0.0.0:{freePort}",
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

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            // Always try to drive the save delay, even if BootstrapCoordinator isn't ticking
            // This ensures the autosave triggers even in edge cases
            TickPostMapEnterSaveDelayAndMaybeSave();

           if (isReconnecting)
               CheckReconnectionState();
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

                    targetConn.Send(new ClientBootstrapUploadStartPacket("save.zip", bytes.Length));

                    const int chunk = 256 * 1024;
                    var sent = 0;
                    while (sent < bytes.Length)
                    {
                        var len = Math.Min(chunk, bytes.Length - sent);
                        var part = new byte[len];
                        Buffer.BlockCopy(bytes, sent, part, 0, len);
                        targetConn.SendFragmented(new ClientBootstrapUploadDataPacket(part).Serialize());
                        sent += len;
                        var progress = bytes.Length == 0 ? 1f : (float)sent / bytes.Length;
                        OnMainThread.Enqueue(() => saveUploadProgress = Mathf.Clamp01(progress));
                    }

                    targetConn.Send(new ClientBootstrapUploadFinishPacket(sha256));

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

        private void CheckboxLabeled(Rect r, string label, ref bool value)
        {
            var labelRect = r.LeftPartPixels(LabelWidth);
            var boxRect = r.RightPartPixels(r.width - LabelWidth);
            Widgets.Label(labelRect, label);
            var oldValue = value;
            Widgets.Checkbox(boxRect.x, boxRect.y + (boxRect.height - 24f) / 2f, ref value, 24f);
            if (value != oldValue)
                RebuildTomlPreview();
        }

        private void TextFieldLabeled(Rect r, string label, ref string value)
        {
            var labelRect = r.LeftPartPixels(LabelWidth);
            var fieldRect = r.RightPartPixels(r.width - LabelWidth);
            Widgets.Label(labelRect, label);
            var oldValue = value;
            value = Widgets.TextField(fieldRect, value ?? "");
            if (value != oldValue)
                RebuildTomlPreview();
        }

        private void TextFieldLabeled(Rect r, string label, ref AutoJoinPointFlags value)
        {
            var labelRect = r.LeftPartPixels(LabelWidth);
            var fieldRect = r.RightPartPixels(r.width - LabelWidth);
            Widgets.Label(labelRect, label);

            // Keep it simple for now: user edits the enum string ("Join, Desync").
            // We'll still emit it as string exactly like Server.TomlSettings.Save would.
            var oldValue = value;
            var str = Widgets.TextField(fieldRect, value.ToString());
            if (Enum.TryParse(str, out AutoJoinPointFlags parsed))
                value = parsed;
            if (value != oldValue)
                RebuildTomlPreview();
        }

        private void TextFieldNumericLabeled(Rect r, string label, ref int value, ref string buffer, int min, int max)
        {
            var labelRect = r.LeftPartPixels(LabelWidth);
            var fieldRect = r.RightPartPixels(r.width - LabelWidth);
            Widgets.Label(labelRect, label);
            var oldValue = value;
            Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, min, max);
            if (value != oldValue)
                RebuildTomlPreview();
        }

        private void TextFieldNumericLabeled(Rect r, string label, ref float value, ref string buffer, float min, float max)
        {
            var labelRect = r.LeftPartPixels(LabelWidth);
            var fieldRect = r.RightPartPixels(r.width - LabelWidth);
            Widgets.Label(labelRect, label);
            var oldValue = value;
            Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, min, max);
            if (value != oldValue)
                RebuildTomlPreview();
        }

        private void EnumDropdownLabeled<T>(Rect r, string label, T value, Action<T> setValue) where T : struct, Enum
        {
            var labelRect = r.LeftPartPixels(LabelWidth);
            var buttonRect = r.RightPartPixels(r.width - LabelWidth);
            Widgets.Label(labelRect, label);

            var buttonLabel = value.ToString();
            if (!Widgets.ButtonText(buttonRect, buttonLabel))
                return;

            var options = new System.Collections.Generic.List<FloatMenuOption>();
            foreach (var v in Enum.GetValues(typeof(T)))
            {
                var cast = (T)v;
                var captured = cast;
                options.Add(new FloatMenuOption(captured.ToString(), () => 
                {
                    setValue(captured);
                    RebuildTomlPreview();
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client;

public partial class BootstrapConfiguratorWindow
{
    private const string BootstrapSaveName = "MpBootstrapSave";
    private const float PostMapEnterSaveDelaySeconds = 1.5f;

    private bool hideWindowDuringMapGen;
    private bool autoAdvanceArmed;
    private bool bootstrapSaveQueued;
    private bool awaitingControllablePawns;
    private bool saveReady;
    private bool isUploadingSave;
    private bool saveUploadAutoStarted;
    private string savedReplayPath;
    private string saveUploadStatus;
    private float saveUploadProgress;
    private float postMapEnterSaveDelayRemaining;

    private float GetGenerateMapStepHeight()
    {
        var contentHeight = Text.CalcHeight(statusText ?? string.Empty, 500f) + 14f;

        if (ShouldShowGenerateMapControls())
            contentHeight += 110f;

        if (!string.IsNullOrEmpty(saveUploadStatus))
            contentHeight += Text.CalcHeight(saveUploadStatus, 500f) + 8f;

        if (autoAdvanceArmed || isUploadingSave)
            contentHeight += 24f;

        contentHeight += 55f;
        return contentHeight;
    }

    private void DrawGenerateMap(Rect entry, Rect inRect)
    {
        var statusHeight = Text.CalcHeight(statusText ?? string.Empty, entry.width);
        Widgets.Label(entry.Height(statusHeight), statusText ?? string.Empty);
        entry = entry.Down(statusHeight + 10f);

        if (ShouldShowGenerateMapControls())
        {
            DrawFactionOwnershipNotice(entry.Height(100f));
            entry = entry.Down(110f);
        }

        if (!string.IsNullOrEmpty(saveUploadStatus))
        {
            var saveStatusHeight = Text.CalcHeight(saveUploadStatus, entry.width);
            Widgets.Label(entry.Height(saveStatusHeight), saveUploadStatus);
            entry = entry.Down(saveStatusHeight + 4f);
        }

        if (autoAdvanceArmed || isUploadingSave)
        {
            var barRect = entry.Height(18f);
            Widgets.FillableBar(barRect, isUploadingSave ? saveUploadProgress : 0.1f);
            entry = entry.Down(24f);
        }

        if (ShouldAutoUploadSave())
        {
            saveUploadAutoStarted = true;
            StartUploadSaveZip();
        }

        if (ShouldShowGenerateMapButton() && Widgets.ButtonText(new Rect((inRect.width - 200f) / 2f, inRect.height - 45f, 200f, 40f), "Create game and upload save"))
        {
            saveUploadAutoStarted = false;
            StartVanillaNewColonyFlow();
        }
    }

    private void DrawFactionOwnershipNotice(Rect noticeRect)
    {
        GUI.color = new Color(1f, 0.85f, 0.5f);
        Widgets.DrawBoxSolid(noticeRect, new Color(0.3f, 0.25f, 0.1f, 0.5f));
        GUI.color = Color.white;

        var noticeTextRect = noticeRect.ContractedBy(8f);
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(1f, 0.9f, 0.6f);
        Widgets.Label(noticeTextRect,
            "IMPORTANT: The user who generates this map will own the main faction (colony).\n" +
            "Make sure this user's username is listed as the host when the final server starts.");
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }

    private bool ShouldShowGenerateMapButton() =>
        saveUploadRequestedByServer && !saveGenerationStarted && !autoAdvanceArmed && !AwaitingBootstrapMapInit && !saveReady && !isUploadingSave;

    private bool ShouldShowGenerateMapControls() => ShouldShowGenerateMapButton();

    private bool ShouldAutoUploadSave() =>
        saveReady && !isUploadingSave && !saveUploadAutoStarted && connection.State == ConnectionStateEnum.ClientBootstrap;

    private void StartVanillaNewColonyFlow()
    {
        if (Multiplayer.session != null)
        {
            Multiplayer.session.Stop();
            Multiplayer.session = null;
        }

        Current.Game ??= new Game();
        Current.Game.InitData ??= new GameInitData { startedFromEntry = true };

        if (Current.Game.components.All(component => component is not BootstrapCoordinator))
            Current.Game.components.Add(new BootstrapCoordinator(Current.Game));

        retainInstanceOnClose = true;
        hideWindowDuringMapGen = true;
        saveGenerationStarted = true;
        saveUploadRequestedByServer = false;
        saveReady = false;
        savedReplayPath = null;
        autoAdvanceArmed = true;
        AwaitingBootstrapMapInit = true;
        saveUploadStatus = "Generating map...";
        Find.WindowStack.TryRemove(this);

        var scenarioPage = new Page_SelectScenario();
        Find.WindowStack.Add(PageUtility.StitchedPages([scenarioPage]));
    }

    private void TryArmAwaitingBootstrapMapInit()
    {
        if (AwaitingBootstrapMapInit)
            return;

        if (Multiplayer.Client != null || bootstrapSaveQueued || saveReady || isUploadingSave || saveUploadAutoStarted)
            return;

        if (Current.ProgramState != ProgramState.Playing || Find.Maps == null || Find.Maps.Count == 0)
            return;

        AwaitingBootstrapMapInit = true;
        autoAdvanceArmed = false;
        saveUploadStatus = "Entered map. Waiting for initialization to complete...";
    }

    internal void TryArmAwaitingBootstrapMapInit_FromRootPlay() => TryArmAwaitingBootstrapMapInit();

    internal void TryArmAwaitingBootstrapMapInit_FromRootPlayUpdate()
    {
        TryArmAwaitingBootstrapMapInit();
        TickPostMapEnterSaveDelayAndMaybeSave();
    }

    internal void BootstrapCoordinatorTick() => TickPostMapEnterSaveDelayAndMaybeSave();

    public void OnBootstrapMapInitialized()
    {
        if (!AwaitingBootstrapMapInit)
            return;

        hideWindowDuringMapGen = false;
        retainInstanceOnClose = false;
        AwaitingBootstrapMapInit = false;
        postMapEnterSaveDelayRemaining = PostMapEnterSaveDelaySeconds;
        awaitingControllablePawns = true;
        bootstrapSaveQueued = false;
        saveUploadStatus = "Map initialized. Waiting before saving...";

        if (Find.WindowStack.WindowOfType<BootstrapConfiguratorWindow>() == null)
            Find.WindowStack.Add(this);
    }

    private void TickPostMapEnterSaveDelayAndMaybeSave()
    {
        if (hideWindowDuringMapGen || bootstrapSaveQueued || saveReady || isUploadingSave)
            return;

        if (postMapEnterSaveDelayRemaining <= 0f && !awaitingControllablePawns)
            return;

        postMapEnterSaveDelayRemaining -= Time.deltaTime;
        if (postMapEnterSaveDelayRemaining > 0f)
            return;

        if (!WaitForControllableColonists())
            return;

        postMapEnterSaveDelayRemaining = 0f;
        bootstrapSaveQueued = true;
        saveUploadStatus = "Map initialized. Starting hosted MP session...";

        LongEventHandler.QueueLongEvent(StartHostedBootstrapSaveCreation, "Starting host", false, null);
    }

    private bool WaitForControllableColonists()
    {
        if (!awaitingControllablePawns)
            return true;

        if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null)
        {
            var anyColonist = Find.CurrentMap.mapPawns?.FreeColonistsSpawned != null &&
                              Find.CurrentMap.mapPawns.FreeColonistsSpawned.Count > 0;

            if (anyColonist)
            {
                awaitingControllablePawns = false;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            }
        }

        if (!awaitingControllablePawns)
            return true;

        saveUploadStatus = "Waiting for controllable colonists to spawn...";
        return false;
    }

    private void StartHostedBootstrapSaveCreation()
    {
        try
        {
            var hostSettings = new ServerSettings
            {
                gameName = settings.gameName,
                maxPlayers = 2,
                direct = false,
                lan = false,
                steam = false,
                arbiter = false,
                asyncTime = settings.asyncTime,
                multifaction = settings.multifaction,
                debugMode = settings.debugMode,
                desyncTraces = settings.desyncTraces,
                syncConfigs = settings.syncConfigs,
                autoJoinPoint = settings.autoJoinPoint,
                devModeScope = settings.devModeScope,
                pauseOnLetter = settings.pauseOnLetter,
                pauseOnJoin = settings.pauseOnJoin,
                pauseOnDesync = settings.pauseOnDesync,
                timeControl = settings.timeControl
            };

            if (!HostWindow.HostProgrammatically(hostSettings))
            {
                OnMainThread.Enqueue(() =>
                {
                    saveUploadStatus = "Failed to host MP session.";
                    bootstrapSaveQueued = false;
                });
                return;
            }

            OnMainThread.Enqueue(() =>
            {
                saveUploadStatus = "Hosted. Saving replay...";
                LongEventHandler.QueueLongEvent(CreateBootstrapReplaySave, "Saving", false, null);
            });
        }
        catch (Exception exception)
        {
            OnMainThread.Enqueue(() =>
            {
                saveUploadStatus = $"Host failed: {exception.GetType().Name}: {exception.Message}";
                Log.Error($"Bootstrap host exception: {exception}");
                bootstrapSaveQueued = false;
            });
        }
    }

    private void CreateBootstrapReplaySave()
    {
        try
        {
            Autosaving.SaveGameToFile_Overwrite(BootstrapSaveName, currentReplay: false);
            var path = Path.Combine(Multiplayer.ReplaysDir, $"{BootstrapSaveName}.zip");
            OnMainThread.Enqueue(() => FinalizeBootstrapSave(path));
        }
        catch (Exception exception)
        {
            OnMainThread.Enqueue(() =>
            {
                saveUploadStatus = $"Save failed: {exception.GetType().Name}: {exception.Message}";
                Log.Error($"Bootstrap save failed: {exception}");
                bootstrapSaveQueued = false;
            });
        }
    }

    private void FinalizeBootstrapSave(string path)
    {
        savedReplayPath = path;
        saveReady = File.Exists(savedReplayPath);

        if (!saveReady)
        {
            saveUploadStatus = $"Save finished but file not found: {path}";
            Log.Error($"Bootstrap save finished but file was missing: {path}");
            bootstrapSaveQueued = false;
            return;
        }

        pendingUploadState = new PendingUploadState
        {
            Settings = MpUtil.ShallowCopy(settings, new ServerSettings()),
            SavePath = savedReplayPath,
            StatusText = statusText ?? string.Empty
        };

        saveUploadStatus = "Save created. Returning to menu...";
        LongEventHandler.QueueLongEvent(ReturnToMenuAndReconnect, "Returning to menu", false, null);
    }

    private void ReturnToMenuAndReconnect()
    {
        GenScene.GoToMainMenu();
        OnMainThread.Enqueue(() =>
        {
            saveUploadStatus = "Reconnecting to upload save...";
            Multiplayer.StopMultiplayer();

            if (reconnectConnector == null)
            {
                saveUploadStatus = "No connector available to reconnect to the bootstrap server.";
                return;
            }

            ClientUtil.TryConnectWithWindow(reconnectConnector, false);
        });
    }

    private void StartUploadSaveZip()
    {
        if (string.IsNullOrWhiteSpace(savedReplayPath) || !File.Exists(savedReplayPath))
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
            bytes = File.ReadAllBytes(savedReplayPath);
        }
        catch (Exception exception)
        {
            isUploadingSave = false;
            saveUploadStatus = $"Failed to read autosave: {exception.GetType().Name}: {exception.Message}";
            return;
        }

        new System.Threading.Thread(() =>
        {
            try
            {
                connection.Send(new ClientBootstrapSaveStartPacket("save.zip", bytes.Length));

                const int chunkSize = 256 * 1024;
                var sent = 0;
                while (sent < bytes.Length)
                {
                    var len = Math.Min(chunkSize, bytes.Length - sent);
                    var part = new byte[len];
                    Buffer.BlockCopy(bytes, sent, part, 0, len);
                    connection.SendFragmented(new ClientBootstrapSaveDataPacket(part).Serialize());
                    sent += len;
                    var progress = bytes.Length == 0 ? 1f : (float)sent / bytes.Length;
                    OnMainThread.Enqueue(() => saveUploadProgress = Mathf.Clamp01(progress));
                }

                byte[] hash;
                using (var hasher = SHA256.Create())
                    hash = hasher.ComputeHash(bytes);

                connection.Send(new ClientBootstrapSaveEndPacket(hash));
                OnMainThread.Enqueue(() =>
                {
                    saveUploadStatus = "Upload finished. Waiting for server to confirm and shut down...";
                });
            }
            catch (Exception exception)
            {
                OnMainThread.Enqueue(() =>
                {
                    isUploadingSave = false;
                    saveUploadStatus = $"Failed to upload save.zip: {exception.GetType().Name}: {exception.Message}";
                });
            }
        }) { IsBackground = true, Name = "MP Bootstrap save upload" }.Start();
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using LiteNetLib;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using Steamworks;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;
using Debug = UnityEngine.Debug;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public static class MultiplayerStatic
    {
        public static KeyBindingDef ToggleChatDef = KeyBindingDef.Named("MpToggleChat");
        public static KeyBindingDef PingKeyDef = KeyBindingDef.Named("MpPingKey");

        public static readonly Texture2D PingBase = ContentFinder<Texture2D>.Get("Multiplayer/PingBase");
        public static readonly Texture2D PingPin = ContentFinder<Texture2D>.Get("Multiplayer/PingPin");
        public static readonly Texture2D WebsiteIcon = ContentFinder<Texture2D>.Get("Multiplayer/Website");
        public static readonly Texture2D DiscordIcon = ContentFinder<Texture2D>.Get("Multiplayer/Discord");
        public static readonly Texture2D Pulse = ContentFinder<Texture2D>.Get("Multiplayer/Pulse");

        public static readonly Texture2D ChangeRelationIcon = ContentFinder<Texture2D>.Get("UI/Icons/VisitorsHelp");

        static MultiplayerStatic()
        {
            Native.InitLmfPtr(
                Application.platform switch
                {
                    RuntimePlatform.LinuxEditor => Native.NativeOS.Linux,
                    RuntimePlatform.LinuxPlayer => Native.NativeOS.Linux,
                    RuntimePlatform.OSXEditor => Native.NativeOS.OSX,
                    RuntimePlatform.OSXPlayer => Native.NativeOS.OSX,
                    _ => Native.NativeOS.Windows
                }
            );

            Native.HarmonyOriginalGetter = MpUtil.GetOriginalFromHarmonyReplacement;

            // UnityEngine.Debug.Log instead of Verse.Log.Message because the server runs on its own thread
            ServerLog.info = str => Debug.Log($"MpServerLog: {str}");
            ServerLog.error = str => Debug.Log($"MpServerLog Error: {str}");
            NetDebug.Logger = new ServerLog();

            SetUsername();

            if (SteamManager.Initialized)
                SteamIntegration.InitCallbacks();

            Log.Message($"Multiplayer version {MpVersion.Version}");
            Log.Message($"Player's username: {Multiplayer.username}");

            var persistentObj = new GameObject();
            persistentObj.AddComponent<OnMainThread>();
            UnityEngine.Object.DontDestroyOnLoad(persistentObj);

            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientSteam, typeof(ClientSteamState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientJoining, typeof(ClientJoiningState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientLoading, typeof(ClientLoadingState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientPlaying, typeof(ClientPlayingState));

            MultiplayerData.CollectCursorIcons();

            PersistentDialog.BindAll(typeof(Multiplayer).Assembly);

            using (DeepProfilerWrapper.Section("Multiplayer MpPatches"))
                Multiplayer.harmony.DoAllMpPatches();

            using (DeepProfilerWrapper.Section("Multiplayer patches"))
                DoPatches();

            Log.messageQueue.maxMessages = 1000;

            DoubleLongEvent(() =>
            {
                MultiplayerData.CollectDefInfos();
                Sync.PostInitHandlers();
            }, "Loading"); // Right before the events from HandleCommandLine

            HandleRestartConnect();
            HandleCommandLine();

            if (Multiplayer.arbiterInstance)
            {
                RuntimeHelpers.RunClassConstructor(typeof(Text).TypeHandle);
            }

            using (DeepProfilerWrapper.Section("Multiplayer TakeModDataSnapshot"))
                JoinData.TakeModDataSnapshot();

            using (DeepProfilerWrapper.Section("MultiplayerData PrecacheMods"))
                MultiplayerData.PrecacheMods();

            if (GenCommandLine.CommandLineArgPassed("profiler"))
                SimpleProfiler.Print("mp_prof_out.txt");

            MultiplayerData.staticCtorRoundMode = RoundMode.GetCurrentRoundMode();
        }

        private static void DoubleLongEvent(Action action, string textKey)
        {
            LongEventHandler.QueueLongEvent(() => LongEventHandler.QueueLongEvent(action, textKey, false, null), textKey, false, null);
        }

        private static void SetUsername()
        {
            Multiplayer.username = Multiplayer.settings.username;

            if (Multiplayer.username == null)
            {
                Multiplayer.username = SteamManager.Initialized ?
                    SteamUtility.SteamPersonaName : NameGenerator.GenerateName(RulePackDefOf.NamerTraderGeneral);

                Multiplayer.username = new Regex("[^a-zA-Z0-9_]").Replace(Multiplayer.username, string.Empty);
                Multiplayer.username = Multiplayer.username.TrimmedToLength(MultiplayerServer.MaxUsernameLength);
                Multiplayer.settings.username = Multiplayer.username;
                Multiplayer.settings.Write();
            }

            if (GenCommandLine.TryGetCommandLineArg("username", out string username))
                Multiplayer.username = username;
            else if (Multiplayer.username == null || Multiplayer.username.Length < 3 || MpVersion.IsDebug)
                Multiplayer.username = "Player" + Rand.Range(0, 9999);
        }

        private static void HandleRestartConnect()
        {
            if (Multiplayer.restartConnect == null)
                return;

            // No colon means the connect string is a steam user id
            if (!Multiplayer.restartConnect.Contains(':'))
            {
                if (ulong.TryParse(Multiplayer.restartConnect, out ulong steamUser))
                    DoubleLongEvent(() => ClientUtil.TrySteamConnectWithWindow((CSteamID)steamUser, false), "MpConnecting");

                return;
            }

            var split = Multiplayer.restartConnect.Split(new[]{':'}, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length == 2 && int.TryParse(split[1], out int port))
                DoubleLongEvent(() => ClientUtil.TryConnectWithWindow(split[0], port, false), "MpConnecting");
        }

        private static void HandleCommandLine()
        {
            if (GenCommandLine.TryGetCommandLineArg("connect", out string addressPort) && Multiplayer.restartConnect == null)
            {
                int port = MultiplayerServer.DefaultPort;

                string address = null;
                var split = addressPort.Split(':');

                if (split.Length == 0)
                    address = "127.0.0.1";
                else if (split.Length >= 1)
                    address = split[0];

                if (split.Length == 2)
                    int.TryParse(split[1], out port);

                DoubleLongEvent(() => ClientUtil.TryConnectWithWindow(address, port, false), "Connecting");
            }

            if (GenCommandLine.CommandLineArgPassed("arbiter"))
            {
                Multiplayer.username = "The Arbiter";
                Prefs.VolumeGame = 0;
            }

            if (GenCommandLine.TryGetCommandLineArg("replay", out string replay))
            {
                GenCommandLine.TryGetCommandLineArg("replaydata", out string replayData);
                var replays = replay.Split(';').ToList().GetEnumerator();

                DoubleLongEvent(() =>
                {
                    void LoadNextReplay()
                    {
                        if (!replays.MoveNext())
                        {
                            Application.Quit();
                            return;
                        }

                        var current = replays.Current!.Split(':');
                        int totalTicks = int.Parse(current[2]);
                        int batchSize = int.Parse(current[3]);
                        int ticksDone = 0;
                        double timeSpent = 0;

                        Replay.LoadReplay(Replay.SavedReplayFile(current[1]), true, () =>
                        {
                            TickPatch.AllTickables.Do(t => t.SetDesiredTimeSpeed(TimeSpeed.Normal));

                            void TickBatch()
                            {
                                if (ticksDone >= totalTicks)
                                {
                                    if (!replayData.NullOrEmpty())
                                    {
                                        string output = "";
                                        void Log(string text) => output += text + "\n";

                                        Log($"Ticks done: {ticksDone}");
                                        Log($"TPS: {1000.0/(timeSpent / ticksDone)}");
                                        Log($"Timer: {TickPatch.Timer}");
                                        Log($"World: {Multiplayer.AsyncWorldTime.worldTicks}/{Multiplayer.AsyncWorldTime.randState}");
                                        foreach (var map in Find.Maps)
                                            Log($"Map {map.uniqueID} rand: {map.AsyncTime().mapTicks}/{map.AsyncTime().randState}");

                                        File.WriteAllText(Path.Combine(replayData, $"{current[0]}"), output);
                                    }

                                    LoadNextReplay();
                                    return;
                                }

                                OnMainThread.Enqueue(() =>
                                {
                                    var watch = Stopwatch.StartNew();
                                    TickPatch.DoTicks(batchSize);
                                    timeSpent += watch.Elapsed.TotalMilliseconds;
                                    ticksDone += batchSize;
                                    TickBatch();
                                });
                            }

                            TickBatch();
                        });
                    }

                    LoadNextReplay();
                }, "Replay");
            }

            if (GenCommandLine.CommandLineArgPassed("printsync"))
            {
                ExtendDirectXmlSaver.extend = true;
                DirectXmlSaver.SaveDataObject(new SyncContainer(), "SyncHandlers.xml");
                ExtendDirectXmlSaver.extend = false;
            }
        }

        public class SyncContainer
        {
            public List<SyncHandler> handlers = Sync.handlers;
        }

        private static void DoPatches()
        {
            bool categoryNeedsAnnouncement = true;
            string category = null;

            void SetCategory(string str)
            {
                categoryNeedsAnnouncement = true;
                category = str;
            }

            void LogError(string str)
            {
                if (categoryNeedsAnnouncement) {
                    Log.Message($"Multiplayer :: {category}");
                }
                Log.Error(str);
                Multiplayer.loadingErrors = true;
            }

            var harmony = Multiplayer.harmony;

            void TryPatch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null,
                HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
            {
                try
                {
                    harmony.PatchMeasure(original, prefix, postfix, transpiler, finalizer);
                } catch (Exception e) {
                    LogError($"FAIL: {original.DeclaringType.FullName}:{original.Name} with {e}");
                }
            }

            SetCategory("Annotated patches");

            Assembly.GetCallingAssembly().GetTypes().Do(type => {
                // EarlyPatches are handled in MultiplayerMod.EarlyPatches
                if (type.IsDefined(typeof(EarlyPatchAttribute))) return;

                try {
                    harmony.CreateClassProcessor(type).Patch();
                } catch (Exception e) {
                    LogError($"FAIL: {type} with {e}");
                }
            });

            SetCategory("General designation patches");

            // General designation handling
            {
                var designatorFinalizer = AccessTools.Method(typeof(DesignatorPatches), nameof(DesignatorPatches.DesignateFinalizer));
                var designatorMethods = new[] {
                     ("DesignateSingleCell", new[]{ typeof(IntVec3) }),
                     ("DesignateMultiCell", new[]{ typeof(IEnumerable<IntVec3>) }),
                     ("DesignateThing", new[]{ typeof(Thing) }),
                };

                foreach (Type t in typeof(Designator).AllSubtypesAndSelf()
                             .Except(typeof(Designator_MechControlGroup))) // Opens float menu, sync that instead
                {
                    foreach ((string m, Type[] args) in designatorMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, args, null);
                        if (method == null) continue;

                        MethodInfo prefix = AccessTools.Method(typeof(DesignatorPatches), m);
                        TryPatch(method, new HarmonyMethod(prefix) { priority = MpPriority.MpFirst }, null, null, new HarmonyMethod(designatorFinalizer));
                    }
                }
            }

            SetCategory("Non-deterministic patches 1");

            // Remove side effects from methods which are non-deterministic during ticking (e.g. camera dependent motes and sound effects)
            {
                var randPatchPrefix = new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Prefix));
                var randPatchFinalizer = new HarmonyMethod(typeof(RandPatches), nameof(RandPatches.Finalizer));

                var subSustainerStart = MpMethodUtil.GetLambda(typeof(SubSustainer), parentMethodType: MethodType.Constructor, parentArgs: new[] { typeof(Sustainer), typeof(SubSoundDef) });
                var sampleCtor = typeof(Sample).GetConstructor(new[] { typeof(SubSoundDef) });
                var subSoundPlay = typeof(SubSoundDef).GetMethod(nameof(SubSoundDef.TryPlay));
                var effecterTick = typeof(Effecter).GetMethod(nameof(Effecter.EffectTick));
                var effecterTrigger = typeof(Effecter).GetMethod(nameof(Effecter.Trigger));
                var effecterCleanup = typeof(Effecter).GetMethod(nameof(Effecter.Cleanup));
                var randomBoltMesh = typeof(LightningBoltMeshPool).GetProperty(nameof(LightningBoltMeshPool.RandomBoltMesh))!.GetGetMethod();
                var drawTrackerCtor = typeof(Pawn_DrawTracker).GetConstructor(new[] { typeof(Pawn) });
                var randomHair = typeof(PawnStyleItemChooser).GetMethod(nameof(PawnStyleItemChooser.RandomHairFor));
                // todo for 1.5
                // var cannotAssignReason = typeof(Dialog_BeginRitual).GetMethod(nameof(Dialog_BeginRitual.CannotAssignReason), BindingFlags.NonPublic | BindingFlags.Instance);
                var canEverSpectate = typeof(RitualRoleAssignments).GetMethod(nameof(RitualRoleAssignments.CanEverSpectate));

                var effectMethods = new MethodBase[] { subSustainerStart, sampleCtor, subSoundPlay, effecterTick, effecterTrigger, effecterCleanup, randomBoltMesh, drawTrackerCtor, randomHair };
                var moteMethods = typeof(MoteMaker).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Concat(typeof(CompCableConnection.Cable).GetMethod(nameof(CompCableConnection.Cable.Tick)));
                var fleckMethods = typeof(FleckMaker).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Where(m => m.ReturnType == typeof(void))
                    .Concat(typeof(FleckManager).GetMethods() // FleckStatic uses Rand in Setup method, FleckThrown uses RandomInRange in TimeInterval. May as well catch all in case mods do the same.
                        .Where(m => m.ReturnType == typeof(void)));
                var ritualMethods = new[] { canEverSpectate };

                foreach (MethodBase m in effectMethods.Concat(moteMethods).Concat(fleckMethods).Concat(ritualMethods))
                    TryPatch(m, randPatchPrefix, finalizer: randPatchFinalizer);
            }

            SetCategory("Non-deterministic patches 2");

            // Set ThingContext and FactionContext (for pawns and buildings) in common Thing methods
            {
                var thingMethodPrefix = new HarmonyMethod(typeof(ThingMethodPatches).GetMethod(nameof(ThingMethodPatches.Prefix)));
                var thingMethodFinalizer = new HarmonyMethod(typeof(ThingMethodPatches).GetMethod(nameof(ThingMethodPatches.Finalizer)));
                var thingMethodPrefixSpawnSetup = new HarmonyMethod(typeof(ThingMethodPatches).GetMethod(nameof(ThingMethodPatches.Prefix_SpawnSetup)));

                var thingMethods = new[]
                {
                    ("Tick", Type.EmptyTypes),
                    ("TickRare", Type.EmptyTypes),
                    ("TickLong", Type.EmptyTypes),
                    ("TakeDamage", new[]{ typeof(DamageInfo) }),
                    ("Kill", new[]{ typeof(DamageInfo?), typeof(Hediff) })
                };

                foreach (Type t in typeof(Thing).AllSubtypesAndSelf())
                {
                    // SpawnSetup is patched separately because it sets the map
                    var spawnSetupMethod = t.GetMethod("SpawnSetup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(Map), typeof(bool) }, null);
                    if (spawnSetupMethod != null)
                        TryPatch(spawnSetupMethod, thingMethodPrefixSpawnSetup, finalizer: thingMethodFinalizer);

                    foreach ((string m, Type[] args) in thingMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, args, null);
                        if (method != null)
                            TryPatch(method, thingMethodPrefix, finalizer: thingMethodFinalizer);
                    }
                }
            }

            // Set FactionContext in common WorldObject methods
            {
                var prefix = new HarmonyMethod(typeof(WorldObjectMethodPatches).GetMethod(nameof(WorldObjectMethodPatches.Prefix)));
                var finalizer = new HarmonyMethod(typeof(WorldObjectMethodPatches).GetMethod(nameof(WorldObjectMethodPatches.Finalizer)));

                var thingMethods = new[]
                {
                    ("SpawnSetup", Type.EmptyTypes),
                    ("Tick", Type.EmptyTypes)
                };

                foreach (Type t in typeof(WorldObject).AllSubtypesAndSelf())
                {
                    foreach ((string m, Type[] args) in thingMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, args, null);
                        if (method != null)
                            TryPatch(method, prefix, finalizer: finalizer);
                    }
                }
            }

            // Full precision floating point saving
            {
                var doubleSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.DoubleSave_Prefix)));
                var floatSavePrefix = new HarmonyMethod(typeof(ValueSavePatch).GetMethod(nameof(ValueSavePatch.FloatSave_Prefix)));
                var valueSaveMethod = typeof(Scribe_Values).GetMethod(nameof(Scribe_Values.Look));

                TryPatch(valueSaveMethod.MakeGenericMethod(typeof(double)), doubleSavePrefix);
                TryPatch(valueSaveMethod.MakeGenericMethod(typeof(float)), floatSavePrefix);
            }

            SetCategory("Map time gui patches");

            // Set the map time for GUI methods depending on it
            {
                var setMapTimePrefix = new HarmonyMethod(AccessTools.Method(typeof(SetMapTimeForUI), nameof(SetMapTimeForUI.Prefix)));
                var setMapTimeFinalizer = new HarmonyMethod(AccessTools.Method(typeof(SetMapTimeForUI), nameof(SetMapTimeForUI.Finalizer)));

                var windowMethods = new[] { "DoWindowContents", "WindowUpdate" };
                foreach (string m in windowMethods)
                    TryPatch(typeof(MainTabWindow_Inspect).GetMethod(m), setMapTimePrefix, finalizer: setMapTimeFinalizer);

                foreach (var t in typeof(InspectTabBase).AllSubtypesAndSelf())
                {
                    var method = t.GetMethod("FillTab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (method != null && !method.IsAbstract)
                        TryPatch(method, setMapTimePrefix, finalizer: setMapTimeFinalizer);
                }
            }
        }
    }

}


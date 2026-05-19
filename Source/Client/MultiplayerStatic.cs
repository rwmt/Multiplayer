using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Multiplayer.Client.Networking;
using Multiplayer.Client.Patches;
using Multiplayer.Client.Util;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public static class MultiplayerStatic
    {
        public static KeyBindingDef ToggleChatDef = KeyBindingDef.Named("MpToggleChat");
        public static KeyBindingDef PingKeyDef = KeyBindingDef.Named("MpPingKey");
        // No default bind - user assigns via Keyboard Config.
        public static KeyBindingDef TogglePingMenuDef = KeyBindingDef.Named("MpTogglePingMenu");

        public static readonly Texture2D PingBase = ContentFinder<Texture2D>.Get("Multiplayer/PingBase");
        public static readonly Texture2D PingPin = ContentFinder<Texture2D>.Get("Multiplayer/PingPin");

        // Procedural, antialiased, white - tint at draw time.
        public static readonly Texture2D PingCircle = MakeCircleTex(256, outerRadius: 127.5f, innerRadius: 0f);
        public static readonly Texture2D PingRing   = MakeCircleTex(256, outerRadius: 127.5f, innerRadius: 108f);

        // Pre-rotated wheel sector textures live next to LocationPings.WheelOptions so the slot
        // count can't desync - see LocationPings.PingSectors / PingSectorArcs.
        public static readonly Texture2D PingChevronUp = MakeChevronUpTex(64);

        // reportFailure=false so a missing path returns null and the renderer falls back to Glyph().
        public static readonly Texture2D PingIconAttack = ContentFinder<Texture2D>.Get("UI/Commands/AttackMelee", false);
        public static readonly Texture2D PingIconDefend = ContentFinder<Texture2D>.Get("UI/Designators/HomeAreaOn", false);
        public static readonly Texture2D PingIconHelp   = ContentFinder<Texture2D>.Get("UI/Commands/AsMedical", false);
        public static readonly Texture2D PingIconLoot   = ContentFinder<Texture2D>.Get("UI/Buttons/TradeMode", false);
        public static readonly Texture2D PingIconRally  = ContentFinder<Texture2D>.Get("UI/Commands/GatherSpotActive", false);

        // Gizmo action icons reuse vanilla UI/ atlases (visibility toggles, reset arrows).
        public static readonly Texture2D PingHideForMeIcon   = ContentFinder<Texture2D>.Get("UI/Designators/PlanHide");
        public static readonly Texture2D PingShowForMeIcon   = ContentFinder<Texture2D>.Get("UI/Designators/PlanOn");
        public static readonly Texture2D PingResetViewIcon   = ContentFinder<Texture2D>.Get("UI/Commands/TempReset");
        // Procedural - half-faded disc for the transparency gizmo.
        public static readonly Texture2D PingTransparencyIcon = MakeFadeDiscTex(128);
        // Procedural - selection corners with central X for the deselect gizmo.
        public static readonly Texture2D PingDeselectIcon = MakeDeselectTex(128);
        // Procedural - speaker + knockout slash. Shared by all mute actions; the label carries the distinction.
        public static readonly Texture2D PingMuteIcon = MakeMuteTex(128);

        private static Texture2D MakeCircleTex(int size, float outerRadius, float innerRadius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha;
                    if (innerRadius <= 0f)
                    {
                        alpha = Mathf.Clamp01(outerRadius - d + 0.5f);
                    }
                    else
                    {
                        var inA  = Mathf.Clamp01(d - innerRadius + 0.5f);
                        var outA = Mathf.Clamp01(outerRadius - d + 0.5f);
                        alpha = Mathf.Min(inA, outA);
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        // Annular sector with axis at centerAngleDeg clockwise from screen-up, half-width halfAngleDeg.
        // Convention: high py = top of rect on screen, so +dy is "screen up" here.
        internal static Texture2D MakeSectorTex(int size, float outerRadius, float innerRadius, float halfAngleDeg, float centerAngleDeg)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;
            var halfA = halfAngleDeg * Mathf.Deg2Rad;
            var centerA = centerAngleDeg * Mathf.Deg2Rad;

            // Outward normals of the right/left radial boundary lines (+x right, +y up).
            var cosR = Mathf.Cos(centerA + halfA);
            var sinR = Mathf.Sin(centerA + halfA);
            var cosL = Mathf.Cos(centerA - halfA);
            var sinL = Mathf.Sin(centerA - halfA);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    var dx = px - center;
                    var dy = py - center;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);

                    var inA  = Mathf.Clamp01(d - innerRadius + 0.5f);
                    var outA = Mathf.Clamp01(outerRadius - d + 0.5f);
                    var radialAlpha = Mathf.Min(inA, outA);

                    var dRight = dx * cosR - dy * sinR;
                    var dLeft  = -dx * cosL + dy * sinL;
                    var angularAlpha = Mathf.Clamp01(0.5f - Mathf.Max(dRight, dLeft));

                    var alpha = radialAlpha * angularAlpha;
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        // Distance-to-line field with AA band so the texture scales cleanly without re-baking.
        // Apex (V's point) at HIGH py, arm ends at LOW py - matches MakeSectorTex convention.
        private static Texture2D MakeChevronUpTex(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;
            var strokeHalf = size * 0.10f;
            var apexY = size * 0.78f;
            var armEndY = size * 0.22f;
            var armEndDx = size * 0.36f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    var dx = px - center;
                    var dy = py;

                    var distR = DistToSegment(dx, dy, 0f, apexY, armEndDx, armEndY);
                    var distL = DistToSegment(dx, dy, 0f, apexY, -armEndDx, armEndY);
                    var d = Mathf.Min(distR, distL);
                    var alpha = Mathf.Clamp01(strokeHalf - d + 0.5f);

                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        // Disc with horizontal alpha gradient - opaque on the left half, fading to ~15% on the right.
        private static Texture2D MakeFadeDiscTex(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;
            var outerRadius = size * 0.46f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var circleAlpha = Mathf.Clamp01(outerRadius - d + 0.5f);

                    // Left edge (x=0) opaque, right edge (x=size-1) at minAlpha.
                    var t = (float)x / (size - 1);
                    var horizontalAlpha = Mathf.Lerp(1f, 0.18f, t);

                    var alpha = circleAlpha * horizontalAlpha;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        // Selection-corner brackets at the four corners + a central X.
        private static Texture2D MakeDeselectTex(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;

            var inset = size * 0.14f;
            var armLen = size * 0.22f;
            var bracketStroke = size * 0.085f;
            var xHalf = size * 0.16f;
            var xStroke = size * 0.085f;

            float far = (size - 1) - inset;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    // 8 L-arm segments - horizontal + vertical at each of the 4 corners.
                    var d = float.MaxValue;
                    d = Mathf.Min(d, DistToSegment(px, py, inset, inset, inset + armLen, inset));
                    d = Mathf.Min(d, DistToSegment(px, py, inset, inset, inset, inset + armLen));
                    d = Mathf.Min(d, DistToSegment(px, py, far, inset, far - armLen, inset));
                    d = Mathf.Min(d, DistToSegment(px, py, far, inset, far, inset + armLen));
                    d = Mathf.Min(d, DistToSegment(px, py, inset, far, inset + armLen, far));
                    d = Mathf.Min(d, DistToSegment(px, py, inset, far, inset, far - armLen));
                    d = Mathf.Min(d, DistToSegment(px, py, far, far, far - armLen, far));
                    d = Mathf.Min(d, DistToSegment(px, py, far, far, far, far - armLen));
                    var bracketAlpha = Mathf.Clamp01(bracketStroke - d + 0.5f);

                    var dXa = DistToSegment(px, py, center - xHalf, center - xHalf, center + xHalf, center + xHalf);
                    var dXb = DistToSegment(px, py, center - xHalf, center + xHalf, center + xHalf, center - xHalf);
                    var xAlpha = Mathf.Clamp01(xStroke - Mathf.Min(dXa, dXb) + 0.5f);

                    var alpha = Mathf.Max(bracketAlpha, xAlpha);
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        // Speaker (rectangular stand + trapezoidal horn) + two sound arcs + diagonal slash.
        // Slash is drawn with a knockout band so it reads against the speaker body (which is
        // also white) at gizmo scale.
        private static Texture2D MakeMuteTex(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;

            // Speaker geometry.
            var standLeft   = size * 0.18f;
            var standRight  = size * 0.34f;
            var standTop    = size * 0.42f;
            var standBottom = size * 0.58f;

            var hornNarrowX = standRight;
            var hornWideX   = size * 0.56f;
            var hornNarrowHalfH = (standBottom - standTop) / 2f;
            var hornWideHalfH   = size * 0.22f;

            // Sound arcs.
            var arcCenterX = size * 0.56f;
            var arcCenterY = center;
            var arcR1 = size * 0.11f;
            var arcR2 = size * 0.21f;
            var arcStroke = size * 0.055f;

            // Slash: from upper-right to lower-left. Knockout band carves the speaker so the
            // slash itself reads as a dark gap with a thin white line through it.
            var slashAx = size * 0.93f;
            var slashAy = size * 0.07f;
            var slashBx = size * 0.07f;
            var slashBy = size * 0.93f;
            var slashGap  = size * 0.075f;
            var slashLine = size * 0.035f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float a = 0f;

                    if (px >= standLeft && px <= standRight && py >= standTop && py <= standBottom)
                        a = 1f;

                    if (px >= hornNarrowX && px <= hornWideX)
                    {
                        var t = (px - hornNarrowX) / Mathf.Max(0.0001f, hornWideX - hornNarrowX);
                        var halfH = Mathf.Lerp(hornNarrowHalfH, hornWideHalfH, t);
                        if (Mathf.Abs(py - center) <= halfH) a = 1f;
                    }

                    var rdx = px - arcCenterX;
                    var rdy = py - arcCenterY;
                    if (rdx > 0f)
                    {
                        var rd = Mathf.Sqrt(rdx * rdx + rdy * rdy);
                        var wedge = Mathf.Abs(rdy) <= rdx ? 1f : 0f;
                        var arc1 = Mathf.Clamp01(arcStroke - Mathf.Abs(rd - arcR1) + 0.5f);
                        var arc2 = Mathf.Clamp01(arcStroke - Mathf.Abs(rd - arcR2) + 0.5f);
                        a = Mathf.Max(a, Mathf.Max(arc1, arc2) * wedge);
                    }

                    var slashD = DistToSegment(px, py, slashAx, slashAy, slashBx, slashBy);
                    if (slashD < slashGap) a = 0f;
                    var slashAlpha = Mathf.Clamp01(slashLine - slashD + 0.5f);
                    a = Mathf.Max(a, slashAlpha);

                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }

        private static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var len2 = dx * dx + dy * dy;
            if (len2 < 1e-6f) return Mathf.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            var t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / len2);
            var qx = ax + t * dx;
            var qy = ay + t * dy;
            return Mathf.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }

        public static readonly Texture2D WebsiteIcon = ContentFinder<Texture2D>.Get("Multiplayer/Website");
        public static readonly Texture2D DiscordIcon = ContentFinder<Texture2D>.Get("Multiplayer/Discord");
        public static readonly Texture2D Pulse = ContentFinder<Texture2D>.Get("Multiplayer/Pulse");

        public static readonly Texture2D ChangeRelationIcon = ContentFinder<Texture2D>.Get("UI/Icons/VisitorsHelp");

        public static readonly Texture2D OptionsGeneral = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");
        public static readonly Texture2D OptionsGameplay = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGameplay");

        public static readonly Texture2D GiftModeIcon = ContentFinder<Texture2D>.Get("UI/Buttons/GiftMode");
        public static readonly Texture2D TradeModeIcon = ContentFinder<Texture2D>.Get("UI/Buttons/TradeMode");

        public const string MpHostReplayCmdLineArgName = "mphostreplay";
        public static string MpHostReplayCmdLineArgValue;

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
            LiteNetLogger.Install();

            SetUsername();

            if (SteamManager.Initialized)
            {
                SteamIntegration.InitCallbacks();
                SteamP2PIntegration.InitCallbacks();
            }

            Log.Message($"Multiplayer version {MpVersion.Version}");
            Log.Message($"Arch: {RuntimeInformation.ProcessArchitecture}/OS: {RuntimeInformation.OSArchitecture}");
            Log.Message($"Player's username: {Multiplayer.username}");

            var persistentObj = new GameObject();
            persistentObj.AddComponent<OnMainThread>();
            Object.DontDestroyOnLoad(persistentObj);

            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientSteam, typeof(ClientSteamState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientJoining, typeof(ClientJoiningState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientLoading, typeof(ClientLoadingState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientPlaying, typeof(ClientPlayingState));
            MpConnectionState.SetImplementation(ConnectionStateEnum.ClientBootstrap, typeof(ClientBootstrapState));

            MultiplayerData.CollectCursorIcons();

            PersistentDialog.BindAll(typeof(Multiplayer).Assembly);

            using (DeepProfilerWrapper.Section("Multiplayer MpPatches"))
                Multiplayer.harmony.DoAllMpPatches();

            using (DeepProfilerWrapper.Section("Multiplayer patches"))
                DoPatches();

            Log.messageQueue.maxMessages = 1000;

            ClientUtil.DoubleLongEvent(() =>
            {
                MultiplayerData.CollectDefInfos();
                Sync.PostInitHandlers();
            }, "Loading"); // Right before the events from HandleCommandLine

            AutoJoinHandler.JoinIfApplicable();
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

        private static void HandleCommandLine()
        {
            if (GenCommandLine.CommandLineArgPassed("arbiter"))
            {
                Multiplayer.username = "The Arbiter";
                Prefs.VolumeGame = 0;
            }

            if (GenCommandLine.TryGetCommandLineArg("replay", out string replay))
            {
                GenCommandLine.TryGetCommandLineArg("replaydata", out string replayData);
                var replays = replay.Split(';').ToList().GetEnumerator();

                ClientUtil.DoubleLongEvent(() =>
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
                            TickPatch.AllTickables.Do(t => t.DesiredTimeSpeed = TimeSpeed.Normal);

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

            if (GenCommandLine.TryGetCommandLineArg(MpHostReplayCmdLineArgName, out var path))
            {
                MpHostReplayCmdLineArgValue = path;
                ClientUtil.DoubleLongEvent(() => HostWindow.VerifyAndOpen(path), "Loading");
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

                var harmonyAttributes = HarmonyMethodExtensions.GetFromType(type);
			    if (harmonyAttributes is null || harmonyAttributes.Count == 0) return;

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    LogError($"FAIL: {type} with {e}");
                }
            });

            SetCategory("General designation patches");

            // General designation handling
            {
                var designatorFinalizer = AccessTools.Method(typeof(DesignatorPatches), nameof(DesignatorPatches.DesignateFinalizer));
                var designatorMethods = new[] {
                     (nameof(DesignatorPatches.DesignateSingleCell), new[]{ typeof(IntVec3) }),
                     (nameof(DesignatorPatches.DesignateMultiCell), new[]{ typeof(IEnumerable<IntVec3>) }),
                     (nameof(DesignatorPatches.DesignateThing), new[]{ typeof(Thing) }),
                };

                foreach (Type t in typeof(Designator).AllSubtypesAndSelf()
                    // Designator_MechControlGroup Opens float menu, sync that instead
                    // Designator_Plan_CopySelection creates the placement gizmo, this shouldn't be synced
                    .Except([typeof(Designator_MechControlGroup), typeof(Designator_Plan_CopySelection)]))
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
                        .Where(m => m.ReturnType == typeof(void)))
                    .Concat(typeof(LavaFXComponent).DeclaredMethod(nameof(LavaFXComponent.ThrowLavaSmoke)))
                    .Concat(typeof(FishShadowComponent).DeclaredMethod(nameof(FishShadowComponent.SpawnFishFleck)))
                    .Concat(typeof(CompFleckEmitterLongTerm).DeclaredMethod(nameof(CompFleckEmitterLongTerm.EmissionTick)));
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
                    ("TickInterval", [typeof(int)]),
                    ("TakeDamage", [typeof(DamageInfo)]),
                    ("Kill", [typeof(DamageInfo?), typeof(Hediff)])
                };

                foreach (Type t in typeof(Thing).AllSubtypesAndSelf())
                {
                    // SpawnSetup is patched separately because it sets the map
                    var spawnSetupMethod = t.GetMethod("SpawnSetup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(Map), typeof(bool) }, null);
                    if (spawnSetupMethod != null)
                        TryPatch(spawnSetupMethod, thingMethodPrefixSpawnSetup, finalizer: thingMethodFinalizer);

                    foreach ((string m, Type[] args) in thingMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.NonPublic, null, args, null);
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
                    ("Tick", Type.EmptyTypes),
                    ("TickInterval", [typeof(int)]),
                };

                foreach (Type t in typeof(WorldObject).AllSubtypesAndSelf())
                {
                    foreach ((string m, Type[] args) in thingMethods)
                    {
                        MethodInfo method = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.NonPublic, null, args, null);
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


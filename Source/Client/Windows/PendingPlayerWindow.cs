#nullable enable
using System;
using System.Collections.Concurrent;
using LudeonTK;
using Multiplayer.Client.Util;
using Steamworks;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Windows;

public class PendingPlayerWindow : Window
{
    private static PendingPlayerWindow? Opened => Find.WindowStack?.WindowOfType<PendingPlayerWindow>();

    public static void EnqueueJoinRequest(string name, Action<Request, bool>? callback = null) => Request.Enqueue(name, callback);
    public static void EnqueueJoinRequest(CSteamID steamId, Action<Request, bool>? callback = null) => Request.Enqueue(steamId, callback);

    public record Request
    {
        public readonly CSteamID? steamId;
        public readonly string name;
        private readonly Action<Request, bool>? callback;

        private Request(CSteamID? steamId, string name, Action<Request, bool>? callback)
        {
            this.steamId = steamId;
            this.name = name;
            this.callback = callback;
        }

        internal void RunCallback(bool accepted)
        {
            try
            {
                callback?.Invoke(this, accepted);
            }
            catch (Exception e)
            {
                MpLog.Warn($"Exception invoking join request callback for {steamId}:{name}: {e}");
            }
        }

        private void Enqueue()
        {
            var window = Opened;
            if (window != null)
            {
                window.queue.Enqueue(this);
                return;
            }

            window = new PendingPlayerWindow();
            window.queue.Enqueue(this);
            Find.WindowStack.Add(window);
        }

        public static void Enqueue(string name, Action<Request, bool>? callback = null) =>
            new Request(null, name, callback).Enqueue();

        public static void Enqueue(CSteamID steamId, Action<Request, bool>? callback = null) =>
            new Request(steamId, SteamFriends.GetFriendPersonaName(steamId), callback).Enqueue();
    }

    private const float BtnMargin = 8f;
    private const float BtnWidth = 65f;
    private const float AnimTimeSecs = .7f;
    private float startTime;

    private readonly ConcurrentQueue<Request> queue = [];
    private Request? req;

    private PendingPlayerWindow()
    {
        preventCameraMotion = false;
        focusWhenOpened = false;
        closeOnClickedOutside = false;
        closeOnCancel = false;
        closeOnAccept = false;
        layer = WindowLayer.GameUI;
    }

    public override Vector2 InitialSize => new(200f, req?.steamId.HasValue == true ? 320f : 245f);
    public override float Margin => 4f;

    public override void PreOpen()
    {
        startTime = Time.time;
        if (req == null) ShowNextRequestOrClose();
        base.PreOpen();
    }

    public override void SetInitialSizeAndPosition()
    {
        // Add scaled 1f to hide the right border of the window
        windowRect = new Rect(UI.screenWidth - InitialSize.y + UIScaling.AdjustCoordToUIScalingCeil(1f),
            InitialSize.x, InitialSize.y, 96f);
    }

    private void UpdateWindowRect()
    {
        var arrivedAgo = Time.time - startTime;
        var animFinished = arrivedAgo > AnimTimeSecs;
        if (!animFinished)
        {
            var timeProgress = arrivedAgo / AnimTimeSecs;
            var posProgress = 1 - Math.Pow(1 - timeProgress, 2.5);
            windowRect.x = UI.screenWidth - (float)posProgress * InitialSize.y +
                           UIScaling.AdjustCoordToUIScalingCeil(1f);
        } else SetInitialSizeAndPosition();
    }

    public override void WindowOnGUI()
    {
        UpdateWindowRect();
        windowRect = GUI.Window(ID, windowRect, innerWindowOnGUICached, "", windowDrawing.EmptyStyle);
    }

    public override void DoWindowContents(Rect inRect)
    {
        // This should not happen
        if (req == null) return;
        if (req.steamId.HasValue)
        {
            // This can return 0 (if the user has no avatar) or -1 (if we are waiting for the avatar to download).
            // In that case, SteamImages will just return null and there will be no avatar displayed.
            // Once the avatar is downloaded, it will just show up. For the download to start, you must
            // RequestUserInformation. The only place that uses this window is SteamIntegration.P2PSessionRequest,
            // which does request the information, so we are fine.
            var avatarId = SteamFriends.GetLargeFriendAvatar(req.steamId.Value);
            var avatarTex = SteamImages.GetTexture(avatarId);
            var avatarRect = new Rect(0, 0, 80, 80).CenteredOnYIn(inRect).Right(4);
            InvisibleOpenSteamProfileButton(avatarRect, req.steamId, doMouseoverSound: false);
            if (avatarTex != null)
                GUI.DrawTextureWithTexCoords(avatarRect, avatarTex, new Rect(0, 1, 1, -1));
            inRect.xMin = avatarRect.xMax + 6f;
        }
        else
        {
            inRect.xMin += 15f;
        }

        using (MpStyle.Set(TextAnchor.UpperLeft).Set(WordWrap.NoWrap).Set(GameFont.Medium))
        {
            var textRect = inRect;

            string usernameClamped = Text.ClampTextWithEllipsis(textRect, req.name);
            var nameTextRect = textRect.FitToText(usernameClamped);

            var showTooltip = usernameClamped.Length != req.name.Length;
            if (req.steamId.HasValue || showTooltip)
                Widgets.DrawHighlightIfMouseover(nameTextRect.ExpandedBy(3f, 0f));
            if (showTooltip) TooltipHandler.TipRegion(nameTextRect, new TipSignal(req.name));

            Widgets.Label(nameTextRect, usernameClamped);
            InvisibleOpenSteamProfileButton(nameTextRect, req.steamId);
            inRect = inRect.MarginTop(nameTextRect.height + Text.SpaceBetweenLines);
        }

        using (MpStyle.Set(TextAnchor.UpperLeft).Set(WordWrap.NoWrap).Set(GameFont.Small))
            Widgets.Label(inRect, "MpJoinRequestDesc".Translate());

        var btnGroupRect = inRect.MarginTop(Text.LineHeightOf(GameFont.Small) + BtnMargin).MarginBottom(4f).MarginRight(6f);

        var btnOkRect = btnGroupRect.Width(BtnWidth);
        if (Widgets.ButtonText(btnOkRect, "Accept".Translate(), overrideTextAnchor: TextAnchor.MiddleCenter))
        {
            req.RunCallback(true);
            ShowNextRequestOrClose();
        }

        var btnNoRect = btnOkRect.Right(btnOkRect.width + BtnMargin);
        if (Widgets.ButtonText(btnNoRect, "RejectLetter".Translate(), overrideTextAnchor: TextAnchor.MiddleCenter)) {
            req.RunCallback(false);
            ShowNextRequestOrClose();
        }
    }

    // Try our best to make sure there is no possibility of missing a request
    public override bool OnCloseRequest() => queue.IsEmpty;

    private static void InvisibleOpenSteamProfileButton(Rect rect, CSteamID? steamId, bool doMouseoverSound = true)
    {
        if (steamId.HasValue && Widgets.ButtonInvisible(rect, doMouseoverSound))
            SteamFriends.ActivateGameOverlayToUser("steamid", steamId.Value);
    }

    private void ShowNextRequestOrClose()
    {
        if (queue.TryDequeue(out req))
        {
            // Window size differs if a Steam ID is present (extra space for avatar)
            UpdateWindowRect();
            return;
        }

        Close();
    }
}

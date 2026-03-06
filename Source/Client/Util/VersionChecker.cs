#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Multiplayer.Common;
using RestSharp;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Util;

public static class VersionChecker
{
    private static Release? latestContinuousRelease;

    public static void Init()
    {
        Task.Run(async void () =>
        {
            try
            {
                if (IsContinuousRelease)
                {
                    latestContinuousRelease = await GetLatestContinuousRelease();
                    if (MpVersion.IsDebug) return;
                    if (latestContinuousRelease is { IsInstalled: false } release)
                        Log.Warning($"Newer Multiplayer version is available at {release.html_url}");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Multiplayer] Failed to check for updates: {e}");
            }
        });
    }

    public static bool IsContinuousRelease =>
        Multiplayer.modContentPack.ModMetaData.Source == ContentSource.ModsFolder &&
        MpVersion.GitDescription?.StartsWith("continuous") == true &&
        MpVersion.GitHash?.EndsWith("dirty") == false;

    public static bool IsLocalBuild =>
        Multiplayer.modContentPack.ModMetaData.Source == ContentSource.ModsFolder &&
        MpVersion.GitHash?.EndsWith("dirty") == true;

    private static async Task<Release?> GetLatestContinuousRelease()
    {
        var client = new RestClient("https://api.github.com/")
            .AddDefaultHeader("Accept", "application/vnd.github+json")
            .AddDefaultHeader("X-GitHub-Api-Version", "2022-11-28");
        var req = new RestRequest("repos/rwmt/Multiplayer/releases?per_page=5");
        var resp = await client.GetAsync<List<Release>>(req);
        return resp.FirstOrDefault(release => release.tag_name == "continuous");
    }

    public static void OpenNewVersionDialogIfApplicable()
    {
        if (MpVersion.IsDebug) return;
        var release = latestContinuousRelease;
        if (!IsContinuousRelease || release is not { IsInstalled: false }) return;
        var dialog =
            new Dialog_MessageBox(
                "A new version of Multiplayer is available. Update now for the latest features, improvements and bug fixes.",
                "Open download page",
                () => { Application.OpenURL(release.html_url); },
                "Remind me later",
                () => { });
        Find.WindowStack.Add(dialog);
    }

    public class Release
    {
        // These are properties because RestSharp requires that for deserialization
        public string tag_name { get; set; } = string.Empty;
        public string target_commitish { get; set; } = string.Empty;
        public string html_url { get; set; } = string.Empty;

        public bool IsInstalled => MpVersion.GitHash != null && target_commitish.StartsWith(MpVersion.GitHash);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class JoinDataWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(640f, 480f);
        
        private enum Tab
        {
            General, ModList, Files, Configs
        }

        private Tab tab;

        class Node
        {
            public string DisplayName => $"{(children.Any() ? $"[{(collapsed ? '+' : '-')}] " : "")}{name}{(children.Any() ? "/" : "")}";

            public string name;
            public string id;
            public int depth;
            public Node parent;
            public List<Node> children = new List<Node>();
            public bool collapsed;
            public NodeStatus status;
            
            public int[] childrenPerStatus;
            public HashSet<string> paths;
        }

        enum NodeStatus{
            None, Missing, Added, Modified
        }

        private RemoteData remote;
        private Node filesRoot;
        private Node configsRoot;

        public JoinDataWindow(RemoteData remote)
        {
            this.remote = remote;

            RefreshFiles();
            RefreshConfigs();
        }

        private void AddNodesForPath(string path, Node root, NodeStatus status)
        {
            if (root.paths.Contains(path)) return;

            var arr = path.Split(new[]{'/'}, StringSplitOptions.RemoveEmptyEntries);
            var cur = root;

            foreach (var part in arr){
                var next = cur.children.FirstOrDefault(n => n.name == part);
                if (next == null){
                    next = new Node(){name=part, parent=cur, depth=cur.depth+1};
                    cur.children.Add(next);
                }
                cur = next;
            }

            cur.status = status;
            root.paths.Add(path);

            if (root.childrenPerStatus != null)
                root.childrenPerStatus[(int)status]++;
        }

        private void RefreshFiles()
        {
            filesRoot = new Node();

            Node ModNode(string modId){
                var mod = JoinData.GetInstalledMod(modId);
                if (mod == null) return null;

                var modNode = filesRoot.children.FirstOrDefault(n => n.id == modId);
                if (modNode == null)
                {
                    modNode = new Node() { name=mod.Name, id=modId, parent=filesRoot, paths=new HashSet<string>(), childrenPerStatus=new int[4], collapsed=true };
                    filesRoot.children.Add(modNode);
                }

                return modNode;
            }

            void AddFiles(ModFileDict one, ModFileDict two, NodeStatus notInTwo){
                foreach (var kv in one)
                {
                    var modNode = ModNode(kv.Key);
                    if (modNode == null) continue;

                    foreach (var f in kv.Value.Values){
                        var inTwo = two.GetOrDefault(kv.Key, f.relPath);
                        AddNodesForPath(
                            f.relPath, 
                            modNode, 
                            inTwo == null ? notInTwo : inTwo.Value.hash != f.hash ? NodeStatus.Modified : NodeStatus.None
                        );
                    }
                }
            }

            AddFiles(remote.remoteFiles, JoinData.ModFiles, NodeStatus.Missing);
            AddFiles(JoinData.ModFiles, remote.remoteFiles, NodeStatus.Added);
        }

        private void RefreshConfigs(){
            configsRoot = new Node() {paths=new HashSet<string>(),depth=-1};

            void AddConfigs(Dictionary<string, string> one, Dictionary<string, string> two, NodeStatus notInTwo){
                foreach (var kv in one){
                    AddNodesForPath(
                        kv.Key, 
                        configsRoot, 
                        two.TryGetValue(kv.Key, out var t) ? (t.Equals(kv.Value) ? NodeStatus.None : NodeStatus.Modified) : notInTwo
                    );
                }
            }

            var localConfigs = JoinData.ModConfigContents.ToDictionary(p => p.Item1, p => p.Item2);

            AddConfigs(remote.remoteModConfigs, localConfigs, NodeStatus.Missing);
            AddConfigs(localConfigs, remote.remoteModConfigs, NodeStatus.Added);
        }

        public override void DoWindowContents(Rect inRect)
        {
            inRect.yMin += 35f;

            var tabsRect = inRect.TopPartPixels(inRect.height - 50f);

            TabDrawer.DrawTabs(tabsRect, new List<TabRecord>() {
                new TabRecord("General", () => tab = Tab.General, tab == Tab.General),
                new TabRecord("Mod list", () => tab = Tab.ModList, tab == Tab.ModList),
                new TabRecord("Files", () => tab = Tab.Files, tab == Tab.Files),
                new TabRecord("Configs", () => tab = Tab.Configs, tab == Tab.Configs)
            });

            GUI.BeginGroup(tabsRect);

            var inTab = new Rect(0, 0, tabsRect.width, tabsRect.height);

            if (tab == Tab.Files)
                DrawTreeTab(inTab, "Local files", filesRoot, RefreshFiles, true, "Mod file differences might cause desyncs.\nMake sure the versions of your mods match the server.");
            else if (tab == Tab.Configs)
                DrawTreeTab(inTab, "Local configs", configsRoot, RefreshConfigs, false, "Configuration differences can in some cases be the cause of desyncs.");
            else if (tab == Tab.ModList)
                DrawModList(inTab);

            GUI.EndGroup();

            var btnCenter = new Rect(0, 0, 135f, 35f).CenterOn(inRect.BottomPartPixels(35f)).Up(10f);

            Widgets.ButtonText(btnCenter.Left(150f), "Connect anyway");
            Widgets.ButtonText(btnCenter, "Fix & restart");
            Widgets.ButtonText(btnCenter.Right(150f), "Close");
        }

        Vector2 scroll;
        int nodeCount;

        private void DrawTreeTab(Rect inRect, string label, Node root, Action refresh, bool showCounts, string desc)
        {
            var listRect = new Rect(0, 20f, 440f, 220f);
            var labelsRect = new Rect(0, 20f, 120f, 220f);

            var combined = new Rect(0, 15f, listRect.width + labelsRect.width + 20f, listRect.yMax).CenteredOnXIn(inRect);

            GUI.BeginGroup(combined);
            
            MpUtil.Label(new Rect(0, 0, 440f, 20f), label, GameFont.Tiny);

            var refreshBtn = new Rect(420, 0, 20, 20);
            TooltipHandler.TipRegion(refreshBtn, "Refresh");
            if (Widgets.ButtonImage(refreshBtn, TexUI.RotRightTex, true))
                refresh();

            GUI.color = new Color(1, 1, 1, 0.1f);
            Widgets.DrawBox(listRect);
            GUI.color = Color.white;

            const float modLabelHeight = 22f;

            var viewRect = new Rect(0, 0, listRect.width - scrollbarWidth, nodeCount * modLabelHeight);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);

            var toDraw = new Stack<Node>();

            foreach (var n in root.children)
                toDraw.Push(n);

            if (Event.current.type == EventType.Layout)
                nodeCount = 0;

            var scrollShown = viewRect.height > listRect.height;

            int i = 0;
            while (toDraw.Count > 0)
            {
                var n = toDraw.Pop();

                if (!n.collapsed)
                    foreach (var c in n.children)
                        toDraw.Push(c);

                var labelRect = new Rect(0, i * modLabelHeight, listRect.width, modLabelHeight);

                Widgets.DrawHighlightIfMouseover(labelRect);
                if (Widgets.ButtonInvisible(labelRect))
                    n.collapsed = !n.collapsed;

                MpUtil.Label(
                    labelRect.WithX(5 + 15f * n.depth),
                    n.DisplayName,
                    color: 
                        n.status == NodeStatus.Added ? Orange : 
                        n.status == NodeStatus.Missing ? Red : 
                        n.status == NodeStatus.Modified ? Yellow : 
                        Color.white
                );

                if (showCounts && n.childrenPerStatus != null)
                    MpUtil.Label(
                        labelRect.Left(scrollShown ? 21f : 5f), 
                        $"<color=#ff4444>{n.childrenPerStatus[1]}</color> <color=#ff8844>{n.childrenPerStatus[2]}</color> <color=#ffff44>{n.childrenPerStatus[3]}</color>",
                        anchor: TextAnchor.UpperRight
                    );
                
                i++;
            }

            if (Event.current.type == EventType.Layout)
                nodeCount = i;

            Widgets.EndScrollView();

            GUI.BeginGroup(labelsRect.Right(listRect.width + 20f));
            
            Text.CurFontStyle.richText = true;
            MpUtil.Label(
                labelsRect,
                "<color=#ff4444>(Missing)</color>\n<color=#ff8844>(Added)</color>\n<color=#ffff44>(Modified)</color>",
                anchor: TextAnchor.MiddleCenter
            );

            GUI.EndGroup();

            GUI.EndGroup();

            MpUtil.Label(
                new Rect(0, combined.yMax + 15f, 360f, 50f).CenteredOnXIn(inRect),
                desc,
                anchor: TextAnchor.MiddleCenter
            );
        }

        Vector2 modScroll1;
        Vector2 modScroll2;

        const float scrollbarWidth = 16f;

        static readonly Color Red = new Color(1f, 0.25f, 0.25f);
        static readonly Color Orange = new Color(1f, 0.5f, 0.25f);
        static readonly Color Yellow = new Color(1f, 1f, 0.25f);

        public void DrawModList(Rect inRect)
        {
            var notInstalled = remote.remoteMods.Where(m => !m.Installed).ToList();

            const float modLabelHeight = 21f;
            const float selectorWidth = 160f;
            const float selectorSpacing = 20f;
            const float selectorHeight = 300f;

            inRect.yMin += 15f;

            var mods1Rect = inRect.
                Right(inRect.center.x - (selectorWidth * 3 + selectorSpacing * 3) / 2f).
                LeftPartPixels(selectorWidth).
                TopPartPixels(selectorHeight + 50f);

            GUI.BeginGroup(mods1Rect);
            {
                MpUtil.Label(new Rect(0, 0, selectorWidth, 20f), "Server mods", GameFont.Tiny, TextAnchor.MiddleCenter);

                var selectorRect = new Rect(0, 20, selectorWidth, selectorHeight);
                GUI.color = new Color(1, 1, 1, 0.1f);
                Widgets.DrawBox(selectorRect);
                GUI.color = Color.white;
                
                Widgets.BeginScrollView(selectorRect, ref modScroll1, new Rect(0, 0, selectorRect.width - scrollbarWidth, remote.remoteMods.Count * 1 * modLabelHeight));

                int i = 0;
                for (int j = 0; j < 1; j++)
                    foreach (var m in remote.remoteMods)
                    {
                        Widgets.DrawHighlightIfMouseover(new Rect(0, i * modLabelHeight, selectorWidth, modLabelHeight));
                        GUI.DrawTexture(new Rect(2f, i * modLabelHeight + 2f, 16f, 16f), m.source.GetIcon());
                        MpUtil.Label(new Rect(modLabelHeight, i * modLabelHeight, selectorWidth, modLabelHeight), m.name, color: m.Installed ? Color.white : Red);
                        i++;
                    }

                Widgets.EndScrollView();
            }
            GUI.EndGroup();

            var mods2Rect = mods1Rect.
                Right(mods1Rect.width + selectorSpacing).
                TopPartPixels(selectorHeight + 25f);

            GUI.BeginGroup(mods2Rect);
            {
                MpUtil.Label(new Rect(0, 0, selectorWidth, 20f), "Your mods", GameFont.Tiny, TextAnchor.MiddleCenter);

                var selectorRect = new Rect(0, 20, selectorWidth, selectorHeight);
                GUI.color = new Color(1, 1, 1, 0.1f);
                Widgets.DrawBox(selectorRect);
                GUI.color = Color.white;

                Widgets.BeginScrollView(selectorRect, ref modScroll2, new Rect(0, 0, selectorRect.width - scrollbarWidth, ModsConfig.ActiveModsInLoadOrder.Count() * 1 * modLabelHeight));
                
                int i = 0;
                for (int j = 0; j < 1; j++)
                    foreach (var m in ModsConfig.ActiveModsInLoadOrder)
                    {
                        GUI.DrawTexture(new Rect(2f, i * modLabelHeight + 2f, 16f, 16f), m.Source.GetIcon());
                        Widgets.Label(new Rect(modLabelHeight, i * modLabelHeight, 100f, modLabelHeight), m.Name);
                        i++;
                    }

                Widgets.EndScrollView();
            }
            GUI.EndGroup();

            var mods3Rect = mods2Rect.
                Right(mods1Rect.width + selectorSpacing * 2).
                TopPartPixels(selectorHeight);

            GUI.BeginGroup(mods3Rect);
            {
                var a = new Rect(0, 0, selectorWidth, 35f * 2 + 10f).CenterOn(new Rect(0, 0, selectorWidth, selectorHeight));

                if (Widgets.ButtonText(a.TopPartPixels(35f), "Refresh"))
                    ModLister.RebuildModList();

                Widgets.ButtonText(a.BottomPartPixels(35f), "Subscribe to missing");

                MpUtil.Label(a.Under(25f), $"Not installed: {notInstalled.Count}", GameFont.Tiny, TextAnchor.MiddleCenter, Red);
                MpUtil.Label(a.Under(50f), $"(not on Steam: 3)", GameFont.Tiny, TextAnchor.MiddleCenter, Red);
            }
            GUI.EndGroup();
        }
    }
}

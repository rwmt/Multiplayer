using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Random = System.Random;

namespace Multiplayer.Client
{
    [HotSwappable]
    [HarmonyPatch(typeof(UI_BackgroundMain), nameof(UI_BackgroundMain.DoOverlay))]
    [StaticConstructorOnStartup]
    static class MainMenuAnimation
    {
        static Texture2D texture;

        const float r = 2282; // radius of planet
        const float cx = r + 80, cy = r + 384; // center coords
        const float origWidth = 8000, origHeight = 5000;
        const int texWidth = (int)cx + 1871; // max extent in the x direction
        const int texHeight = 5000;

        // The background might be drawn at the same time as the loading of a map on a different thread calls Verse.Rand
        // Verse.Rand can't be used here because it isn't thread-safe
        private static Random rand = new();

        private static float newPulseTimer;

        static MainMenuAnimation()
        {
            using (DeepProfilerWrapper.Section("Multiplayer main menu animation texture gen"))
            {
                texture = new(texWidth, texHeight, TextureFormat.RGBA32, false);
                RegenTexture();
            }
        }

        static void Prefix(UI_BackgroundMain __instance, Rect bgRect)
        {
            if (Input.GetKeyDown(KeyCode.KeypadPeriod))
                DoMainMenuPatch.hideMainMenu = !DoMainMenuPatch.hideMainMenu;

            if (!Multiplayer.settings.showMainMenuAnim
                || __instance.overrideBGImage != null && __instance.overrideBGImage != UI_BackgroundMain.BGPlanet)
                return;

            if (Input.GetKeyDown(KeyCode.End))
                RegenTexture();

            GUI.DrawTexture(bgRect with { width = texWidth * bgRect.width / origWidth }, texture, ScaleMode.ScaleToFit);

#if DEBUG
            if (Input.GetKey(KeyCode.LeftShift))
                DrawEdges(bgRect);
#endif

            if (Event.current.type != EventType.Repaint) return;

            newPulseTimer += Time.deltaTime;
            if (newPulseTimer > 1 / 12f)
            {
                var edge = edges[rand.Next(edges.Count())];
                var switchEndpoints = rand.NextDouble() < 0.5;
                pulses.Add(new Pulse(switchEndpoints ? edge.v2 : edge.v1, switchEndpoints ? edge.v1 : edge.v2) { starting = true });
                newPulseTimer = 0;
            }

            DrawPulses(bgRect);
        }

        static void DrawPulses(Rect bgRect)
        {
            for (int i = pulses.Count - 1; i >= 0; i--)
            {
                var pulse = pulses[i];
                pulse.progress += Math.Min(Time.deltaTime, 0.05f) * 0.25f / pulse.dist;

                if (pulse.progress > 1f)
                {
                    pulses.RemoveAt(i);

                    if (!pulse.EaseOut)
                        pulses.Add(new Pulse(pulse.end, pulse.nextEnd)
                        {
                            ending = rand.NextDouble() < 0.4f
                        });
                }

                var start = Quaternion.Euler(pulse.start.theta, pulse.start.phi, 0);
                var end = Quaternion.Euler(pulse.end.theta, pulse.end.phi, 0);

                var x = pulse.progress;

                var progress =
                    pulse.EaseIn && pulse.EaseOut ? x < 0.5 ? 2 * x * x : 1 - (float)Math.Pow(-2 * x + 2, 2) / 2 :
                    pulse.EaseIn ? x * x :
                    pulse.EaseOut ? 1 - (1 - x) * (1 - x) :
                    x;

                float theta = Quaternion.Lerp(start, end, progress).eulerAngles.x;
                float phi = Quaternion.Lerp(start, end, progress).eulerAngles.y;

                float posX = cx + r * Mathf.Cos(theta * Mathf.Deg2Rad) * Mathf.Sin(phi * Mathf.Deg2Rad);
                float posY = cy + r * Mathf.Sin(theta * Mathf.Deg2Rad);

                const float alphaEase = 0.3f;
                var alpha =
                    pulse.EaseIn && x < alphaEase ? x / alphaEase :
                    pulse.EaseOut && x > 1f - alphaEase ? (1 - x) / alphaEase :
                    1;

                using (MpStyle.Set(new Color(1, 1, 1, alpha)))
                    GUI.DrawTexture(
                        new Rect(
                            GetPos(bgRect, posX, posY),
                            new(6, 6)
                        ),
                        MultiplayerStatic.Pulse
                    );
            }
        }

        static Vert GetNewEnd(Pulse pulse)
        {
            Vert newEnd = null;
            var attempts = 0;

            while (newEnd == null && attempts < 20)
            {
                var randEdge = edges[rand.Next(edges.Count())];
                var otherVert = randEdge.OtherVert(pulse.end);
                if (otherVert != null && otherVert != pulse.start && otherVert != pulse.end)
                    newEnd = otherVert;
                attempts++;
            }

            return newEnd;
        }

        const float rotate = 35f;
        const float maskTheta = 10;

        static void DrawEdges(Rect bgRect)
        {
            {
                var proj1 = r * Project(maskTheta, -90).RotatedBy(rotate) + new Vector2(cx, cy);
                var proj2 = r * Project(maskTheta, 90).RotatedBy(rotate) + new Vector2(cx, cy);
                Widgets.DrawLine(GetPos(bgRect, proj1.x, proj1.y), GetPos(bgRect, proj2.x, proj2.y), Color.blue, 2f);
            }

            foreach (var e in edges)
            {
                var start = Quaternion.Euler(e.v1.theta, e.v1.phi, 0);
                var end = Quaternion.Euler(e.v2.theta, e.v2.phi, 0);

                float prevX = 0;
                float prevY = 0;

                for (float d = 0; d <= 1; d += 0.25f)
                {
                    float theta = Quaternion.Lerp(start, end, d).eulerAngles.x;
                    float phi = Quaternion.Lerp(start, end, d).eulerAngles.y;

                    var proj = Project(theta, phi);
                    float x = cx + r * proj.x;
                    float y = cy + r * proj.y;

                    if (prevX != 0 && prevY != 0)
                    {
                        var lineStart = GetPos(bgRect, prevX, prevY);
                        var lineEnd = GetPos(bgRect, x, y);

                        if (d == 0.5f)
                            MpUI.Label(new Rect(lineStart, new(100, 100)), $"{e.dist}", GameFont.Tiny);

                        Widgets.DrawLine(lineStart, lineEnd, Color.red, 2f);
                    }

                    prevX = x;
                    prevY = y;
                }
            }
        }

        static float SphereDist(float t1, float p1, float t2, float p2)
        {
            return Mathf.Acos(
                Mathf.Sin(t1 * Mathf.Deg2Rad) * Mathf.Sin(t2 * Mathf.Deg2Rad)
                + Mathf.Cos(t1 * Mathf.Deg2Rad) * Mathf.Cos(t2 * Mathf.Deg2Rad) * Mathf.Cos((p2 - p1) * Mathf.Deg2Rad)
            );
        }

        private static List<Vert> verts = new();
        private static List<Edge> edges = new();
        private static List<Pulse> pulses = new();

        static void Mst()
        {
            foreach (var v in verts)
            {
                v.cost = float.PositiveInfinity;
                v.costVert = null;
            }

            var q = verts.ToList();

            while (q.Count > 0)
            {
                var v = q.MinBy(a => a.cost);
                q.Remove(v);

                if (v.costVert != null)
                    edges.Add(new Edge(v, v.costVert));

                foreach (var w in q)
                {
                    var dist = SphereDist(v.theta, v.phi, w.theta, w.phi);
                    if (dist < w.cost)
                    {
                        w.cost = dist;
                        w.costVert = v;
                    }
                }
            }
        }

        static Vector2 InverseSpherical(float x, float y, float r)
        {
            var ro = new Vector2(x, y).magnitude;
            var c = Mathf.Asin(ro / r);
            return new Vector2(
                Mathf.Asin(y * Mathf.Sin(c) / ro) * Mathf.Rad2Deg,
                Mathf.Atan2(x * Mathf.Sin(c), ro * Mathf.Cos(c)) * Mathf.Rad2Deg
            );
        }

        static Vector2 GetPos(Rect rect, float x, float y)
        {
            return rect.min + new Vector2(rect.width * x / origWidth, rect.height * y / origHeight);
        }

        static Vector2 Project(float theta, float phi)
        {
            return new Vector2(
                Mathf.Cos(theta * Mathf.Deg2Rad) * Mathf.Sin(phi * Mathf.Deg2Rad),
                Mathf.Sin(theta * Mathf.Deg2Rad)
            );
        }

        static void RegenTexture()
        {
            using var _ = DeepProfilerWrapper.Section("RegenTexture");

            verts.Clear();

            // vaniller cities
            verts.Add(new Vert { theta = 60, phi = 34 });
            verts.Add(new Vert { theta = -4, phi = -68 });
            verts.Add(new Vert { theta = -2, phi = -68 });
            verts.Add(new Vert { theta = 4, phi = -50 });
            verts.Add(new Vert { theta = 18, phi = -61 });

            edges.Clear();
            pulses.Clear();

            var colors = new Color[texWidth * texHeight];

            // Cities
            for (int i = 0; i < 20;)
            {
                float cityTheta = rand.Range(maskTheta, 75f);
                float cityPhi = rand.Range(-75f, 75f);

                // ugh
                var pr = Project(cityTheta, cityPhi).RotatedBy(rotate);
                var inv = InverseSpherical(pr.x, pr.y, 1f);
                cityTheta = inv.x;
                cityPhi = inv.y;

                // Avoid the light side
                var cityXY = r * Project(cityTheta, cityPhi) + new Vector2(cx, cy);
                const float maskR = 2160;
                if ((new Vector2(520 + maskR, 64 + maskR) - cityXY).magnitude < maskR)
                    continue;

                var blobs = rand.Next(2, 5);
                if (rand.NextDouble() < 0.15) blobs += rand.Next(2, 4);

                Vector2? lit = null;

                // Blobs
                for (int k = 0; k < blobs; k++)
                {
                    var blobSpan = 2.5f + Math.Max(0, (blobs - 3) * 0.25f);
                    float theta = cityTheta + rand.Range(-blobSpan, blobSpan);
                    float phi = cityPhi + rand.Range(-blobSpan, blobSpan);

                    // Dots
                    for (int j = 0; j < 7; j++)
                    {
                        var blobSize = j == 0 || rand.NextDouble() < 0.3f ? 15 : 10;

                        float dotTheta = rand.Range(-1f, 1f);
                        float dotPhi = rand.Range(-1f, 1f);

                        for (int y = -blobSize; y <= blobSize; y++)
                        for (int x = -blobSize; x <= blobSize; x++)
                            if (x * x + y * y < blobSize * blobSize)
                            {
                                var proj = Project(theta + dotTheta, phi + dotPhi);
                                float centerX = cx + r * proj.x;
                                float centerY = cy + r * proj.y;

                                var c = 1 - new Vector2(x,y).magnitude / blobSize;
                                var fromCenter = 1 - new Vector2(dotTheta, dotPhi).magnitude / 2;
                                var fromCenter2 = 1 - new Vector2(theta - cityTheta, phi - cityPhi).magnitude / 5;
                                var a = c * fromCenter * fromCenter2;

                                if (j == 0) a *= 3f;
                                else if (a > 0.4) a *= 2f;

                                var pos = new Vector2(centerX, centerY);
                                var color = new Color(0.93f, 0.75f, 0.55f, a * a * 2);
                                var pixelX = (int)pos.x + x;
                                var pixelY = (int)origHeight - (int)pos.y + y;
                                colors[pixelX + pixelY * texWidth] = color;

                                if (color.a >= 1 && lit == null)
                                {
                                    lit = new Vector2(theta + dotTheta, phi + dotPhi);
                                }
                            }
                    }
                }

                if (lit != null)
                    verts.Add(new Vert { theta = lit.Value.x, phi = lit.Value.y });

                i++;
            }

            using (DeepProfilerWrapper.Section("SetPixels and Apply"))
            {
                texture.SetPixels(colors);
                texture.Apply();
            }

            Mst();
        }

        class Vert
        {
            public float theta;
            public float phi;

            public float cost;
            public Vert costVert;
        }

        class Edge
        {
            public readonly Vert v1, v2;
            public readonly float dist;

            public Edge(Vert v1, Vert v2)
            {
                this.v1 = v1;
                this.v2 = v2;

                dist = SphereDist(v1.theta, v1.phi, v2.theta, v2.phi);
            }

            protected bool Equals(Edge other)
            {
                return
                    Equals(v1, other.v1) && Equals(v2, other.v2)
                    || Equals(v1, other.v2) && Equals(v2, other.v1);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj.GetType() == GetType() && Equals((Edge)obj);
            }

            public override int GetHashCode()
            {
                return v1.GetHashCode() * v2.GetHashCode();
            }

            public Vert OtherVert(Vert v)
            {
                return v1 == v ? v2 : v2 == v ? v1 : null;
            }
        }

        class Pulse
        {
            public float progress;
            public Vert start;
            public Vert end;
            public float dist;

            public bool starting;
            public bool ending;
            public Vert nextEnd;

            public bool EaseIn => starting;
            public bool EaseOut => ending || nextEnd == null;

            public Pulse(Vert start, Vert end)
            {
                this.start = start;
                this.end = end;

                dist = SphereDist(start.theta, start.phi, end.theta, end.phi);
                nextEnd = GetNewEnd(this);
            }
        }
    }

    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.ShouldDoMainMenu), MethodType.Getter)]
    static class DoMainMenuPatch
    {
        public static bool hideMainMenu;

        static void Postfix(ref bool __result)
        {
            if (hideMainMenu)
                __result = false;
        }
    }
}

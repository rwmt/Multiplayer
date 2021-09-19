using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HotSwappable]
    [HarmonyPatch(typeof(UI_BackgroundMain), nameof(UI_BackgroundMain.DoOverlay))]
    [StaticConstructorOnStartup]
    static class MainMenuAnimation
    {
        static Texture2D texture = new(8000, 5000, TextureFormat.RGBA32, false);

        const float r = 2282;
        const float cx = r + 80, cy = r + 384;
        const float sizeX = 8000, sizeY = 5000;

        static MainMenuAnimation()
        {
            RegenTexture();
        }

        static void Prefix(UI_BackgroundMain __instance, Rect bgRect)
        {
            if (!Multiplayer.settings.showMainMenuAnim
                || __instance.overrideBGImage != null && __instance.overrideBGImage != UI_BackgroundMain.BGPlanet)
                return;

            if (Input.GetKeyDown(KeyCode.End))
                RegenTexture();

            GUI.DrawTexture(bgRect, texture, ScaleMode.ScaleToFit);

#if DEBUG
            if (Input.GetKey(KeyCode.LeftShift))
                DrawEdges(bgRect);
#endif

            if (Time.frameCount % 5 == 0)
            {
                var edge = edges.RandomElement();
                pulses.Add(new Pulse(edge.v1, edge.v2) { easeIn = true});
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

                    if (!pulse.easeOut)
                    {
                        var ends = edges.Where(e => e.Contains(pulse.end)).SelectMany(e => e).ToHashSet();
                        ends.Remove(pulse.end);
                        ends.Remove(pulse.start);
                        if (ends.Any())
                            pulses.Add(new Pulse(pulse.end, ends.RandomElement()) { easeOut = Rand.Value < 0.7f});
                    }
                }

                var start = Quaternion.Euler(pulse.start.theta, pulse.start.phi, 0);
                var end = Quaternion.Euler(pulse.end.theta, pulse.end.phi, 0);

                var x = pulse.progress;

                var progress =
                    pulse.easeIn && pulse.easeOut ? x < 0.5 ? 2 * x * x : 1 - (float)Math.Pow(-2 * x + 2, 2) / 2 :
                    pulse.easeIn ? x * x :
                    pulse.easeOut ? 1 - (1 - x) * (1 - x) :
                    x;

                float theta = Quaternion.Lerp(start, end, progress).eulerAngles.x;
                float phi = Quaternion.Lerp(start, end, progress).eulerAngles.y;

                float posX = cx + r * Mathf.Cos(theta * Mathf.Deg2Rad) * Mathf.Sin(phi * Mathf.Deg2Rad);
                float posY = cy + r * Mathf.Sin(theta * Mathf.Deg2Rad);

                const float alphaEase = 0.3f;
                var alpha =
                    pulse.easeIn && x < alphaEase ? x / alphaEase :
                    pulse.easeOut && x > 1f - alphaEase ? (1 - x) / alphaEase :
                    1;

                using (MpStyle.Set(new Color(1, 1, 1, alpha)))
                    GUI.DrawTexture(
                        new Rect(
                            bgRect.min + GetPos(bgRect, posX, posY),
                            new(6, 6)
                        ),
                        MultiplayerStatic.Pulse
                    );
            }
        }

        static void DrawEdges(Rect bgRect)
        {
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
                        var lineStart = bgRect.min + GetPos(bgRect, prevX, prevY);
                        var lineEnd = bgRect.min + GetPos(bgRect, x, y);

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
        private static HashSet<Edge> edges = new();
        private static List<Pulse> pulses = new();

        static void Mst()
        {
            foreach (var v in verts)
            {
                v.cost = float.PositiveInfinity;
                v.other = null;
            }

            var q = verts.ToList();

            while (q.Count > 0)
            {
                var v = q.MinBy(a => a.cost);
                q.Remove(v);

                if (v.other != null)
                    edges.Add(new Edge(v, v.other));

                foreach (var w in q)
                {
                    var dist = SphereDist(v.theta, v.phi, w.theta, w.phi);
                    if (dist < w.cost)
                    {
                        w.cost = dist;
                        w.other = v;
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
            return new Vector2(rect.width * x / sizeX, rect.height * y / sizeY);
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
            verts.Clear();

            // vaniller cities
            verts.Add(new Vert { theta = 60, phi = 34 });
            verts.Add(new Vert { theta = -4, phi = -68 });
            verts.Add(new Vert { theta = -2, phi = -68 });
            verts.Add(new Vert { theta = -4, phi = -50 });
            verts.Add(new Vert { theta = 18, phi = -61 });

            edges.Clear();
            pulses.Clear();

            var cols = texture.GetPixels();
            for (int i = 0; i < cols.Length; i++)
                cols[i] = new Color(0, 0, 0, 0);
            texture.SetPixels(cols);

            if (true)
                // Cities
                for (int i = 0; i < 20; i++)
                {
                    float cityTheta = Rand.Range(20f, 75f);
                    float cityPhi = Rand.Range(-75f, 75f);

                    // ugh
                    var pr = Project(cityTheta, cityPhi).RotatedBy(45f);
                    var inv = InverseSpherical(pr.x, pr.y, 1f);
                    cityTheta = inv.x;
                    cityPhi = inv.y;

                    var blobs = Rand.RangeInclusive(2, 4);
                    if (Rand.Value < 0.15) blobs += Rand.RangeInclusive(2, 3);

                    Vector2? lit = null;
                    int lits = 0;

                    // Blobs
                    for (int k = 0; k < blobs; k++)
                    {
                        const int height = 5000;
                        var blobSpan = 2.5f + Math.Max(0, (blobs - 3) * 0.25f);
                        float theta = cityTheta + Rand.Range(-blobSpan, blobSpan);
                        float phi = cityPhi + Rand.Range(-blobSpan, blobSpan);

                        // Dots
                        for (int j = 0; j < 7; j++)
                        {
                            var blobSize = j == 0 || Rand.Value < 0.3f ? 15 : 10;

                            float dotTheta = Rand.Range(-1f, 1f);
                            float dotPhi = Rand.Range(-1f, 1f);

                            for (int x = -blobSize; x <= blobSize; x++)
                            for (int y = -blobSize; y <= blobSize; y++)
                                if (x * x + y * y < blobSize * blobSize)
                                {
                                    var proj = Project(theta + dotTheta, phi + dotPhi);
                                    float centerX = cx + r * proj.x;
                                    float centerY = cy + r * proj.y;

                                    var pos = GetPos(new Rect(0, 0, 8000, height), centerX, centerY);

                                    var c = 1 - new Vector2(x,y).magnitude / blobSize;
                                    var fromCenter = 1 - new Vector2(dotTheta, dotPhi).magnitude / 2;
                                    var fromCenter2 = 1 - new Vector2(theta - cityTheta, phi - cityPhi).magnitude / 5;
                                    var a = c * fromCenter * fromCenter2;

                                    if (j == 0) a *= 3f;
                                    else if (a > 0.4) a *= 2f;

                                    var color = new Color(0.93f, 0.75f, 0.55f, a * a * 2);
                                    texture.SetPixel((int)pos.x + x, height - (int)pos.y + y, color);

                                    if (color.a >= 1 && lit == null)
                                    {
                                        lit = new Vector2(theta + dotTheta, phi + dotPhi);
                                    }
                                }
                        }
                    }

                    if (lit != null)
                        verts.Add(new Vert { theta = lit.Value.x, phi = lit.Value.y });
                }

            texture.Apply();

            Mst();
        }

        class Vert
        {
            public float theta;
            public float phi;

            public float cost;
            public Vert other;
        }

        class Edge : IEnumerable<Vert>
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

            public IEnumerator<Vert> GetEnumerator()
            {
                yield return v1;
                yield return v2;
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

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        class Pulse
        {
            public float progress;
            public Vert start;
            public Vert end;
            public float dist;

            public bool easeIn;
            public bool easeOut;

            public Pulse(Vert start, Vert end)
            {
                this.start = start;
                this.end = end;

                dist = SphereDist(start.theta, start.phi, end.theta, end.phi);
            }
        }
    }
}

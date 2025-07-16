using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Factions
{
    public class Dialog_ChooseFactionColor : Window
    {
        public override float Margin => 0f;

        private readonly Action<Color> onChoose;
        private readonly List<Color> palette = new List<Color>
        {
            Color.white,
            Color.yellow,
            new ColorInt(218,166,26).ToColor,    // contributor yellow
            new ColorInt(255, 100, 0).ToColor,   // orange
            Color.red,
            new ColorInt(231,76,60).ToColor,     // core red
            Color.magenta,
            new ColorInt(128, 0, 128).ToColor,   // purple
            Color.blue,
            new Color32(0x00, 0xBC, 0xD8, 255),  // cyan / default faction color
            new ColorInt(38,196,133).ToColor,    // admin Green
            new ColorInt(26, 185, 12).ToColor,   // green
            new ColorInt(0, 100, 8).ToColor,     // dark green
            new ColorInt(87, 40, 0).ToColor,     // brown
            new ColorInt(145, 113, 53).ToColor,  // brown 2
            new ColorInt(193, 193, 193).ToColor, // gray 
            new ColorInt(129, 129, 129).ToColor, // gray 2
            new ColorInt(62, 62, 62).ToColor,    // gray 3
            Color.black
        };
        private Color selectedColor;

        private const float boxSize = 24f;
        private const float padding = 6f;

        private int gridSize = 0;
        private int actualRows = 0;

        public Dialog_ChooseFactionColor(Action<Color> onChoose, Color _selectedColor)
        {
            this.onChoose = onChoose;
            forcePause = false;
            absorbInputAroundWindow = true;
            draggable = false;
            doCloseX = false;
            closeOnClickedOutside = true;
            selectedColor = _selectedColor;

            gridSize = (int)Math.Ceiling(Math.Sqrt(palette.Count));
            actualRows = (palette.Count + gridSize - 1) / gridSize;
        }

        public override void PreOpen()
        {
            base.PreOpen();

            float windowWidth = padding + gridSize * (boxSize + padding);
            float windowHeight = padding + actualRows * (boxSize + padding);

            this.windowRect.size = new Vector2(windowWidth, windowHeight);
            this.windowRect.position = new Vector2(332, UI.screenHeight / 2f - 80f);
        }

        public override void DoWindowContents(Rect inRect)
        {
            for (int i = 0; i < palette.Count; i++)
            {
                int row = i / gridSize;
                int col = i % gridSize;

                float x = inRect.x + padding + col * (boxSize + padding);
                float y = inRect.y + padding + row * (boxSize + padding);

                Rect boxRect = new Rect(x, y, boxSize, boxSize);
                Color c = palette[i];

                if (c == selectedColor)
                    Widgets.DrawBoxSolidWithOutline(boxRect, c, Widgets.OptionSelectedBGBorderColor, 2);
                else if (Mouse.IsOver(boxRect))
                    Widgets.DrawBoxSolidWithOutline(boxRect.ExpandedBy(1f), c, Color.white, 2);
                else
                    Widgets.DrawBoxSolidWithOutline(boxRect, c, Color.white, 1);

                if (Widgets.ButtonInvisible(boxRect))
                {
                    selectedColor = c;
                    onChoose?.Invoke(c);
                    Close();
                }
            }
        }       
    }
}

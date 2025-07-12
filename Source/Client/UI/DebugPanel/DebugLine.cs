using UnityEngine;

namespace Multiplayer.Client.DebugUi
{
public static partial class SyncDebugPanel
    {
        // Data structures for organizing debug sections
        private struct DebugLine
        {
            public string Label;
            public string Value;
            public Color Color;

            public DebugLine(string label, string value, Color color)
            {
                Label = label;
                Value = value;
                Color = color;
            }
        }
    }
} 

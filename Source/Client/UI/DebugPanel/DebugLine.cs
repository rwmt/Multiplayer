using UnityEngine;

namespace Multiplayer.Client.DebugUi
{
    // Data structures for organizing debug sections
    internal struct DebugLine
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

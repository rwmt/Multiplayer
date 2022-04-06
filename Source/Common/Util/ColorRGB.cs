using UnityEngine;
using Verse;

namespace Multiplayer.Common
{
    public struct ColorRGB : IExposable
    {
        public byte r, g, b;

        public ColorRGB(byte r, byte g, byte b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public void ExposeData()
        {
            ScribeAsInt(ref r, "r");
            ScribeAsInt(ref g, "g");
            ScribeAsInt(ref b, "b");
        }

        private void ScribeAsInt(ref byte value, string label)
        {
            int temp = value;
            Scribe_Values.Look(ref temp, label);
            value = (byte)temp;
        }

        public static implicit operator Color(ColorRGB value) => new(value.r / 255f, value.g / 255f, value.b / 255f);
    }
}

namespace Multiplayer.Client.DebugUi
{
    internal struct DebugSection
    {
        public string Title;
        public DebugLine[] Lines;

        public DebugSection(string title, DebugLine[] lines)
        {
            Title = title;
            Lines = lines;
        }
    }
} 

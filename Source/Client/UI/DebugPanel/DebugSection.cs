namespace Multiplayer.Client.DebugUi
{
public static partial class SyncDebugPanel
    {
        private struct DebugSection
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
} 

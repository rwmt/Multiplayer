namespace Multiplayer.Client
{
    public class SyncLogger
    {
        public const string RootNodeName = "Root";

        public LogNode current = new(RootNodeName);
        private int stopped;

        public T NodePassthrough<T>(string text, T val)
        {
            Node($"{text}{val}");
            return val;
        }

        public LogNode Node(string text)
        {
            if (stopped > 0)
                return current;
            LogNode logNode = new LogNode(text, current);
            current.children.Add(logNode);
            return logNode;
        }

        public void Enter(string? text)
        {
            if (stopped <= 0)
                current = Node(text ?? "");
        }

        public void Exit()
        {
            if (stopped <= 0)
                current = current.parent!;
        }

        public void Pause()
        {
            stopped++;
        }

        public void Resume()
        {
            stopped--;
        }

        public void AppendToCurrentName(string append)
        {
            current.text += append;
        }
    }

}

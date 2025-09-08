using System.Collections.Generic;

namespace Multiplayer.Client
{
    public class LogNode(string text, LogNode? parent = null)
    {
        public LogNode? parent = parent;
        public string text = text;
        public List<LogNode> children = new();
    }

}

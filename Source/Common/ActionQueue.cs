using System;
using System.Collections.Generic;

namespace Multiplayer.Common
{
    public class ActionQueue
    {
        private Queue<Action> queue = new();
        private Queue<Action> tempQueue = new();

        public void RunQueue(Action<string> errorLogger)
        {
            lock (queue)
            {
                if (queue.Count > 0)
                {
                    foreach (Action a in queue)
                        tempQueue.Enqueue(a);
                    queue.Clear();
                }
            }

            try
            {
                while (tempQueue.Count > 0)
                    tempQueue.Dequeue().Invoke();
            }
            catch (Exception e)
            {
                errorLogger($"Exception while executing action queue: {e}");
            }
        }

        public void Enqueue(Action action)
        {
            lock (queue)
                queue.Enqueue(action);
        }
    }
}

namespace ProjectT.Server.Messages
{
    using Cysharp.Threading.Tasks.Triggers;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using Unity.Jobs;

    public class MessageQueue<T> where T : struct
    {
        private Queue<T> queue1;
        private Queue<T> queue2;

        private Queue<T> refInput;
        private Queue<T> refOutput;

        public int Count => refOutput.Count;

        public MessageQueue(int capacity = 512)
        {
            queue1 = new Queue<T>(capacity);
            queue2 = new Queue<T>(capacity);

            refInput = queue1;
            refOutput = queue2;
        }

        public void Enqueue(T message)
        {
            lock(this)
            {
                refInput.Enqueue(message);
            }
        }

        public bool Dequeue(out T outMessage)
        {
            if(refOutput.Count > 0)
            {
                outMessage = refOutput.Dequeue();
                return true;
            }
            else
            {
                Swap();
                outMessage = new T();
                return false;
            }
        }

        public bool DequeueOld(out T outMessage)
        {
            if(refOutput.Count >0)
            {
                outMessage = refOutput.Dequeue();
            }
            else
            {
                Swap();

                if(refOutput.Count > 0)
                {
                    outMessage = refOutput.Dequeue();
                    return false;
                }
            }

            Thread.Sleep(1);
            outMessage = new T();
            return false;
        }

        private void Swap()
        {
            lock(this)
            {
                var temp = refInput;
                refInput = refOutput;
                refOutput = temp;
            }
        }
    }
}
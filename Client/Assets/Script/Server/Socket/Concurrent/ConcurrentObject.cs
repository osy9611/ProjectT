using System.Collections;
using System.Collections.Generic;

namespace ProjectT.Concurrent
{
    public class ConcurrentObject<T> where T : class
    {
        private T value;

        public ConcurrentObject(T value = null)
        {
            this.value = value;
        }

        public bool Exist()
        {
            lock(this)
            {
                return this.value != null;
            }
        }

        public void Set(T value)
        {
            lock(this)
            {
                this.value = value;
            }
        }

        public bool Try(out T outValue)
        {
            lock(this)
            {
                if(value != null)
                {
                    outValue = value;
                    value = null;
                    return true;
                }
                else
                {
                    outValue = null;
                    return false;
                }
            }
        }
    }
}

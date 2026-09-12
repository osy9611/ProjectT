using System;
using System.Threading;

namespace ProjectT.Addressable
{
    public sealed class ResourceScope : IDisposable
    {
        private readonly Action<ResourceScope> onRelease;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool isDisposed;
        internal CancellationToken Token { get; }

        internal ResourceScope(Action<ResourceScope> onRelease)
        {
            this.onRelease = onRelease ?? throw new ArgumentNullException(nameof(onRelease));
            Token = lifetime.Token;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;

            try
            {
                lifetime.Cancel();
            }
            finally
            {
                try
                {
                    onRelease(this);
                }
                finally
                {
                    lifetime.Dispose();
                }
            }
        }
    }
}

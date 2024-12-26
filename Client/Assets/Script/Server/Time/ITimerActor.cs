using System.Threading;

namespace ProjectT.Server.Time
{
    public interface ITimerActor
    {
        void Ready(TimerCallback callback, int dueTime, int period);
        bool Start();
        bool Change(int dueTime, int period);
        bool Stop();
        bool IsRunning();
    }
}
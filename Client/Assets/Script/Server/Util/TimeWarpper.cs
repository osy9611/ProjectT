namespace ProjectT.Server.Util
{
    using UnityEngine;
    public static class TimeWarpper
    {
        public static long RealtimeSinceStartup
        {
            get => (long)(Time.realtimeSinceStartup * 1000);
        }
    }
}
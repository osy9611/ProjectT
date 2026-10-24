using System;

namespace ProjectT.UGUI
{
    public class HudDefine
    {
        public enum eHudType
        {
        }

        public static string GetPath(eHudType type)
        {
            switch (type)
            {
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "HUD path is not defined.");
            }
        }
    }
}

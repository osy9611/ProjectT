using System;

namespace ProjectT.UGUI
{
    public class HudDefine
    {
        public enum eHudType
        {
            SampleHpBar
        }

        public static string GetPath(eHudType type)
        {
            switch (type)
            {
                case eHudType.SampleHpBar: return "Assets/BundleRes/UI/Sample/SampleHpBar.prefab";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "HUD path is not defined.");
            }
        }
    }
}

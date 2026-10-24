using System;
namespace ProjectT.UGUI
{

    public class UIDefine
    {
        public enum eUIType
        {
            SampleStaticA,
            SampleStaticB,
            SampleDynamic,
            SampleSystem
        }

        public static string GetUIPath(eUIType type)
        {
            switch (type)
            {
                case eUIType.SampleStaticA: return "Assets/BundleRes/UI/Sample/SampleStaticA.prefab";
                case eUIType.SampleStaticB: return "Assets/BundleRes/UI/Sample/SampleStaticB.prefab";
                case eUIType.SampleDynamic: return "Assets/BundleRes/UI/Sample/SampleDynamic.prefab";
                case eUIType.SampleSystem: return "Assets/BundleRes/UI/Sample/SampleSystem.prefab";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "UI path is not defined.");
            }
        }
    }
}


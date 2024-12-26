using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace ProjectT.UGUI
{

    public class UIDefine
    {
        public enum eUIEvent
        {
            Click,
            Drag,
            Up,
            Down
        }

        public enum eUIType
        {
            Test
        }

        public static string GetUIPath(eUIType type)
        {
            switch (type)
            {
                case eUIType.Test: return "Assets/BundleRes/UI/Test.prefab";
                default:
                    return string.Empty;
            }
        }
    }
}


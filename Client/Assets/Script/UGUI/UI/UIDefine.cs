using System;
namespace ProjectT.UGUI
{

    public class UIDefine
    {
        public enum eUIType
        {
        }

        public static string GetUIPath(eUIType type)
        {
            switch (type)
            {
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "UI path is not defined.");
            }
        }
    }
}


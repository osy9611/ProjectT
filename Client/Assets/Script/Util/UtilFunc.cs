using ProjectT;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class UtilFunc
{
    public static string MakeColorRichText(string original, string colorHex)
    {
        if (Application.isBatchMode)
        {
            return original;
        }
        else
        {
            if (string.IsNullOrEmpty(colorHex))
                return original;

            if (colorHex[0] != '#')
                colorHex = "#" + colorHex;

            if (colorHex.Length == 7)
                colorHex = colorHex + "FF";

            string colorString = string.Format("<color={0}>{1}</color>", colorHex, original);
            return colorString;
        }
    }

    public static bool LoadAtlasAndImage(UnityEngine.UI.Image image, DesignEnum.AtlasType type, string name)
    {
        if (image == null)
            return false;

        var tbData = Global.Table.AtlasDataInfos.Get((int)type);
        if (tbData == null)
            return false;

        var sprite = Global.Resource.GetSprite(tbData.Path, name, dontDestroy: true);
        image.sprite = sprite;

        return sprite != null;
    }
}

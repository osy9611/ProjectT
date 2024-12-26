using ProjectT;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

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
        var tbData = Global.Table.AtlasDataInfos.Get((int)type);
        if (tbData == null)
            return false;

        var spriteAtlas = Global.Resource.LoadAndGet<SpriteAtlas>(tbData.Path);

        if (spriteAtlas == null)
            return false;

        image.sprite = spriteAtlas.GetSprite(name);

        return true;
    }
}

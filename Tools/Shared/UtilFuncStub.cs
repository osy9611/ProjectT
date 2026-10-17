namespace ProjectT
{
    // 실제 UtilFunc는 Global.Table과 아틀라스에 의존하므로 Global.Log가 쓰는 함수만 둔다.
    public static class UtilFunc
    {
        public static string MakeColorRichText(string message, string color) => message;
    }
}

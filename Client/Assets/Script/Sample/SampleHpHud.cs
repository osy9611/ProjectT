using ProjectT.UGUI;
using UnityEngine;

namespace ProjectT.Sample
{
    // SampleActor 머리 위에 붙는 HP 바. 위치는 UIManager가 갱신하고, 값은 Actor가 HP 변경 시 넘긴다.
    public class SampleHpHud : ComHudAgent
    {
        [SerializeField] private RectTransform fill;

        public void SetRatio(float ratio)
        {
            // 스프라이트 없는 Image도 줄어들도록 Filled 대신 앵커로 너비를 바꾼다.
            fill.anchorMax = new Vector2(ratio, 1f);
        }
    }
}

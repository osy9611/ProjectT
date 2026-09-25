namespace ProjectT.UGUI
{
    using UnityEngine;

    // 부모 RectTransform이 화면 전체를 덮는다는 전제로 안전 영역을 정규화 앵커로 적용한다.
    // UIContainer가 위젯 루트의 앵커를 다시 설정하므로 위젯 루트가 아니라 그 아래 패널에 붙인다.
    [RequireComponent(typeof(RectTransform))]
    public class UISafeArea : MonoBehaviour
    {
        [SerializeField] private bool conformTop = true;
        [SerializeField] private bool conformBottom = true;
        [SerializeField] private bool conformLeft = true;
        [SerializeField] private bool conformRight = true;

        private RectTransform panel;
        private Rect appliedSafeArea;
        private Vector2Int appliedScreenSize;
        private ScreenOrientation appliedOrientation;

        private void Awake()
        {
            panel = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            Rect safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);

            if (safeArea == appliedSafeArea && screenSize == appliedScreenSize && Screen.orientation == appliedOrientation)
                return;

            if (screenSize.x <= 0 || screenSize.y <= 0)
                return;

            var anchorMin = new Vector2(conformLeft ? safeArea.xMin : 0f, conformBottom ? safeArea.yMin : 0f);
            var anchorMax = new Vector2(conformRight ? safeArea.xMax : screenSize.x, conformTop ? safeArea.yMax : screenSize.y);

            anchorMin.x /= screenSize.x;
            anchorMin.y /= screenSize.y;
            anchorMax.x /= screenSize.x;
            anchorMax.y /= screenSize.y;

            // 일부 삼성 기기는 시작 직후 NaN 영역을 한 번 반환한다. 적용 값을 기록하지 않아야 다음 프레임에 다시 적용한다.
            if (!(anchorMin.x >= 0f && anchorMin.y >= 0f && anchorMax.x >= 0f && anchorMax.y >= 0f))
                return;

            panel.anchorMin = anchorMin;
            panel.anchorMax = anchorMax;

            appliedSafeArea = safeArea;
            appliedScreenSize = screenSize;
            appliedOrientation = Screen.orientation;
        }
    }
}

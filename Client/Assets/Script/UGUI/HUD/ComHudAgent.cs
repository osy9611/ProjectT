using ProjectT.Pivot;
using UnityEngine;

namespace ProjectT.UGUI
{
    // 월드 위치를 따라가는 HUD. 위치 갱신은 UIManager가 프레임마다 한 번에 수행한다.
    public class ComHudAgent : MonoBehaviour
    {
        private PivotInfo pivotInfo;

        internal void AttachInternal(PivotInfo pivotInfo)
        {
            this.pivotInfo = pivotInfo;
        }

        internal void UpdatePositionInternal(Camera worldCamera, RectTransform root, Camera uiCamera)
        {
            Vector3 screenPoint = worldCamera.WorldToScreenPoint(pivotInfo.PivotTr.position);

            // 카메라 뒤의 대상은 화면 좌표가 반전되어 엉뚱한 위치에 그려지므로 숨긴다.
            bool visible = screenPoint.z > 0f;
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);

            if (!visible)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, uiCamera, out Vector2 localPoint);
            transform.localPosition = localPoint;
        }
    }
}

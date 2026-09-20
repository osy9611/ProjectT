using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectT.Scene
{
    public class SceneTransitionView : MonoBehaviour
    {
        public float FadeDuration = 0.15f;
        private CanvasGroup group;

        public void Initialize()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            gameObject.AddComponent<GraphicRaycaster>();
            group = gameObject.AddComponent<CanvasGroup>();
            var image = new GameObject("Fade", typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(transform, false);
            image.color = Color.black;
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            ResetView();
        }

        public virtual UniTask ShowAsync(CancellationToken token)
        {
            return FadeAsync(1f, token);
        }

        public virtual UniTask HideAsync(CancellationToken token)
        {
            return FadeAsync(0f, token);
        }

        private async UniTask FadeAsync(float target, CancellationToken token)
        {
            group.blocksRaycasts = true;
            float start = group.alpha;
            for (float elapsed = 0f; elapsed < FadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                token.ThrowIfCancellationRequested();
                group.alpha = Mathf.Lerp(start, target, elapsed / FadeDuration);
                await UniTask.Yield(cancellationToken: token);
            }
            token.ThrowIfCancellationRequested();
            group.alpha = target;
        }

        public void ResetView()
        {
            if (group == null)
                return;
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }
    }
}

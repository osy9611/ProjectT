namespace ProjectT.UGUI
{
    using ProjectT.Util;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UI;

    public enum eUIContainerType
    {
        System,  // 게임 시스템 UI 관련
        Dynamic, // 동적 UI 관련
        Static,   // 정적 UI 관련
        HUD
    }

    public abstract class UIBase : MonoBehaviour
    {
        [SerializeField] private eUIContainerType type;
        public eUIContainerType Type => type;

        [SerializeField] private Selectable firstSelected;
        private GameObject lastSelected;

        private readonly Dictionary<Type, UnityEngine.Object[]> objects = new Dictionary<Type, UnityEngine.Object[]>();

        private bool isActive = false;
        public bool IsActive => isActive;

        private UIContainer owner;
        private bool released;

        internal UIDefine.eUIType UIType { get; private set; }

        protected virtual void OnEnter() { }

        protected virtual void OnLeave() { }

        internal void InitializeInternal(UIContainer container, UIDefine.eUIType uiType)
        {
            owner = container;
            UIType = uiType;
            OnEnter();
        }

        public void Show()
        {
            if (released || owner == null)
                throw new InvalidOperationException("UI is not available.");
            owner.ShowWidget(this);
        }

        // GameObject 활성화는 UIContainer가 스택·포커스와 함께 먼저 처리한다.
        internal void ShowInternal()
        {
            if (isActive)
                return;

            isActive = true;
            OnShow();
        }

        public void Hide(bool restorePrevious = true)
        {
            if (released)
                return;

            if (owner == null)
                throw new InvalidOperationException("UI is not initialized.");

            owner.HideWidget(this, restorePrevious);
        }

        internal void HideInternal()
        {
            if (!isActive)
                return;

            isActive = false;
            OnHide();
        }

        // 해제 순서는 UIContainer.ReleaseWidget이 정한다.
        internal bool MarkReleasedInternal()
        {
            if (released)
                return false;

            released = true;
            owner = null;
            return true;
        }

        internal void LeaveInternal()
        {
            OnLeave();
        }

        protected virtual void OnDestroy()
        {
            if (owner != null)
                owner.ReleaseWidget(this);
        }

        protected abstract void OnShow();
        protected abstract void OnHide();

        internal void BringToFrontInternal()
        {
            this.transform.SetAsLastSibling();
        }

        internal void RememberSelectionInternal(GameObject selected)
        {
            if (selected != null && selected.transform.IsChildOf(transform))
                lastSelected = selected;
        }

        // CanvasGroup 차단 중에도 대상을 정할 수 있도록 Selectable 자체의 interactable만 확인한다.
        internal GameObject GetSelectionTargetInternal()
        {
            if (lastSelected != null && lastSelected.TryGetComponent(out Selectable last) && last.isActiveAndEnabled && last.interactable)
                return lastSelected;

            if (firstSelected != null && firstSelected.isActiveAndEnabled && firstSelected.interactable)
                return firstSelected.gameObject;

            foreach (var selectable in GetComponentsInChildren<Selectable>())
            {
                if (selectable.isActiveAndEnabled && selectable.interactable)
                    return selectable.gameObject;
            }

            return null;
        }

        protected void Bind<T>(Type type) where T : UnityEngine.Object
        {
            if (type == null || !type.IsEnum)
                throw new ArgumentException("Binding type must be an enum.", nameof(type));

            var values = Enum.GetValues(type);
            for (int i = 0; i < values.Length; ++i)
            {
                if (!values.GetValue(i).Equals(Enum.ToObject(type, i)))
                    throw new ArgumentException($"Binding enum must contain unique consecutive values starting at zero: {type.Name}", nameof(type));
            }
            BindInternal<T>(Enum.GetNames(type));
        }

        private void BindInternal<T>(string[] names) where T : UnityEngine.Object
        {
            if (objects.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"UI {name} already has a {typeof(T).Name} binding group.");

            var bindings = new UnityEngine.Object[names.Length];
            for (int i = 0; i < names.Length; ++i)
            {
                if (gameObject.name == names[i])
                    bindings[i] = typeof(T) == typeof(GameObject) ? gameObject : GetComponent<T>();
                else
                    bindings[i] = typeof(T) == typeof(GameObject)
                        ? ComUtilFunc.FindChild(gameObject, names[i], true)
                        : ComUtilFunc.FindChild<T>(gameObject, names[i], true);

                if (bindings[i] == null)
                    throw new InvalidOperationException($"UI {name} is missing required {typeof(T).Name} binding '{names[i]}' at index {i}.");
            }
            objects.Add(typeof(T), bindings);
        }

        protected T Get<T>(int idx) where T : UnityEngine.Object
        {
            if (!objects.TryGetValue(typeof(T), out var bindings))
                throw new InvalidOperationException($"UI {name} has no {typeof(T).Name} binding group.");
            if (idx < 0 || idx >= bindings.Length)
                throw new ArgumentOutOfRangeException(nameof(idx), idx, $"UI {name} {typeof(T).Name} binding index is out of range.");

            return bindings[idx] as T;
        }

        protected Text GetText(int idx) { return Get<Text>(idx); }
        protected Button GetButton(int idx) { return Get<Button>(idx); }
    }

}

namespace ProjectT.UGUI
{
    using ProjectT.Util;
    using System;
    using System.Collections.Generic;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;
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

        protected Dictionary<Type, UnityEngine.Object[]> objects = new Dictionary<Type, UnityEngine.Object[]>();

        protected bool isActive = false;
        public bool IsActive => isActive;

        private UIContainer owner;
        private bool released;

        protected virtual void OnEnter() { }

        protected virtual void OnLeave() { }

        internal void InitializeInternal(UIContainer container)
        {
            owner = container;
            OnEnter();
        }

        public void Show()
        {
            if (released || owner == null)
                throw new InvalidOperationException("UI is not available.");
            owner.ShowWidget(this);
        }

        internal void ShowInternal()
        {
            gameObject.SetActive(true);

            if (isActive)
                return;

            isActive = true;
            bool shown = false;

            try
            {
                OnShow();
                shown = true;
            }
            finally
            {
                if (!shown)
                {
                    isActive = false;
                    gameObject.SetActive(false);
                }
            }
        }

        public void Hide(bool activePrevUI = true)
        {
            if (released)
                return;

            if (owner == null)
                throw new InvalidOperationException("UI is not initialized.");

            owner.HideWidget(this, activePrevUI);
        }

        internal void HideInternal()
        {
            gameObject.SetActive(false);

            if (!isActive)
                return;

            isActive = false;
            OnHide();
        }

        internal void ReleaseInternal()
        {
            if (released)
                return;

            released = true;

            var container = owner;
            bool restorePrevious = container != null && container.DetachWidget(this);

            List<Exception> errors = null;
            ErrorCollector.Run(ref errors, this, widget => widget.HideInternal());
            ErrorCollector.Run(ref errors, this, widget => widget.OnLeave());

            owner = null;

            if (restorePrevious)
                ErrorCollector.Run(ref errors, container, item => item.RestorePreviousInternal());

            ErrorCollector.ThrowIfAny(errors);
        }

        protected virtual void OnDestroy()
        {
            ReleaseInternal();
        }

        protected abstract void OnShow();
        protected abstract void OnHide();

        internal void ChangeFirstDepth()
        {
            this.transform.SetAsLastSibling();
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

        protected void Bind<T>(string[] names) where T : UnityEngine.Object
        {
            BindInternal<T>(names);
        }

        private void BindInternal<T>(string[] names) where T : UnityEngine.Object
        {
            if (names == null)
                throw new ArgumentNullException(nameof(names));
            if (objects.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"UI {name} already has a {typeof(T).Name} binding group.");

            var bindings = new UnityEngine.Object[names.Length];
            for (int i = 0; i < names.Length; ++i)
            {
                if (string.IsNullOrEmpty(names[i]))
                    throw new ArgumentException($"UI {name} has an empty binding name at index {i}.", nameof(names));

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

        protected T[] Get<T>() where T : UnityEngine.Object
        {
            if (!this.objects.TryGetValue(typeof(T), out var objects))
                return null;

            return Array.ConvertAll(objects, x => (T)x);
        }

        protected GameObject GetObject(int idx) { return Get<GameObject>(idx); }
        protected Text GetText(int idx) { return Get<Text>(idx); }
        protected TextMeshProUGUI GetTextMeshPro(int idx) { return Get<TextMeshProUGUI>(idx); }
        protected Button GetButton(int idx) { return Get<Button>(idx); }
        protected Image GetImage(int idx) { return Get<Image>(idx); }

        public static void BindEvent(GameObject go, Action<PointerEventData, System.Action<Vector2>> action, UIDefine.eUIEvent type = UIDefine.eUIEvent.Click)
        {
            if (!go.TryGetComponent(out UIEventHandler handle))
                handle = go.AddComponent<UIEventHandler>();

            switch (type)
            {
                case UIDefine.eUIEvent.Click:
                    handle.OnClickHandler -= action;
                    handle.OnClickHandler += action;
                    break;
                case UIDefine.eUIEvent.Drag:
                    handle.OnDragHandler -= action;
                    handle.OnDragHandler += action;
                    break;
                case UIDefine.eUIEvent.Up:
                    handle.OnPointerUpHandler -= action;
                    handle.OnPointerUpHandler += action;
                    break;
                case UIDefine.eUIEvent.Down:
                    handle.OnPointerDownHandler -= action;
                    handle.OnPointerDownHandler += action;
                    break;
            }
        }
    }

}

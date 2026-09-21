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

        public virtual void OnEnter() { }

        public virtual void OnLeave() { }

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

            owner?.DetachWidget(this);

            List<Exception> errors = null;
            ErrorCollector.Run(ref errors, this, widget => widget.HideInternal());
            ErrorCollector.Run(ref errors, this, widget => widget.OnLeave());

            owner = null;

            ErrorCollector.ThrowIfAny(errors);
        }

        protected virtual void OnDestroy()
        {
            ReleaseInternal();
        }

        public abstract void OnShow();
        public abstract void OnHide();

        public void ChangeFirstDepth()
        {
            this.transform.SetAsLastSibling();
        }

        protected void Bind<T>(Type type) where T : UnityEngine.Object
        {
            string[] names = Enum.GetNames(type);
            UnityEngine.Object[] objects = new UnityEngine.Object[names.Length];
            this.objects.Add(typeof(T), objects);

            for(int i=0;i<names.Length;++i)
            {
                if(gameObject.name == names[i])
                {
                    if (typeof(T) == typeof(GameObject))
                        objects[i] = gameObject;
                    else
                        objects[i] = GetComponent<T>();
                }
                else
                {
                    if (typeof(T) == typeof(GameObject))
                        objects[i] = ComUtilFunc.FindChild(gameObject, names[i], true);
                    else
                        objects[i] = ComUtilFunc.FindChild<T>(gameObject, names[i], true);
                }

                if (objects[i] == null)
                    Global.Instance.Log($"Fail To Bind({names[i]})");
            }
        }

        protected void Bind<T>(string[] names) where T : UnityEngine.Object
        {
            UnityEngine.Object[] objects = new UnityEngine.Object[names.Length];
            this.objects.Add(typeof(T), objects);

            for (int i = 0, range = names.Length; i < range; ++i)
            {
                if (typeof(T) == typeof(GameObject))
                    objects[i] = ComUtilFunc.FindChild(gameObject, names[i], true);
                else
                    objects[i] = ComUtilFunc.FindChild<T>(gameObject, names[i], true);

                if (objects[i] == null)
                    Debug.LogError($"Fail To Bind({names[i]})");
            }
        }


        protected T Get<T>(int idx) where T : UnityEngine.Object
        {
            if (!this.objects.TryGetValue(typeof(T), out var objects))
                return null;

            return objects[idx] as T;
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

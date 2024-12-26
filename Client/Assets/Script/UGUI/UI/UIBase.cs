namespace ProjectT.UGUI
{
    using ProjectT.Util;
    using System;
    using System.Collections.Generic;
    using TMPro;
    using Unity.VisualScripting;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

    public enum eUIContainerType
    {
        System,  // 게임 시스템 UI 관련
        Dynamic, // 동적 UI 관련(다음 씬으로 전환되면 파괴됨)
        Static,   // 정적 UI 관련(다음 씬으로 전환되어도 유지가 됨)
        HUD
    }

    public abstract class UIBase : MonoBehaviour
    {
        [SerializeField] private eUIContainerType type;
        public eUIContainerType Type => type;

        private UIBase prevUI;
        public UIBase PrevUI { get => prevUI; set => prevUI = value; }

        protected Dictionary<Type, UnityEngine.Object[]> objects = new Dictionary<Type, UnityEngine.Object[]>();

        protected bool isActive = false;

        public virtual void OnEnter() { }

        public virtual void Show()
        {
            isActive = true;
            gameObject.SetActive(isActive);
            OnShow();
        }

        public virtual void Hide(bool activePrevUI = true)
        {
            isActive = false;
            gameObject.SetActive(isActive);

            if (activePrevUI && prevUI != null)
            {
                if (prevUI.isActive && !prevUI.gameObject.activeInHierarchy)
                    prevUI.gameObject.SetActive(true);
                else
                    prevUI.Show();
                prevUI = null;
            }

            OnHide();
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
            UnityEngine.Object[] objects = null;
            if (!this.objects.ContainsKey(typeof(T)))
                return null;

            return objects[idx] as T;
        }

        protected T[] Get<T>() where T : UnityEngine.Object
        {
            UnityEngine.Object[] objects = null;
            if (!this.objects.ContainsKey(typeof(T)))
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
            UIEventHandler handle = go.GetOrAddComponent<UIEventHandler>();
            if (handle == null)
            {
                Debug.LogError("Null UI_EventHandler");
                return;
            }

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

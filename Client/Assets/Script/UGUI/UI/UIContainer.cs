namespace ProjectT.UGUI
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using ProjectT;
    using Unity.VisualScripting;
    using UnityEngine.UI;
    using ProjectT.Util;
    using Cysharp.Threading.Tasks;

    public class UIContainer
    {
        private Dictionary<UIDefine.eUIType, UIBase> uiDatas = new Dictionary<UIDefine.eUIType, UIBase>();
        public Dictionary<UIDefine.eUIType, UIBase> UIDatas { get => uiDatas; }



        private RectTransform[] typeRoot;
        public RectTransform[] TypeRoot { get => typeRoot; }

        private Transform uiCanvas2D;
        public Transform UICanvas2D { get => uiCanvas2D; }

        private Camera canvas2DCam;
        public Camera Canvas2DCam { get => canvas2DCam; }

        private CanvasScaler canvas2DScaler;
        private GraphicRaycaster raycaster2D;

        //AutoScale
        private Vector2 ratio = new Vector2(16, 9); //16:9
        private Vector2Int screenSize = new Vector2Int(0, 0);
        private ScreenOrientation screenOrientation = ScreenOrientation.AutoRotation;

        private CanvasGroup staticPanelCanvasGroup;
        private CanvasGroup dynamicPanelCanvasGroup;

        private UIManager uiManager;

        public UIContainer()
        {
            uiManager = Global.UI;
        }

        #region Methods
        public void OnEnter()
        {
            CreateCamera();
            CreateCanvas();

            InitialUI();
        }

        public void OnLeave()
        {

        }

        public void OnUpdate(float dt)
        {
            SetAutoSacle();
        }

        public void OnLateUpdate(float dt)
        {

        }

        public void OnFixedUpdate(float dt)
        {

        }
        #endregion

        private void InitialUI()
        {
            typeRoot = new RectTransform[System.Enum.GetValues(typeof(eUIContainerType)).Length];

            //2D Canvas
            var uiCanvas2DRect = uiCanvas2D.GetComponent<RectTransform>();

            foreach (eUIContainerType type in System.Enum.GetValues(typeof(eUIContainerType)))
            {
                typeRoot[(int)type] = new GameObject($"{type}").AddComponent<RectTransform>();
                var rect = typeRoot[(int)type].GetComponent<RectTransform>();
                typeRoot[(int)type].AddComponent<CanvasGroup>();


                rect.InitRectTransform(uiCanvas2DRect, AnchorPresets.StretchAll, PivotPresets.MiddleCenter);
            }
        }

        private void CreateCamera()
        {
            GameObject camUI2DObj = new GameObject("2DUICam");
            camUI2DObj.transform.SetParent(uiManager.RootObject.transform, false);
            camUI2DObj.transform.localPosition = new Vector3(0, 0, -10);

            Camera camUI2D = camUI2DObj.AddComponent<Camera>();
            camUI2D.fieldOfView = 1.0f;
            camUI2D.nearClipPlane = 50.0f;
            camUI2D.farClipPlane = 150.0f;
            camUI2D.cullingMask = 1 << LayerMask.NameToLayer("UI");
            camUI2D.depth = 3;
            camUI2D.clearFlags = CameraClearFlags.Depth;
            camUI2D.backgroundColor = Color.black;
            camUI2D.useOcclusionCulling = false;
            canvas2DCam = camUI2D;
        }

        private void CreateCanvas()
        {
            //2D Canvas
            uiCanvas2D = new GameObject("Canvas2D").AddComponent<RectTransform>();
            uiCanvas2D.SetParent(uiManager.RootObject.transform, false);
            ComUtilFunc.SetLayer(uiCanvas2D.gameObject, LayerMask.NameToLayer("UI"), true);

            var canvas2D = uiCanvas2D.GetComponent<Canvas>();
            if (canvas2D == null)
                canvas2D = uiCanvas2D.AddComponent<Canvas>();
            canvas2D.renderMode = RenderMode.ScreenSpaceCamera;
            canvas2D.worldCamera = canvas2DCam;

            canvas2DScaler = uiCanvas2D.gameObject.AddComponent<CanvasScaler>();
            canvas2DScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            raycaster2D = uiCanvas2D.gameObject.AddComponent<GraphicRaycaster>();
            raycaster2D.ignoreReversedGraphics = true;
            raycaster2D.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            raycaster2D.blockingMask = LayerMask.GetMask("UI");
        }


        public T CreateWidget<T>(UIDefine.eUIType type, string path) where T : UIBase
        {
            T widget = FindWiget<T>(type);

            if (widget != null)
            {
                Global.Instance.LogWarning($"UI Container {type} Already Have");
                return widget;
            }

            widget = LoadWidget<T>(type, path);
            widget.OnEnter();

            uiDatas.Add(type, widget);

            RectTransform rect = widget.GetComponent<RectTransform>();
            RectTransform root = typeRoot[(int)widget.Type].GetComponent<RectTransform>();
            if (rect != null)
                rect.InitRectTransform(root, AnchorPresets.StretchAll, PivotPresets.MiddleCenter);


            return widget;
        }

        public async UniTask<T> CreatWidgetAsync<T>(UIDefine.eUIType type, string path) where T : UIBase
        {
            T widget = FindWiget<T>(type);

            if (widget != null)
            {
                Global.Instance.LogWarning($"UI Container {type} Already Have");
                return widget;
            }

            widget = await LoadWidgetAsync<T>(type, path);

            widget.OnEnter();

            uiDatas.Add(type, widget);

            RectTransform rect = widget.GetComponent<RectTransform>();
            RectTransform root = typeRoot[(int)widget.Type].GetComponent<RectTransform>();
            if (rect != null)
                rect.InitRectTransform(root, AnchorPresets.StretchAll, PivotPresets.MiddleCenter);

            return widget;
        }

        public T FindWiget<T>(UIDefine.eUIType type) where T : UIBase
        {
            if (uiDatas.ContainsKey(type))
                return uiDatas[type] as T;
            return default(T);
        }

        public T LoadWidget<T>(UIDefine.eUIType type, string path) where T : UIBase
        {
            T widget = default(T);
            Global.Resource.LoadAsset<GameObject>(path,
                (result) =>
                {
                    if (result != null)
                    {
                        var obj = GameObject.Instantiate(result);
                        widget = obj.GetComponent<T>();
                    }
                });

            return widget;
        }

        public async UniTask<T> LoadWidgetAsync<T>(UIDefine.eUIType type, string path) where T : UIBase
        {
            T widget = default(T);
            await Global.Resource.LoadAssetAsync<GameObject>(path,
                (result) =>
                {
                    if (result != null)
                    {
                        var obj = GameObject.Instantiate(result);
                        widget = obj.GetComponent<T>();
                    }
                });

            return widget;
        }

        public void RemoveWidget(UIDefine.eUIType type,string path)
        {
            var uiBase = FindWiget<UIBase>(type);

            if (uiBase != null)
            {
                uiDatas[type].OnHide();
                uiDatas.Remove(type);

                Global.Resource.Release(path);
            }
        }

        private void SetAutoSacle()
        {
            if (Screen.width != screenSize.x
               || Screen.height != screenSize.y
               || Screen.orientation != screenOrientation)
            {
                screenSize.x = Screen.width;
                screenSize.y = Screen.height;
                screenOrientation = Screen.orientation;

                if (Screen.width > 0 && Screen.height > 0)
                {
                    //현재 화면의 가로/세로 비율
                    float currentAspectRaito = (float)Screen.width / Screen.height;

                    //원하는 비율 대비 현재 비율의 차이 계산
                    float scaleFactor = ratio.x / ratio.y;

                    //CanvasScaler의 Match Width Or Height 모드를 설정
                    canvas2DScaler.matchWidthOrHeight = (scaleFactor > 1) ? 0 : 1;
                    canvas2DScaler.referenceResolution = new Vector2(
                        canvas2DScaler.referenceResolution.x * scaleFactor,
                        canvas2DScaler.referenceResolution.y * scaleFactor
                    );
                }
            }
        }
    }
}

namespace ProjectT.UGUI
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine;
    using ProjectT;
    using UnityEngine.UI;
    using ProjectT.Util;
    using Cysharp.Threading.Tasks;
    using System.Linq;
    using System.Runtime.ExceptionServices;

    public class UIContainer
    {
        //뒤에서 앞 순서. System UI가 항상 최상위에 그려진다
        private static readonly eUIContainerType[] displayOrder =
        {
            eUIContainerType.Static,
            eUIContainerType.HUD,
            eUIContainerType.Dynamic,
            eUIContainerType.System
        };

        private Dictionary<UIDefine.eUIType, UIBase> uiDatas = new Dictionary<UIDefine.eUIType, UIBase>();
        public IReadOnlyDictionary<UIDefine.eUIType, UIBase> UIDatas { get; }

        private readonly List<UIBase> uiStack = new List<UIBase>();
        public IReadOnlyList<UIBase> UIStack { get; }
        private readonly Dictionary<UIDefine.eUIType, PendingCreation> pending = new Dictionary<UIDefine.eUIType, PendingCreation>();
        private readonly CancellationToken lifetimeToken;
        private readonly Transform root;
        private bool stopped;
        private bool clearing;
        private bool inputAllowed = true;

        private sealed class PendingCreation
        {
            internal readonly UniTaskCompletionSource<UIBase> Completion = new UniTaskCompletionSource<UIBase>();
            internal readonly CancellationTokenSource Cancellation;
            internal int Waiters;

            internal PendingCreation(CancellationToken token)
            {
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            }
        }

        private RectTransform[] typeRoot;

        private Transform uiCanvas2D;
        public Transform UICanvas2D { get => uiCanvas2D; }

        private Camera canvas2DCam;
        public Camera Canvas2DCam { get => canvas2DCam; }

        private CanvasScaler canvas2DScaler;
        private CanvasGroup canvas2DGroup;

        //AutoScale
        private Vector2 referenceResolution;
        private Vector2 ratio = new Vector2(16, 9); //16:9
        private Vector2Int screenSize = new Vector2Int(0, 0);
        private ScreenOrientation screenOrientation = ScreenOrientation.AutoRotation;

        public UIContainer(Transform root, CancellationToken lifetimeToken)
        {
            this.root = root;
            this.lifetimeToken = lifetimeToken;
            UIDatas = new System.Collections.ObjectModel.ReadOnlyDictionary<UIDefine.eUIType, UIBase>(uiDatas);
            UIStack = uiStack.AsReadOnly();
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
            stopped = true;
            ClearWidgetsInternal(true);
        }

        public void OnUpdate(float dt)
        {
            SetAutoScale();
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
                typeRoot[(int)type].InitRectTransform(uiCanvas2DRect, AnchorPresets.StretchAll, PivotPresets.MiddleCenter);
            }

            //enum 순서가 아니라 표시 순서로 배치한다. 시스템 공지가 다른 UI에 가려지면 안 된다
            for (int i = 0; i < displayOrder.Length; ++i)
            {
                typeRoot[(int)displayOrder[i]].SetSiblingIndex(i);
            }
        }

        private void CreateCamera()
        {
            GameObject camUI2DObj = new GameObject("2DUICam");
            camUI2DObj.transform.SetParent(root, false);
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
            uiCanvas2D.SetParent(root, false);
            ComUtilFunc.SetLayer(uiCanvas2D.gameObject, LayerMask.NameToLayer("UI"), true);

            var canvas2D = uiCanvas2D.gameObject.AddComponent<Canvas>();

            canvas2D.renderMode = RenderMode.ScreenSpaceCamera;
            canvas2D.worldCamera = canvas2DCam;
            //UI 카메라의 near(50)~far(150) 사이에 있어야 그려진다
            canvas2D.planeDistance = 100f;

            canvas2DScaler = uiCanvas2D.gameObject.AddComponent<CanvasScaler>();
            canvas2DScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            referenceResolution = canvas2DScaler.referenceResolution;

            var raycaster2D = uiCanvas2D.gameObject.AddComponent<GraphicRaycaster>();
            raycaster2D.ignoreReversedGraphics = true;
            raycaster2D.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            raycaster2D.blockingMask = LayerMask.GetMask("UI");

            canvas2DGroup = uiCanvas2D.gameObject.AddComponent<CanvasGroup>();
        }

        public void SetInputAllowed(bool allowed)
        {
            if (inputAllowed == allowed)
                return;

            inputAllowed = allowed;
            canvas2DGroup.interactable = allowed;
            canvas2DGroup.blocksRaycasts = allowed;
            ApplyInputAllowedInternal(uiCanvas2D.gameObject);
        }

        private void ApplyInputAllowedInternal(GameObject target)
        {
            foreach (var handler in target.GetComponentsInChildren<UIEventHandler>(true))
                handler.SetInputAllowedInternal(inputAllowed);
        }

        private void EnsureAvailable()
        {
            lifetimeToken.ThrowIfCancellationRequested();
            if (stopped || clearing)
                throw new InvalidOperationException("UI container is unavailable.");
        }

        public T CreateWidget<T>(UIDefine.eUIType type, string path) where T : UIBase
        {
            EnsureAvailable();
            if (pending.ContainsKey(type))
                throw new InvalidOperationException($"UI creation is already in progress: {type}");

            var widget = FindWidget<T>(type);
            if (widget != null)
                return widget;

            var creation = new PendingCreation(lifetimeToken);
            pending.Add(type, creation);
            try
            {
                var result = AttachWidget<T>(type, path, Global.Resource.LoadAndGet<GameObject>(path), creation.Cancellation.Token);

                CompletePendingInternal(type, creation, result);

                return result;
            }
            catch (Exception error)
            {
                FailPendingInternal(type, creation, error);
                throw;
            }
            finally
            {
                creation.Cancellation.Dispose();
            }
        }

        public async UniTask<T> CreateWidgetAsync<T>(UIDefine.eUIType type, string path, CancellationToken token) where T : UIBase
        {
            EnsureAvailable();
            token.ThrowIfCancellationRequested();

            bool started = false;
            if (!pending.TryGetValue(type, out var creation))
            {
                var widget = FindWidget<T>(type);
                if (widget != null)
                    return widget;

                creation = new PendingCreation(lifetimeToken);
                pending.Add(type, creation);
                started = true;
            }

            // 생성을 시작하기 전에 올려야 동기로 끝난 실패도 이 호출자에게 전달된다.
            creation.Waiters++;
            if (started)
                CreateWidgetInternalAsync<T>(type, path, creation).Forget(Global.LogException);

            try
            {
                // CompletionSource는 복수 대기를 지원한다. 호출자 취소는 공유 생성을 취소하지 않는다.
                var result = await creation.Completion.Task.AttachExternalCancellation(token);
                if (result is T typed)
                    return typed;

                throw new InvalidOperationException($"UI {type} is not {typeof(T).Name}.");
            }
            finally
            {
                creation.Waiters--;
            }
        }

        private async UniTask CreateWidgetInternalAsync<T>(UIDefine.eUIType type, string path, PendingCreation creation) where T : UIBase
        {
            try
            {
                var token = creation.Cancellation.Token;
                var prefab = await Global.Resource.LoadAndGetAsync<GameObject>(path, cancelToken: token);
                token.ThrowIfCancellationRequested();
                var widget = AttachWidget<T>(type, path, prefab, token);
                CompletePendingInternal(type, creation, widget);
            }
            catch (Exception error)
            {
                // 모든 호출자가 대기를 취소했다면 결과를 받을 곳이 없으므로 Forget 경계에서 보고한다.
                if (!FailPendingInternal(type, creation, error))
                    throw;
            }
            finally
            {
                creation.Cancellation.Dispose();
            }
        }

        private void RemovePendingInternal(UIDefine.eUIType type, PendingCreation creation)
        {
            if (pending.TryGetValue(type, out var current) && ReferenceEquals(current, creation))
                pending.Remove(type);
        }

        // 대기자 continuation은 동기로 실행되어 같은 타입을 다시 요청할 수 있으므로 결과를 넘기기 전에 등록을 해제한다.
        private void CompletePendingInternal(UIDefine.eUIType type, PendingCreation creation, UIBase widget)
        {
            RemovePendingInternal(type, creation);
            creation.Completion.TrySetResult(widget);
        }

        // 대기자 없이 실패를 넣으면 미관찰 예외로 다시 보고되므로 넣지 않는다. 호출자가 보고해야 하는 실패면 false를 반환한다.
        private bool FailPendingInternal(UIDefine.eUIType type, PendingCreation creation, Exception error)
        {
            RemovePendingInternal(type, creation);

            if (creation.Waiters == 0)
                return error is OperationCanceledException;

            creation.Completion.TrySetException(error);
            return true;
        }

        private T AttachWidget<T>(UIDefine.eUIType type, string path, GameObject prefab, CancellationToken token) where T : UIBase
        {
            EnsureAvailable();
            token.ThrowIfCancellationRequested();

            var source = prefab == null ? null : prefab.GetComponent<T>();
            if (source == null)
                throw new InvalidOperationException($"UI prefab has no {typeof(T).Name}: {type}");

            var instance = GameObject.Instantiate(prefab, typeRoot[(int)source.Type], false);
            var widget = instance.GetComponent<T>();
            try
            {
                instance.SetActive(false);

                var rect = widget.GetComponent<RectTransform>();
                if (rect == null)
                    throw new InvalidOperationException($"UI prefab has no RectTransform: {type}");

                rect.InitRectTransform(typeRoot[(int)widget.Type], AnchorPresets.StretchAll, PivotPresets.MiddleCenter);

                widget.InitializeInternal(this);
                ApplyInputAllowedInternal(instance);
                token.ThrowIfCancellationRequested();

                EnsureAvailable();

                // System UI는 씬 전환 후에도 남으므로 프리팹이 씬 Scope와 함께 해제되지 않게 앱 Scope 소유를 추가한다.
                // 실패할 수 있는 단계를 모두 지난 뒤에 추가해야 생성 실패 시 참조가 남지 않는다.
                if (widget.Type == eUIContainerType.System)
                    Global.Resource.LoadAndGet<GameObject>(path, dontDestroy: true);

                uiDatas.Add(type, widget);
                return widget;
            }
            catch (Exception error)
            {
                List<Exception> errors = null;
                ErrorCollector.Run(ref errors, widget, DestroyWidgetInternal);
                if (errors != null)
                {
                    errors.Insert(0, error);
                    throw new AggregateException(errors);
                }
                throw;
            }
        }

        public T FindWidget<T>(UIDefine.eUIType type) where T : UIBase
        {
            if (!uiDatas.TryGetValue(type, out var widget) || widget == null)
            {
                uiDatas.Remove(type);
                return null;
            }

            if (widget is T typed)
                return typed;

            throw new InvalidOperationException($"UI {type} is not {typeof(T).Name}.");
        }

        public void RemoveWidget(UIDefine.eUIType type)
        {
            if (pending.TryGetValue(type, out var creation))
            {
                pending.Remove(type);
                creation.Cancellation.Cancel();
            }
            if (!uiDatas.TryGetValue(type, out var widget))
                return;

            uiDatas.Remove(type);
            bool wasCurrent = GetCurrentStackUI() == widget;
            try
            {
                DestroyWidgetInternal(widget);
            }
            finally
            {
                if (wasCurrent)
                    RestorePreviousInternal();
            }
        }

        public void ClearTransientWidgets()
        {
            ClearWidgetsInternal(false);
        }

        private void ClearWidgetsInternal(bool includeSystem)
        {
            if (clearing)
                return;

            clearing = true;

            List<Exception> errors = null;
            try
            {
                // 타입을 확인하기 전인 로딩 요청도 전환 경계에서 취소해 이전 씬 UI의 늦은 등록을 막는다.
                var creations = new List<PendingCreation>(pending.Values);
                pending.Clear();

                foreach (var creation in creations)
                {
                    ErrorCollector.Run(ref errors, creation, item => item.Cancellation.Cancel());
                }                    

                foreach (var pair in new List<KeyValuePair<UIDefine.eUIType, UIBase>>(uiDatas))
                {
                    if (!includeSystem && pair.Value != null && pair.Value.Type == eUIContainerType.System)
                        continue;

                    uiDatas.Remove(pair.Key);
                    ErrorCollector.Run(ref errors, pair.Value, DestroyWidgetInternal);
                }
            }
            finally
            {
                clearing = false;
            }
            ErrorCollector.ThrowIfAny(errors);
        }

        private void DestroyWidgetInternal(UIBase widget)
        {
            if (widget == null)
                return;

            try
            {
                widget.ReleaseInternal();
            }
            finally
            {
                GameObject.Destroy(widget.gameObject);
            }
        }

        internal void ShowWidget(UIBase widget)
        {
            EnsureAvailable();

            if (!uiDatas.ContainsValue(widget))
                throw new InvalidOperationException("UI is not registered.");

            // OnShow에서 다른 UI를 열 수 있으므로 표시 순서와 이력을 먼저 반영해야 나중에 연 UI가 앞에 남는다.
            widget.ChangeFirstDepth();
            if (widget.Type != eUIContainerType.Static)
            {
                widget.ShowInternal();
                return;
            }

            var previous = GetCurrentStackUI();
            uiStack.Remove(widget);
            uiStack.Add(widget);

            bool shown = false;
            Exception showError = null;
            Exception cleanupError = null;
            try
            {
                widget.ShowInternal();
                shown = true;
            }
            catch (Exception error)
            {
                showError = error;
            }
            finally
            {
                if (!shown)
                    uiStack.Remove(widget);

                // OnShow 중 다른 UI가 열리거나 표시가 실패해도 가장 위 UI만 보이도록 이전 UI를 정리한다.
                try
                {
                    if (previous != null && previous != GetCurrentStackUI())
                        previous.gameObject.SetActive(false);
                }
                catch (Exception error)
                {
                    cleanupError = error;
                }
            }

            if (showError != null)
            {
                if (cleanupError != null)
                    throw new AggregateException(showError, cleanupError);

                ExceptionDispatchInfo.Capture(showError).Throw();
            }

            if (cleanupError != null)
                ExceptionDispatchInfo.Capture(cleanupError).Throw();
        }

        internal void HideWidget(UIBase widget, bool activePrevUI)
        {
            bool wasCurrent = GetCurrentStackUI() == widget;
            uiStack.Remove(widget);

            try
            {
                widget.HideInternal();
            }
            finally
            {
                if (activePrevUI && wasCurrent)
                    RestorePreviousInternal();
            }
        }

        internal void RestorePreviousInternal()
        {
            if (clearing || stopped || lifetimeToken.IsCancellationRequested)
                return;

            var previous = GetCurrentStackUI();
            if (previous != null)
                previous.Show();
        }

        public UIBase GetCurrentStackUI()
        {
            uiStack.RemoveAll(item => item == null);
            return uiStack.Count == 0 ? null : uiStack[uiStack.Count - 1];
        }

        internal bool DetachWidget(UIBase widget)
        {
            bool wasCurrent = uiStack.Count > 0 && ReferenceEquals(uiStack[uiStack.Count - 1], widget);
            uiStack.Remove(widget);

            foreach (var pair in uiDatas)
            {
                if (ReferenceEquals(pair.Value, widget))
                {
                    uiDatas.Remove(pair.Key);
                    return wasCurrent;
                }
            }
            return false;
        }

        private void SetAutoScale()
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
                    float currentAspectRatio = (float)Screen.width / Screen.height;

                    //기준 비율
                    float targetAspectRatio = ratio.x / ratio.y;

                    //기준 해상도는 고정하고 매칭 축만 바꾼다. 이전 값에 배율을 누적하면 화면이 바뀔 때마다 기준이 밀린다
                    canvas2DScaler.referenceResolution = referenceResolution;
                    canvas2DScaler.matchWidthOrHeight = currentAspectRatio > targetAspectRatio ? 1f : 0f;
                }
            }
        }
    }
}

using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using ProtoBuf;
using ProjectT.Skill;

namespace ProjectT
{
    public class Global : MonoBehaviour
    {
        static Global s_instance = null;
        public static Global Instance { get => s_instance; }

        public bool LoadData = false;
        public bool UseDebugLog = true;
        private bool isInitialized = false;

        private List<KeyValuePair<string, ManagerBase>> m_managers = new List<KeyValuePair<string, ManagerBase>>();

        #region Managers
        private ResourceManager resource = new ResourceManager();
        public static ResourceManager Resource { get => Instance.resource; }

        private DataManager data = new DataManager();
        public static DataManager Data { get => Instance.data; }

        private PoolManager pool = new PoolManager();
        public static PoolManager Pool { get => Instance.pool; }

        private SceneManager scene = new SceneManager();
        public static SceneManager Scene { get => Instance.scene; }

        private UIManager ui = new UIManager();
        public static UIManager UI { get => Instance.ui; }

        private SoundManager sound = new SoundManager();
        public static SoundManager Sound { get => Instance.sound; }

        private NotificationManager notify = new NotificationManager();
        public static NotificationManager Notify { get => Instance.notify; }

        private ClientLocalStorageManager localStorage = new ClientLocalStorageManager();
        public static ClientLocalStorageManager LocalStorage { get => Instance.localStorage; }

        private CostumeManager costume = new CostumeManager();
        public static CostumeManager Costume { get => Instance.costume; }

        #endregion

        #region Extensions
        public static DesignTable.DataMgr Table { get => Instance.data.Table; }
        #endregion

        private void Awake()
        {
            Init();
        }

        private void Update()
        {
            foreach (var manager in m_managers)
            {
                manager.Value.OnUpdate(Time.deltaTime);
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!isInitialized)
                return;

            Log($"OnApplicationFocus({focus})");

            if (m_managers == null)
                return;

            for (int i = 0; i < m_managers.Count; ++i)
            {
                if (m_managers[i].Value != null)
                {
                    m_managers[i].Value.OnAppFocuse(focus);
                }
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (!isInitialized)
                return;

            Log($"OnApplicationPause({pause})");

            for (int i = 0; i < m_managers.Count; ++i)
            {
                if (m_managers[i].Value != null)
                {
                    m_managers[i].Value.OnAppPause(pause);
                }
            }
        }

        private void OnApplicationQuit()
        {
            Log("OnApplicationQuite()");
        }

        private void OnDestroy()
        {
            Log("OnDestory()");
            DestoryManagers();
        }

        private void Init()
        {
            if (s_instance != null)
                return;

            DontDestroyOnLoad(this.gameObject);
            s_instance = this.gameObject.GetComponent<Global>();

            CreateManagers();

            isInitialized = true;

            foreach (var manager in m_managers)
            {
                manager.Value.OnEnter();
            }

            if (LoadData)
            {
                Global.Data.GetTableDatas().Forget();
                Global.LocalStorage.LoadAllDataAsync().Forget();
            }

            SkillActionContainer.AutoRegister();
            BuffContainer.AutoRegister();
        }

        private void CreateManagers()
        {
            Log("CreateManagers()");

            if (m_managers == null)
                m_managers = new List<KeyValuePair<string, ManagerBase>>();

            CreateAndAddManager<ResourceManager>(ref resource);
            CreateAndAddManager<DataManager>(ref data);
            CreateAndAddManager<PoolManager>(ref pool);
            CreateAndAddManager<SceneManager>(ref scene);
            CreateAndAddManager<UIManager>(ref ui);
            CreateAndAddManager<SoundManager>(ref sound);
            CreateAndAddManager<NotificationManager>(ref notify);
            CreateAndAddManager<ClientLocalStorageManager>(ref localStorage);
            CreateAndAddManager<CostumeManager>(ref costume);
        }

        private void DestoryManagers()
        {
            Log("DestoryManagers()");

            for (int i = m_managers.Count - 1; i >= 0; --i)
            {
                if (m_managers[i].Value != null)
                    m_managers[i].Value.OnLeave();
            }

            DestoryManager<ResourceManager>(ref resource);
            DestoryManager<DataManager>(ref data);
            DestoryManager<PoolManager>(ref pool);
            DestoryManager<SceneManager>(ref scene);
            DestoryManager<UIManager>(ref ui);
            DestoryManager<SoundManager>(ref sound);
            DestoryManager<NotificationManager>(ref notify);
            DestoryManager<ClientLocalStorageManager>(ref localStorage);
            DestoryManager<CostumeManager>(ref costume);
        }

        private void CreateAndAddManager<T>(ref T manager) where T : ManagerBase, new()
        {
            if (manager == null)
                return;

            manager = new T();
            manager.Name = typeof(T).Name;
            manager.OnAppStart();
            AddManager(manager);
        }


        private void DestoryManager<T>(ref T manager) where T : ManagerBase
        {
            if (manager == null)
                return;

            manager.OnAppEnd();
            RemoveManager(manager);
            manager = default(T);
        }

        private void AddManager(ManagerBase manager)
        {
            KeyValuePair<string, ManagerBase> keyValuePaira = m_managers.FirstOrDefault(x => (x.Key.IndexOf(manager.Name, System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (keyValuePaira.Value != null)
                return;

            m_managers.Add(new KeyValuePair<string, ManagerBase>(manager.Name, manager));
        }

        private void RemoveManager(ManagerBase manager)
        {
            KeyValuePair<string, ManagerBase> handler = m_managers.FirstOrDefault(x => (x.Key.IndexOf(manager.Name, System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (handler.Value == null)
                return;

            m_managers.Remove(handler);
        }

        public static T GetManager<T>() where T : ManagerBase
        {
            var managerName = typeof(T).Name;

            KeyValuePair<string, ManagerBase> keyValuePair = Instance.m_managers.FirstOrDefault(x => (x.Key.IndexOf(managerName, System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (keyValuePair.Value != null)
                return (T)keyValuePair.Value;

            return default(T);
        }


        #region Log Methods
        public void Log(string msg, string colorHex = default)
        {
            if (UseDebugLog)
                Debug.Log(MakeTimeStampLog(UtilFunc.MakeColorRichText($"[Global] {msg}", colorHex), "INFO"));
        }

        public void LogWarning(string msg)
        {
            if (UseDebugLog)
                Debug.LogWarning(MakeTimeStampLog($"[Global] {msg}", "WARNING"));
        }

        public void LogError(string msg)
        {
            if (UseDebugLog)
                Debug.LogError(MakeTimeStampLog($"[Global] {msg}", "ERROR"));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string MakeTimeStampLog(string msg, string type)
        {
#if UNITY_EDITOR
            return msg;
#else
            var timestamp = $"[{type}][{System.DateTime.Now.ToString("yy-MM-dd HH:mm:ss")}]";
            msg = $"{timestamp} {msg}";
            return msg;
#endif
        }
        #endregion
    }
}



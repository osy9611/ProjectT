namespace ProjectT
{
    public partial class Global
    {
        public static PatchManager Patch { get => GetManager<PatchManager>(); }

        public static ResourceManager Resource { get => GetManager<ResourceManager>(); }

        public static DataManager Data { get => GetManager<DataManager>(); }

        public static PoolManager Pool { get => GetManager<PoolManager>(); }

        public static SceneManager Scene { get => GetManager<SceneManager>(); }

        public static UIManager UI { get => GetManager<UIManager>(); }

        public static SoundManager Sound { get => GetManager<SoundManager>(); }

        public static NotificationManager Notify { get => GetManager<NotificationManager>(); }

        public static InputManager Input { get => GetManager<InputManager>(); }

        public static DesignTable.DataMgr Table => Data.Table;

        private void RegisterManagers(ManagerHost host)
        {
            // 첫 비동기 대기 전까지는 Awake 안에서 Ready가 되므로 InputManager는 Actor 활성화보다 먼저 준비된다.
            host.Register(new InputManager());
            host.Register(new PatchManager(UseRemoteResource));
            host.Register(new ResourceManager());
            host.Register(new NotificationManager());
            host.Register(new DataManager(LoadData));
            host.Register(new PoolManager());
            host.Register(new SceneManager());
            host.Register(new UIManager());
            host.Register(new SoundManager());
        }
    }
}

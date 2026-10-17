namespace ProjectT
{
    public partial class Global
    {
        public static ResourceManager Resource => GetManager<ResourceManager>();

        public static PoolManager Pool => GetManager<PoolManager>();

        public static SceneManager Scene => GetManager<SceneManager>();

        public static InputManager Input => GetManager<InputManager>();

        public static NotificationManager Notify => GetManager<NotificationManager>();

        public static UIManager UI => GetManager<UIManager>();

        public static SoundManager Sound => GetManager<SoundManager>();

        public static DesignTable.DataMgr Table => GetManager<DataManager>().Table;
    }
}

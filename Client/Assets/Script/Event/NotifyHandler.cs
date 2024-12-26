public enum eNotifyHandler
{
    Default = 0x00000001,
    Page = 0x00000002,
    Widget = 0x00000004,
    Node = 0x00000008,
    IngameObject = 0x00000016,

    //util
    Util_GameFlow = Page | Widget
}

public interface INotifyHandler
{
    string HandlerName { get; }
    bool IsConnected { get; }
    eNotifyHandler GetHandlerType();
    int GetOrder();
    bool IsActiveAndEnabled();
    void OnConnectHandler();
    void OnDisConnectHandler();
    void OnNotify(INotify notify);
}
namespace ProjectT.Server.Messages
{
    public enum eMessageBuiltInDataId
    {
        None,

        //built - in data id
        // 0 ~ 10000
        Ping = 1000,
        Pong = 1001,
        Heartbeat = 1010,
        Connected = 1101, // Server to Client

        //user data
        ExternalData = 10000 // 20000
    }

    public enum eBuiltInEventId
    {
        Connected,
        Disconnected
    }
    
    public enum eMessageType
    {
        BuiltInEvent,
        Data
    }
}
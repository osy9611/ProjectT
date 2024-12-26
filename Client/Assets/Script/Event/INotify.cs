public interface INotify
{
    uint MsgCode { get; }
    object[] Data { get; }
}
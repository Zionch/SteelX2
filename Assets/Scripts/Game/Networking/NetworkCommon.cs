public struct TransportEvent
{
    public enum Type
    {
        Data = 10,
        Connect,
        Disconnect
    }
    public Type type;
    public int ConnectionId;
    public byte[] Data;

    public TransportEvent(Type type, int connectionId, byte[] data) {
        this.type = type;
        this.ConnectionId = connectionId;
        this.Data = data;
    }
}

public interface INetworkTransport
{
    void Connect();
    void Disconnect();
    void Shutdown();
    bool NextEvent(ref TransportEvent e);
    void SendData(int connectionId, TransportEvent.Type type, byte[] data);
    string GetConnectionDescription(int connectionId);
    void Update();
}


public struct TransportEvent
{
    public enum Type
    {
        Data,
        Connect,
        Disconnect
    }
    public Type type;
    public int ConnectionId;
    public byte[] Data;
    public int DataSize;
}

public interface INetworkTransport
{
    void Connect();
    void Disconnect();
    bool NextEvent(ref TransportEvent e);
    void SendData(int connectionId, byte[] data, int sendSize);
    string GetConnectionDescription(int connectionId);
    void Update();
    void Shutdown();
}



public class ServerGameWorld
{
    public void HandlePlayerConnect(int connectionId) {

    }

    public void HandlePlayerDisconnect(int connectionId) {

    }

    public void Shutdown() {

    }
}

public class ServerGameLoop : Game.IGameLoop, INetworkCallbacks
{
    private enum ServerState
    {
        Connecting,
        Loading,
        Active,
    }

    private NetworkServer _networkServer;
    private ServerGameWorld _serverGameWorld;

    public bool Init(string[] args) {
        _networkServer = new NetworkServer(new ServerPhotonNetworkTransport());
        _serverGameWorld = new ServerGameWorld();
        _networkStatistics = new NetworkStatisticsServer(_networkServer);
        _networkServer.Connect();

        return true;
    }

    public void Update() {
        _networkServer.Update(this);

        _networkServer.SendData();

        _networkStatistics.Update();
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
    }

    public void Shutdown() {
        _networkServer.Shutdown();
        _serverGameWorld.Shutdown();
    }

    public void OnConnect(int clientId) {
        if(_serverGameWorld != null)
            _serverGameWorld.HandlePlayerConnect(clientId);
    }

    public void OnDisconnect(int clientId) {
        if (_serverGameWorld != null)
            _serverGameWorld.HandlePlayerDisconnect(clientId);
    }

    private NetworkStatisticsServer _networkStatistics;
}


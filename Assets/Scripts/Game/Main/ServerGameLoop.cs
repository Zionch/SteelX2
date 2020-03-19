
public class ServerGameLoop : Game.IGameLoop
{
    private enum ServerState
    {
        Connecting,
        Loading,
        Active,
    }

    private NetworkServer _networkServer;

    public bool Init(string[] args) {
        _networkServer = new NetworkServer(new ServerPhotonNetworkTransport());
        _networkStatistics = new NetworkStatisticsServer(_networkServer);
        _networkServer.Connect();

        return true;
    }

    public void Update() {
        _networkServer.Update();

        _networkServer.SendData();

        _networkStatistics.Update();
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
    }

    public void Shutdown() {
        _networkServer.Shutdown();
    }

    private NetworkStatisticsServer _networkStatistics;
}


public class ClientGameLoop : Game.IGameLoop
{
    private enum ClientState
    {
        Connecting,
        Loading,
        Playing,
        Leaving,
    }

    private NetworkClient _networkClient;
    private NetworkStatisticsClient _networkStatisticsClient;

    public bool Init(string[] args) {
        _networkClient = new NetworkClient(new ClientPhotonNetworkTransport());
        _networkStatisticsClient = new NetworkStatisticsClient(_networkClient);
        _networkClient.Connect();

        return true;
    }

    public void Update() {
        _networkClient.Update();

        _networkClient.SendData();
        _networkStatisticsClient.Update();
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
    }

    public void Shutdown() {
        _networkClient.Disconnect();
    }
}


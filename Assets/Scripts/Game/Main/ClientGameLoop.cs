public class ClientGameLoop : Game.IGameLoop, INetworkClientCallbacks
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
        _networkClient.Update(this);

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

    public void OnMapUpdate(ref NetworkReader reader) {
        GameDebug.Log("map : " + reader.ReadString());
        //m_LevelName = data.ReadString();
        //if (m_StateMachine.CurrentState() != ClientState.Loading)
        //    m_StateMachine.SwitchTo(ClientState.Loading);
    }

    public void OnConnect(int clientId) {}

    public void OnDisconnect(int clientId) {}
}


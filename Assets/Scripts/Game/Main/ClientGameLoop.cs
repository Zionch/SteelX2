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

    private StateMachine<ClientState> _stateMachine;

    public bool Init(string[] args) {

        _stateMachine = new StateMachine<ClientState>();
        _stateMachine.Add(ClientState.Connecting, EnterConnectingState, UpdateConnectingState, null);
        _stateMachine.Add(ClientState.Loading, EnterLoadingState, UpdateLoadingState, null);

        _networkClient = new NetworkClient(new ClientPhotonNetworkTransport());
        _networkStatisticsClient = new NetworkStatisticsClient(_networkClient);

        _stateMachine.SwitchTo(ClientState.Connecting);

        return true;
    }

    private double timeout;

    private void EnterConnectingState() {
        timeout = Game.frameTime;
        _networkClient.Connect();
    }
    
    private void UpdateConnectingState() {
        if (_networkClient.IsConnected) {
            _stateMachine.SwitchTo(ClientState.Loading);
        }else if(Game.frameTime - timeout > 10) {
            GameDebug.Log("Client timeout. Leaving.");
            _stateMachine.SwitchTo(ClientState.Leaving);
        }        
    }

    private void EnterLoadingState() {

    }

    private void UpdateLoadingState() {

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
        if (_stateMachine.CurrentState() != ClientState.Loading)
            _stateMachine.SwitchTo(ClientState.Loading);
    }

    public void OnConnect(int clientId) {}

    public void OnDisconnect(int clientId) {}
}


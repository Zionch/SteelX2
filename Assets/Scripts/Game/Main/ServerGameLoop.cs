
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
    private StateMachine<ServerState> _stateMachine;

    public bool Init(string[] args) {
        _networkServer = new NetworkServer(new ServerPhotonNetworkTransport());
        _serverGameWorld = new ServerGameWorld();
        _networkStatistics = new NetworkStatisticsServer(_networkServer);

        _stateMachine = new StateMachine<ServerState>();
        _stateMachine.Add(ServerState.Connecting, EnterConnectingState, UpdateConnectingState, null);
        _stateMachine.Add(ServerState.Loading, EnterLoadingState, UpdateLoadingState, null);
        _stateMachine.Add(ServerState.Active, EnterActiveState, UpdateActiveState, null);

        _stateMachine.SwitchTo(ServerState.Connecting);

        return true;
    }

    private void EnterConnectingState() {
        _networkServer.Connect();
    }

    private void UpdateConnectingState() {
        if (_networkServer.IsConnected) {
            _stateMachine.SwitchTo(ServerState.Loading);
        }
    }

    private void EnterLoadingState() {

    }

    private void UpdateLoadingState() {
        //todo : wait map ready

        _stateMachine.SwitchTo(ServerState.Active);
    }

    private void EnterActiveState() {
        _networkServer.InitializeMap((ref NetworkWriter data) => {
            data.WriteString("name", "testscene");
        });
    }

    private void UpdateActiveState() {

    }

    public void Update() {
        _stateMachine.Update();

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


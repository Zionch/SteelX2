using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ClientGameWorld{
    
    public GameTime PredictedTime
    {
        get { return m_PredictedTime; }
    }

    public GameTime RenderTime
    {
        get { return m_RenderTime; }
    }

    public ClientGameWorld(GameWorld world, NetworkClient networkClient, NetworkStatisticsClient _networkStatistics) {
        _gameWorld = world;
        _networkClient = networkClient;
        this._networkStatistics = _networkStatistics;
    }

    // This is called at the actual client frame rate, so may be faster or slower than tickrate.
    public void Update(float frameDuration) {
        // Advances time and accumulate input into the UserCommand being generated
        HandleTime(frameDuration);
        _gameWorld.worldTime = m_RenderTime;
        _gameWorld.frameDuration = frameDuration;
        _gameWorld.lastServerTick = _networkClient.serverTime;

        //todo: Prediction
    }

    public void LateUpdate(float delta) {

    }

    private void HandleTime(float frameDuration) {
        // Update tick rate (this will only change runtime in test scenarios)
        // TODO consider use ConfigVars with Server flag for this
        if (_networkClient.serverTickRate != m_PredictedTime.tickRate) {
            m_PredictedTime.tickRate = _networkClient.serverTickRate;
            m_RenderTime.tickRate = _networkClient.serverTickRate;
        }

        int prevTick = m_PredictedTime.tick;

        // Increment time
        var deltaPredictedTime = frameDuration * frameTimeScale;
        m_PredictedTime.AddDuration(deltaPredictedTime);

        // Adjust time to be synchronized with server
        int preferredBufferedCommandCount = 2;
        int preferredTick = _networkClient.serverTime + (int)(((_networkClient.timeSinceSnapshot + _networkStatistics.rtt.average) / 1000.0f) * _gameWorld.worldTime.tickRate) + preferredBufferedCommandCount;
        
        bool resetTime = false;
        if (!resetTime && m_PredictedTime.tick < preferredTick - 3) {
            GameDebug.Log(string.Format("Client hard catchup ... "));
            resetTime = true;
        }

        if (!resetTime && m_PredictedTime.tick > preferredTick + 6) {
            GameDebug.Log(string.Format("Client hard slowdown ... "));
            resetTime = true;
        }
        frameTimeScale = 1.0f;

        if (resetTime) {
            GameDebug.Log(string.Format("CATCHUP ({0} -> {1})", m_PredictedTime.tick, preferredTick));

            _networkStatistics.notifyHardCatchup = true;
            m_PredictedTime.tick = preferredTick;
            m_PredictedTime.SetTime(preferredTick, 0);

        } else {
            //int bufferedCommands = m_NetworkClient.lastAcknowlegdedCommandTime - m_NetworkClient.serverTime;
            //if (bufferedCommands < preferredBufferedCommandCount)
            //    frameTimeScale = 1.01f;

            //if (bufferedCommands > preferredBufferedCommandCount)
            //    frameTimeScale = 0.99f;
        }

        // Increment interpolation time
        m_RenderTime.AddDuration(frameDuration * frameTimeScale);

        // Force interp time to not exeede server time
        if (m_RenderTime.tick >= _networkClient.serverTime) {
            m_RenderTime.SetTime(_networkClient.serverTime, 0);
        }

        // hard catchup
        if (m_RenderTime.tick < _networkClient.serverTime - 10) {
            m_RenderTime.SetTime(_networkClient.serverTime - 8, 0);
        }

        // Throttle up to catch up
        if (m_RenderTime.tick < _networkClient.serverTime - 1) {
            m_RenderTime.AddDuration(frameDuration * 0.01f);
        }

        // If predicted time has entered a new tick the stored commands should be sent to server 
        if (m_PredictedTime.tick > prevTick) {
            //var oldestCommandToSend = Mathf.Max(prevTick, m_PredictedTime.tick - NetworkConfig.commandClientBufferSize);
            //for (int tick = oldestCommandToSend; tick < m_PredictedTime.tick; tick++) {
            //    m_PlayerModule.StoreCommand(tick);
            //    m_PlayerModule.SendCommand(tick);
            //}

            //m_PlayerModule.ResetInput(userInputEnabled);
            //m_PlayerModule.StoreCommand(m_PredictedTime.tick);
        }

        // Store command
        //m_PlayerModule.StoreCommand(m_PredictedTime.tick);
    }

    public void Shutdown() {

    }

    public float frameTimeScale = 1.0f;
    private GameTime m_RenderTime = new GameTime(60);
    private GameTime m_PredictedTime = new GameTime(60);

    private GameWorld _gameWorld;
    private NetworkClient _networkClient;
    private NetworkStatisticsClient _networkStatistics;
}

public class ClientGameLoop : Game.IGameLoop, INetworkClientCallbacks, ISnapshotConsumer
{
    private enum ClientState
    {
        Connecting,
        Loading,
        Playing,
        Leaving,
    }

    private GameWorld _gameWorld;
    private NetworkClient _networkClient;
    private NetworkStatisticsClient _networkStatisticsClient;

    private StateMachine<ClientState> _stateMachine;
    private ClientGameWorld _clientGameWorld;

    public bool Init(string[] args) {
        _stateMachine = new StateMachine<ClientState>();
        _stateMachine.Add(ClientState.Connecting, EnterConnectingState, UpdateConnectingState, null);
        _stateMachine.Add(ClientState.Loading, EnterLoadingState, UpdateLoadingState, null);
        _stateMachine.Add(ClientState.Playing, EnterPlayingState, UpdatePlayingState, null);
        _stateMachine.Add(ClientState.Leaving, EnterLeavingState, UpdateLeavingState, null);

        _gameWorld = new GameWorld("ClientWorld");
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
            //wait map update
            timeout = Game.frameTime;
        } else if(Game.frameTime - timeout > 10) {//Todo : fix this constant
            GameDebug.Log("Client timeout. Leaving.");
            _stateMachine.SwitchTo(ClientState.Leaving);
        }        
    }

    private void EnterLoadingState() {
        SceneManager.sceneLoaded += OnSceneLoaded;

        SceneManager.LoadScene(m_LevelName, LoadSceneMode.Additive);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        _stateMachine.SwitchTo(ClientState.Playing);

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void UpdateLoadingState() {
    }

    private void EnterPlayingState() {
        _clientGameWorld = new ClientGameWorld(_gameWorld, _networkClient, _networkStatisticsClient);

        _networkClient.QueueEvent((ushort)GameNetworkEvents.EventType.PlayerReady, true, (ref NetworkWriter data) => { });
    }

    private void UpdatePlayingState() {
        // Handle disconnects
        if (!_networkClient.IsConnected) {
            _stateMachine.SwitchTo(ClientState.Leaving);
            return;
        }

        float frameDuration = m_lastFrameTime != 0 ? (float)(Game.frameTime - m_lastFrameTime) : 0;
        m_lastFrameTime = Game.frameTime;

        _clientGameWorld.Update(frameDuration);
        m_performGameWorldLateUpdate = true;
    }

    private void LeavePlayingState() {
        _clientGameWorld.Shutdown();
    }

    private void EnterLeavingState() {

    }

    private void UpdateLeavingState() {

    }

    public void Update() {
        _networkClient.Update(this, this);

        _networkClient.SendData();
        _networkStatisticsClient.Update();

        if (_clientGameWorld != null)
            _networkStatisticsClient.Update();

        _stateMachine.Update();
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
        if (_gameWorld != null && m_performGameWorldLateUpdate) {
            m_performGameWorldLateUpdate = false;
            _clientGameWorld.LateUpdate(Time.deltaTime);
        }
    }

    public void Shutdown() {
        _networkClient.Disconnect();

        _gameWorld.Shutdown();
    }

    public void OnMapUpdate(ref NetworkReader reader) {
        m_LevelName = reader.ReadString();
        GameDebug.Log("map : " + m_LevelName);
        if (_stateMachine.CurrentState() != ClientState.Loading)//in case map update when loading
            _stateMachine.SwitchTo(ClientState.Loading);
    }

    public void OnEvent(int clientId, NetworkEvent info) {
    }

    public void OnConnect(int clientId) {}

    public void OnDisconnect(int clientId) {}

    public void ProcessEntityDespawns(int serverTime, List<int> despawns) {
    }

    public void ProcessEntitySpawn(int serverTime, int id, ushort typeId) {
    }

    public void ProcessEntityUpdate(int serverTime, int id, ref NetworkReader reader) {
    }

    private string m_LevelName;
    private bool m_performGameWorldLateUpdate;
    private double m_lastFrameTime;
}


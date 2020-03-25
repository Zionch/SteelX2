using UnityEngine;
using UnityEngine.Profiling;

public class ServerGameWorld : ISnapshotGenerator
{
    public int WorldTick { get { return _gameWorld.worldTime.tick; } }
    public int TickRate {
        get {
            return _gameWorld.worldTime.tickRate;
        }
        set {
            _gameWorld.worldTime.tickRate = value;
        }
    }
    public float TickInterval { get { return _gameWorld.worldTime.tickInterval; } }

    public ServerGameWorld(GameWorld world, BundledResourceManager resourceSystem, NetworkServer networkServer) {
        _gameWorld = world;
        _networkServer = networkServer;

        m_ReplicatedEntityModule = new ReplicatedEntityModuleServer(_gameWorld, resourceSystem, networkServer);
        m_ReplicatedEntityModule.ReserveSceneEntities(networkServer);
    }

    public void Update() {

    }

    public void LateUpdate() {

    }

    public void ServerTickUpdate() {
        _gameWorld.worldTime.tick++;
        _gameWorld.worldTime.tickDuration = _gameWorld.worldTime.tickInterval;
        _gameWorld.frameDuration = _gameWorld.worldTime.tickInterval;

        m_ReplicatedEntityModule.HandleSpawning();



        m_ReplicatedEntityModule.HandleDespawning();
    }

    public void HandlePlayerConnect(int connectionId) {

    }

    public void HandlePlayerDisconnect(int connectionId) {

    }

    public void Shutdown() {
        m_ReplicatedEntityModule.Shutdown();
    }

    public void GenerateEntitySnapshot(int entityId, ref NetworkWriter writer) {
        Profiler.BeginSample("ServerGameLoop.GenerateEntitySnapshot()");

        m_ReplicatedEntityModule.GenerateEntitySnapshot(entityId, ref writer);

        Profiler.EndSample();
    }

    public string GenerateEntityName(int entityId) {
        return m_ReplicatedEntityModule.GenerateName(entityId);
    }

    private GameWorld _gameWorld;
    private NetworkServer _networkServer;
    readonly ReplicatedEntityModuleServer m_ReplicatedEntityModule;
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
    private GameWorld _gameWorld;
    private StateMachine<ServerState> _stateMachine;

    public bool Init(string[] args) {
        _gameWorld = new GameWorld("ServerWorld");
        _networkServer = new NetworkServer(new ServerPhotonNetworkTransport());
        _networkStatistics = new NetworkStatisticsServer(_networkServer);

        _stateMachine = new StateMachine<ServerState>();
        _stateMachine.Add(ServerState.Connecting, EnterConnectingState, UpdateConnectingState, null);
        _stateMachine.Add(ServerState.Loading, EnterLoadingState, UpdateLoadingState, null);
        _stateMachine.Add(ServerState.Active, EnterActiveState, UpdateActiveState, LeaveActiveState);

        _networkServer.UpdateClientInfo();

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
        GameDebug.Assert(_serverGameWorld == null);

        m_resourceSystem = new BundledResourceManager(_gameWorld, "BundledResources/Server");

        _serverGameWorld = new ServerGameWorld(_gameWorld, m_resourceSystem, _networkServer);

        _networkServer.InitializeMap((ref NetworkWriter data) => {
            data.WriteString("name", "testscene");
        });

        m_nextTickTime = Game.frameTime;
    }

    private void UpdateActiveState() {
        int tickCount = 0;
        while (Game.frameTime > m_nextTickTime) {
            tickCount++;
            _serverGameWorld.ServerTickUpdate();

            Profiler.BeginSample("GenerateSnapshots");
            _networkServer.GenerateSnapshot(_serverGameWorld, m_LastSimTime);
            Profiler.EndSample();

            m_nextTickTime += _serverGameWorld.TickInterval;
            m_performLateUpdate = true;
        }

        //
        // If running as headless we nudge the Application.targetFramerate back and forth
        // around the actual framerate -- always trying to have a remaining time of half a frame
        // The goal is to have the while loop above tick exactly 1 time
        //
        // The reason for using targetFramerate is to allow Unity to sleep between frames
        // reducing cpu usage on server.
        //
        if (Game.IsHeadless) {
            float remainTime = (float)(m_nextTickTime - Game.frameTime);

            int rate = _serverGameWorld.TickRate;
            if (remainTime > 0.75f * _serverGameWorld.TickInterval)
                rate -= 2;
            else if (remainTime < 0.25f * _serverGameWorld.TickInterval)
                rate += 2;

            Application.targetFrameRate = rate;

            //
            // Show some stats about how many world ticks per unity update we have been running
            //
            //if (debugServerTickStats.IntValue > 0) {
            //    if (Time.frameCount % 10 == 0)
            //        GameDebug.Log(remainTime + ":" + rate);

            //    if (!m_TickStats.ContainsKey(tickCount))
            //        m_TickStats[tickCount] = 0;
            //    m_TickStats[tickCount] = m_TickStats[tickCount] + 1;
            //    if (Time.frameCount % 100 == 0) {
            //        foreach (var p in m_TickStats) {
            //            GameDebug.Log(p.Key + ":" + p.Value);
            //        }
            //    }
            //}
        }
    }

    private void LeaveActiveState() {
        m_resourceSystem.Shutdown();
    }

    public void Update() {
        m_SimStartTime = Game.Instance.Clock.ElapsedTicks;
        m_SimStartTimeTick = _serverGameWorld != null ? _serverGameWorld.WorldTick : 0;

        UpdateNetwork();

        _stateMachine.Update();

        _networkServer.Update(this);
        _networkServer.SendData();
        _networkStatistics.Update();
    }

    private void UpdateNetwork() {
        // If serverTickrate was changed, update both game world and info
        if ((ConfigVar.DirtyFlags & ConfigVar.Flags.ServerInfo) == ConfigVar.Flags.ServerInfo) {
            _networkServer.UpdateClientInfo();
            ConfigVar.DirtyFlags &= ~ConfigVar.Flags.ServerInfo;
        }

        if (_serverGameWorld != null && _serverGameWorld.TickRate != Game.serverTickRate.IntValue)
            _serverGameWorld.TickRate = Game.serverTickRate.IntValue;
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
        if (_serverGameWorld != null && m_SimStartTimeTick != _serverGameWorld.WorldTick) {
            // Only update sim time if we actually simulatated
            // TODO : remove this when targetFrameRate works the way we want it.
            m_LastSimTime = Game.Instance.Clock.GetTicksDeltaAsMilliseconds(m_SimStartTime);
        }

        if (m_performLateUpdate) {
            _serverGameWorld.LateUpdate();
            m_performLateUpdate = false;
        }
    }

    public void Shutdown() {
        _networkServer.Shutdown();
        _serverGameWorld.Shutdown();

        _gameWorld.Shutdown();
        _gameWorld = null;
    }

    public void OnConnect(int clientId) {
        if(_serverGameWorld != null)
            _serverGameWorld.HandlePlayerConnect(clientId);
    }

    public void OnDisconnect(int clientId) {
        if (_serverGameWorld != null)
            _serverGameWorld.HandlePlayerDisconnect(clientId);
    }

    unsafe public void OnEvent(int clientId, NetworkEvent info) {
        var type = info.type.typeId;
        fixed (uint* data = info.data) {
            var reader = new NetworkReader(data, info.type.schema);

            switch ((GameNetworkEvents.EventType)type) {
                case GameNetworkEvents.EventType.PlayerReady:
                _networkServer.MapReady(clientId); // TODO hacky
                //client.isReady = true;
                break;

                //case GameNetworkEvents.EventType.PlayerSetup:
                //client.playerSettings.Deserialize(ref reader);
                //if (client.player != null)
                //    _serverGameWorld.HandlePlayerSetupEvent(client.player, client.playerSettings);
                //break;

                //case GameNetworkEvents.EventType.RemoteConsoleCmd:
                //HandleClientCommand(client, reader.ReadString());
                //break;
                //case GameNetworkEvents.EventType.Chat:
                //m_ChatSystem.ReceiveMessage(client, reader.ReadString(256));
                //break;
            }
        }
    }

    BundledResourceManager m_resourceSystem;
    private NetworkStatisticsServer _networkStatistics;

    public double m_nextTickTime = 0;
    
    long m_SimStartTime;
    int m_SimStartTimeTick;
    private bool m_performLateUpdate;
    private float m_LastSimTime;
}


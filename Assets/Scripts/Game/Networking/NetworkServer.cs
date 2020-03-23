using NetworkCompression;
using Photon.Pun;
using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

public interface ISnapshotGenerator
{
    int WorldTick { get; }
    //void GenerateEntitySnapshot(int entityId, ref NetworkWriter writer);
    //string GenerateEntityName(int entityId);
}

public class NetworkServer
{
    [ConfigVar(Name = "server.debug", DefaultValue = "0", Description = "Enable debug printing of server handshake etc.", Flags = ConfigVar.Flags.None)]
    public static ConfigVar serverDebug;

    public delegate void DataGenerator(ref NetworkWriter writer);

    [ConfigVar(Name = "server.network_prediction", DefaultValue = "1", Description = "Predict snapshots data to improve compression and minimize bandwidth")]
    public static ConfigVar network_prediction;

    // Each client needs to receive this on connect and when any of the values changes
    public class ServerInfo
    {
        public int ServerTickRate;
    }

    private enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    public class ServerPackageInfo : PackageInfo
    {
        public int serverSequence;
        public int serverTime;
    }

    public class Counters : NetworkConnectionCounters
    {
        public int snapshotsOut;
        public int commandsIn;
    }

    private unsafe class MapInfo
    {
        public int serverInitSequence;                  // The server frame the map was initialized
        public ushort mapId;                            // Unique sequence number for the map (to deal with redudant mapinfo messages)
        public NetworkSchema schema;                    // Schema for the map info
        public uint* data = (uint*)UnsafeUtility.Malloc(1024, UnsafeUtility.AlignOf<uint>(), Unity.Collections.Allocator.Persistent);            // Game specific payload
    }

    // Each tick a WorldSnapshot is generated. The data buffer contains serialized data
    // from all serializable entitites
    unsafe class WorldSnapshot
    {
        public int serverTime;  // server tick for this snapshot
        public int length;      // length of data in data field
        public uint* data;
    }

    private ConnectionState _connectionState = ConnectionState.Disconnected;

    private INetworkTransport _transport;
    private ServerConnection _serverConnection;

    //the game time on the server
    public int serverTime { get; private set; }
    public ServerInfo serverInfo;

    public bool IsConnected { get { return _connectionState == ConnectionState.Connected; } }

    unsafe public NetworkServer(INetworkTransport transport) {
        _transport = transport;

        serverInfo = new ServerInfo();
        // Allocate array to hold world snapshots
        m_Snapshots = new WorldSnapshot[NetworkConfig.snapshotDeltaCacheSize];
        for (int i = 0; i < m_Snapshots.Length; ++i) {
            m_Snapshots[i] = new WorldSnapshot();
            m_Snapshots[i].data = (uint*)UnsafeUtility.Malloc(NetworkConfig.maxWorldSnapshotDataSize, UnsafeUtility.AlignOf<UInt32>(), Unity.Collections.Allocator.Persistent);
        }
    }

    public void Disconnect() {
        _transport.Disconnect();
    }

    public void Connect() {
        GameDebug.Assert(_connectionState == ConnectionState.Disconnected);
        GameDebug.Assert(_serverConnection == null);

        _transport.Connect();

        _connectionState = ConnectionState.Connecting;
    }

    unsafe public void InitializeMap(DataGenerator generator) {
        // Generate schema the first time we set map info
        bool generateSchema = false;
        if (m_MapInfo.schema == null) {
            m_MapInfo.schema = new NetworkSchema(NetworkConfig.mapSchemaId);
            generateSchema = true;
        }

        // Update map info
        var writer = new NetworkWriter(m_MapInfo.data, 1024, m_MapInfo.schema, generateSchema);
        generator(ref writer);
        writer.Flush();

        m_MapInfo.serverInitSequence = m_ServerSequence;
        ++m_MapInfo.mapId;

        // Reset map and connection state
        //serverTime = 0;
        //m_Entities.Clear();
        //m_FreeEntities.Clear();
        foreach (var pair in _serverConnections)
            pair.Value.Reset();
    }

    public void MapReady(int clientId) {
        GameDebug.Log("Client " + clientId + " is ready");
        if (!_serverConnections.ContainsKey(clientId)) {
            GameDebug.Log("Got MapReady from unknown client?");
            return;
        }
        _serverConnections[clientId].mapReady = true;
    }

    unsafe public void GenerateSnapshot(ISnapshotGenerator snapshotGenerator, float simTime) {
        var time = snapshotGenerator.WorldTick;
        GameDebug.Assert(time > serverTime);      // Time should always flow forward
        GameDebug.Assert(m_MapInfo.mapId > 0);    // Initialize map before generating snapshot

        ++m_ServerSequence;

        // We currently keep entities around until every client has ack'ed the snapshot with the despawn
        // Then we delete them from our list and recycle the id
        // TODO: we do not need this anymore?

        // Find oldest (smallest seq no) acked snapshot.
        var minClientAck = int.MaxValue;
        foreach (var pair in _serverConnections) {
            var c = pair.Value;
            // If a client is so far behind that we have to send non-baseline updates to it
            // there is no reason to keep despawned entities around for this clients sake
            if (m_ServerSequence - c.maxSnapshotAck >= NetworkConfig.snapshotDeltaCacheSize - 2) // -2 because we want 3 baselines!
                continue;
            var acked = c.maxSnapshotAck;
            if (acked < minClientAck)
                minClientAck = acked;
        }

        // Recycle despawned entities that have been acked by all
        //for (int i = 0; i < m_Entities.Count; i++) {
        //    var e = m_Entities[i];
        //    if (e.despawnSequence > 0 && e.despawnSequence < minClientAck) {
        //        if (serverDebugEntityIds.IntValue > 1)
        //            GameDebug.Log("Recycling entity id: " + i + " because despawned in " + e.despawnSequence + " and minAck is now " + minClientAck);
        //        e.Reset();
        //        m_FreeEntities.Add(i);
        //    }
        //}

        serverTime = time;
        //m_ServerSimTime = simTime;

        //m_LastEntityCount = 0;

        // Grab world snapshot from circular buffer
        var worldsnapshot = m_Snapshots[m_ServerSequence % m_Snapshots.Length];
        worldsnapshot.serverTime = time;
        worldsnapshot.length = 0;
    }

    public void Update(INetworkCallbacks loop) {
        _transport.Update();

        TransportEvent e = new TransportEvent();
        while (_transport.NextEvent(ref e)) {
            switch (e.type) {
                case TransportEvent.Type.Connect:
                OnConnect(e.ConnectionId, loop);
                break;
                case TransportEvent.Type.Disconnect:
                OnDisconnect(e.ConnectionId, loop);
                break;
                case TransportEvent.Type.Data:
                OnData(e.ConnectionId, e.Data, loop);
                break;
            }
        }
    }

    public void UpdateClientInfo() {
        serverInfo.ServerTickRate = Game.serverTickRate.IntValue;

        foreach (var pair in _serverConnections)
            pair.Value.clientInfoAcked = false;
    }

    public void SendData() {
        foreach(var connection in _serverConnections.Values) {
            connection.SendPackage();
        }
    }

    public void OnData(int connectionId, byte[] data, INetworkCallbacks loop) {
        if (!_serverConnections.ContainsKey(connectionId))
            return;

        _serverConnections[connectionId].ReadPackage(data, loop);
    }

    public void OnConnect(int connectionId, INetworkCallbacks loop) {
        if(connectionId == PhotonNetwork.LocalPlayer.ActorNumber) {
            _connectionState = ConnectionState.Connected;
            return;
        }
        GameDebug.Log($"Player {connectionId} is connected");

        if (!_serverConnections.ContainsKey(connectionId)) {
            _serverConnections.Add(connectionId, new ServerConnection(this, connectionId, _transport));
        }
    }

    public void OnDisconnect(int connectionId, INetworkCallbacks loop) {
        if (connectionId == PhotonNetwork.LocalPlayer.ActorNumber) {
            _connectionState = ConnectionState.Disconnected;
            return;
        }

        GameDebug.Log($"Player {connectionId} is disconnected");

        if (_serverConnections.ContainsKey(connectionId))
            _serverConnections.Remove(connectionId);
    }

    public void Shutdown() {
        _transport.Shutdown();

        Disconnect();
    }

    public class ServerConnection : NetworkConnection<ServerPackageInfo, NetworkServer.Counters>
    {
        public ServerConnection(NetworkServer server, int connectionId, INetworkTransport transport) : base(connectionId, transport) {
            _server = server;
        }

        public void ReadPackage(byte[] packageData, INetworkCallbacks loop) {
            counters.bytesIn += packageData.Length;

            NetworkMessage content;

            int headerSize;
            var packageSequence = ProcessPackageHeader(packageData, out content, out headerSize);

            var input = new RawInputStream(packageData, headerSize);

            //if ((content & NetworkMessage.ClientConfig) != 0)
            //    ReadClientConfig(ref input);

            if ((content & NetworkMessage.Events) != 0)
                ReadEvents(ref input, loop);
        }

        //public void ReadClientConfig(ref BitInputStream inpuf) {

        //}

        public void SendPackage() {
            var rawOutputStream = new BitOutputStream(m_PackageBuffer);

            ServerPackageInfo packageInfo;
            BeginSendPackage(ref rawOutputStream, out packageInfo);

            int endOfHeaderPos = rawOutputStream.Align();
            var output = new RawOutputStream();// new TOutputStream();  Due to bug new generates garbage here
            output.Initialize(m_PackageBuffer, endOfHeaderPos);

            packageInfo.serverSequence = _server.m_ServerSequence;
            packageInfo.serverTime = _server.serverTime;             // Server time (could be ticks or could be ms)

            // The ifs below are in essence the 'connection handshake' logic.
            if (!clientInfoAcked) {
                // Keep sending client info until it is acked
                WriteClientInfo(ref output);
            }else if (!mapAcked) {
                if (_server.m_MapInfo.serverInitSequence > 0) {
                    // Keep sending map info until it is acked
                    WriteMapInfo(ref output);
                }
            } else {
                // Send snapshot, buf only
                //   if client has declared itself ready
                //   if we have not already sent for this tick (because we need to be able to map a snapshot 
                //     sequence to a package sequence we cannot send the same snapshot multiple times).
                if (mapReady && _server.m_ServerSequence > snapshotServerLastWritten) {
                    WriteSnapshot(ref output);
                }
            }

            int compressedSize = output.Flush();
            rawOutputStream.SkipBytes(compressedSize);

            CompleteSendPackage(packageInfo, ref rawOutputStream);
        }

        public void WriteClientInfo(ref RawOutputStream output) {
            AddMessageContentFlag(NetworkMessage.ClientInfo);

            output.WriteRawBits((uint)ConnectionId, 8);
            output.WriteRawBits((uint)_server.serverInfo.ServerTickRate, 8);
        }

        unsafe private void WriteMapInfo(ref RawOutputStream output){
            AddMessageContentFlag(NetworkMessage.MapInfo);

            output.WriteRawBits(_server.m_MapInfo.mapId, 16);

            // Write schema if client haven't acked it
            output.WriteRawBits(mapSchemaAcked ? 0 : 1U, 1);
            if (!mapSchemaAcked)
                NetworkSchema.WriteSchema(_server.m_MapInfo.schema, ref output);

            // Write map data
            NetworkSchema.CopyFieldsFromBuffer(_server.m_MapInfo.schema, _server.m_MapInfo.data, ref output);
        }

        unsafe void WriteSnapshot(ref RawOutputStream output){
            AddMessageContentFlag(NetworkMessage.Snapshot);

            bool enableNetworkPrediction = network_prediction.IntValue != 0;
            //bool enableHashing = debug_hashing.IntValue != 0;

            // Check if the baseline from the client is too old. We keep N number of snapshots on the server 
            // so if the client baseline is older than that we cannot generate the snapshot. Furthermore, we require
            // the client to keep the last N updates for any entity, so even though the client might have much older
            // baselines for some entities we cannot guarantee it. 
            // TODO : Can we make this simpler?
            var haveBaseline = maxSnapshotAck != 0;
            if (_server.m_ServerSequence - maxSnapshotAck >= NetworkConfig.snapshotDeltaCacheSize - 2) // -2 because we want 3 baselines!
            {
                if (serverDebug.IntValue > 0)
                    GameDebug.Log("ServerSequence ahead of latest ack'ed snapshot by more than cache size. " + (haveBaseline ? "nobaseline" : "baseline"));
                haveBaseline = false;
            }
            var baseline = haveBaseline ? maxSnapshotAck : 0;

            int snapshot0Baseline = baseline;
            int snapshot1Baseline = baseline;
            int snapshot2Baseline = baseline;
            int snapshot0BaselineClient = snapshotPackageBaseline;
            int snapshot1BaselineClient = snapshotPackageBaseline;
            int snapshot2BaselineClient = snapshotPackageBaseline;
            if (enableNetworkPrediction && haveBaseline) {
                var end = snapshotPackageBaseline - NetworkConfig.clientAckCacheSize;
                end = end < 0 ? 0 : end;
                var a = snapshotPackageBaseline - 1;
                while (a > end) {
                    if (snapshotAcks[a % NetworkConfig.clientAckCacheSize]) {
                        var base1 = snapshotSeqs[a % NetworkConfig.clientAckCacheSize];
                        if (_server.m_ServerSequence - base1 < NetworkConfig.snapshotDeltaCacheSize - 2) {
                            snapshot1Baseline = base1;
                            snapshot1BaselineClient = a;
                            snapshot2Baseline = snapshotSeqs[a % NetworkConfig.clientAckCacheSize];
                            snapshot2BaselineClient = a;
                        }
                        break;
                    }
                    a--;
                }
                a--;
                while (a > end) {
                    if (snapshotAcks[a % NetworkConfig.clientAckCacheSize]) {
                        var base2 = snapshotSeqs[a % NetworkConfig.clientAckCacheSize];
                        if (_server.m_ServerSequence - base2 < NetworkConfig.snapshotDeltaCacheSize - 2) {
                            snapshot2Baseline = base2;
                            snapshot2BaselineClient = a;
                        }
                        break;
                    }
                    a--;
                }
            }
            output.WriteRawBits(haveBaseline ? 1u : 0, 1);
            output.WritePackedIntDelta(snapshot0BaselineClient, outSequence - 1, NetworkConfig.baseSequenceContext);
            output.WriteRawBits(enableNetworkPrediction ? 1u : 0u, 1);
            //output.WriteRawBits(enableHashing ? 1u : 0u, 1);
            if (enableNetworkPrediction) {
                output.WritePackedIntDelta(haveBaseline ? snapshot1BaselineClient : 0, snapshot0BaselineClient - 1, NetworkConfig.baseSequence1Context);
                output.WritePackedIntDelta(haveBaseline ? snapshot2BaselineClient : 0, snapshot1BaselineClient - 1, NetworkConfig.baseSequence2Context);
            }

            // NETTODO: For us serverTime == tick but network layer only cares about a growing int
            output.WritePackedIntDelta(_server.serverTime, haveBaseline ? maxSnapshotTime : 0, NetworkConfig.serverTimeContext);
            // NETTODO: a more generic way to send stats
            var temp = _server.m_ServerSimTime * 10;
            output.WriteRawBits((byte)temp, 8);


            snapshotSeqs[outSequence % NetworkConfig.clientAckCacheSize] = _server.m_ServerSequence;
            snapshotServerLastWritten = _server.m_ServerSequence;
        }

        protected override void NotifyDelivered(int sequence, ServerPackageInfo info, bool madeIt) {
            base.NotifyDelivered(sequence, info, madeIt);

            if (madeIt) {
                if ((info.Content & NetworkMessage.ClientInfo) != 0) {
                    clientInfoAcked = true;
                }

                // Check if the client received the map info
                if ((info.Content & NetworkMessage.MapInfo) != 0 && info.serverSequence >= _server.m_MapInfo.serverInitSequence) {
                    mapAcked = true;
                    mapSchemaAcked = true;
                }

                // Update the snapshot baseline if the client received the snapshot
                if (mapAcked && (info.Content & NetworkMessage.Snapshot) != 0) {
                    snapshotPackageBaseline = sequence;

                    GameDebug.Assert(snapshotSeqs[sequence % NetworkConfig.clientAckCacheSize] > 0, "Got ack for package we did not expect?");
                    snapshotAcks[sequence % NetworkConfig.clientAckCacheSize] = true;

                    // Keep track of newest ack'ed snapshot
                    if (info.serverSequence > maxSnapshotAck) {
                        if (maxSnapshotAck == 0 && serverDebug.IntValue > 0)
                            GameDebug.Log("SERVER: first max ack for " + info.serverSequence);
                        maxSnapshotAck = info.serverSequence;
                        maxSnapshotTime = info.serverTime;
                    }
                }
            }
        }

        public void Reset() {
            mapAcked = false;
            mapReady = false;
            maxSnapshotAck = 0;
            maxSnapshotTime = 0;
        }

        // flags for ack of individual snapshots indexed by client sequence
        bool[] snapshotAcks = new bool[NetworkConfig.clientAckCacheSize];
        // corresponding server baseline no for each client seq
        int[] snapshotSeqs = new int[NetworkConfig.clientAckCacheSize];
        public int maxSnapshotAck;
        int maxSnapshotTime;
        private int snapshotPackageBaseline;
        private int snapshotServerLastWritten;

        public bool clientInfoAcked;
        public bool mapReady;

        private bool mapAcked, mapSchemaAcked;
        private NetworkServer _server;
    }

    public Dictionary<int, ServerConnection> GetConnections() {
        return _serverConnections;
    }

    WorldSnapshot[] m_Snapshots;

    int m_ServerSequence = 1;
    float m_ServerSimTime;
    MapInfo m_MapInfo = new MapInfo();
    private Dictionary<int, ServerConnection> _serverConnections = new Dictionary<int, ServerConnection>();
}


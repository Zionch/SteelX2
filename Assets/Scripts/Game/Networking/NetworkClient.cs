using NetworkCompression;
using System.Collections.Generic;
using System.Text;

public interface INetworkClientCallbacks : INetworkCallbacks
{
    void OnMapUpdate(ref NetworkReader reader);
}

public interface ISnapshotConsumer
{
    void ProcessEntityDespawns(int serverTime, List<int> despawns);
    void ProcessEntitySpawn(int serverTime, int id, ushort typeId);
    void ProcessEntityUpdate(int serverTime, int id, ref NetworkReader reader);
}

public class NetworkClient
{
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,//client is connected if received client info
    }
    protected ConnectionState connectionState { get {
        return _clientConnection == null ? ConnectionState.Disconnected : _clientConnection.connectionState;
    }}

    // Sent from client to server when changed
    public class ClientConfig
    {
        public int serverUpdateRate;            // max bytes/sec
        public int serverUpdateInterval;        // requested tick / update
    }

    public class Counters : NetworkConnectionCounters
    {
        public int snapshotsIn;             // Total number of snapshots received
        public int fullSnapshotsIn;         // Number of snapshots without a baseline
        public int commandsOut;             // Number of command messages sent
    }

    private INetworkTransport _transport;
    private ClientConfig _clientConfig;
    public int serverTime { get { return _clientConnection != null ? _clientConnection.serverTime : -1; } }
    public int serverTickRate { get { return _clientConnection != null ? _clientConnection.serverTickRate : 60; } }
    public int rtt { get { return _clientConnection != null ? _clientConnection.rtt : 0; } }
    public float timeSinceSnapshot { get { return _clientConnection != null ? NetworkUtils.stopwatch.ElapsedMilliseconds - _clientConnection.snapshotReceivedTime : -1; } }

    public ClientConnection _clientConnection { get; private set; }
    Dictionary<ushort, NetworkEventType> m_EventTypesOut = new Dictionary<ushort, NetworkEventType>();

    public bool IsConnected { get { return connectionState == ConnectionState.Connected; } }

    public NetworkClient(INetworkTransport transport) {
        _transport = transport;
        _clientConfig = new ClientConfig();
    }

    public void Disconnect() {
        _transport.Disconnect();
    }

    public void Connect() {
        GameDebug.Assert(connectionState == ConnectionState.Disconnected);
        GameDebug.Assert(_clientConnection == null);

        _transport.Connect();

        _clientConnection = new ClientConnection(0, _transport, _clientConfig);//0 is temp. , wait server send our id
    }

    public void Update(INetworkClientCallbacks clientNetworkConsumer, ISnapshotConsumer snapshotConsumer) {
        _transport.Update();

        TransportEvent e = new TransportEvent();
        while(_transport.NextEvent(ref e)) {
            switch (e.type) {
                case TransportEvent.Type.Connect:
                OnConnect(e.ConnectionId);
                break;
                case TransportEvent.Type.Disconnect:
                OnDisconnect(e.ConnectionId);
                break;
                case TransportEvent.Type.Data:
                OnData(e.Data, clientNetworkConsumer, snapshotConsumer);
                break;
            }
        }

        if (_clientConnection != null)
            _clientConnection.ProcessMapUpdate(clientNetworkConsumer);
    }

    public void SendData() {
        if (_clientConnection == null)
            return;

        _clientConnection.SendPackage();
    }

    public void QueueEvent(ushort typeId, bool reliable, NetworkEventGenerator generator) {
        if (_clientConnection == null)
            return;

        var e = NetworkEvent.Serialize(typeId, reliable, m_EventTypesOut, generator);
        _clientConnection.QueueEvent(e);
        e.Release();
    }

    public void OnData(byte[] data, INetworkClientCallbacks clientNetworkConsumer, ISnapshotConsumer snapshotConsumer) {
        if (_clientConnection == null)
            return;

        _clientConnection.ReadPackage(data, snapshotConsumer, clientNetworkConsumer);
    }

    public void OnConnect(int connectionId) {//connect to photon
        GameDebug.Assert(connectionState == ConnectionState.Connecting);
    }

    public void OnDisconnect(int connectionId) {
        if (_clientConnection == null) return;
        
        GameDebug.Log("Disconnected");
        _clientConnection = null;
    }

    public class ClientConnection : NetworkConnection<PackageInfo, NetworkClient.Counters>
    {
        public ConnectionState connectionState = ConnectionState.Connecting;
        private ClientConfig _clientConfig;

        public long snapshotReceivedTime;               // Time we received the last snapshot
        public float serverSimTime;                     // Server simulation time (actualy time spent doing simulation regardless of tickrate)

        public ClientConnection(int connectionId, INetworkTransport transport, ClientConfig clientConfig) :  base(connectionId, transport){
        }

        public void Reset() {
            serverTime = 0;
        }

        unsafe public void ProcessMapUpdate(INetworkClientCallbacks loop) {
            if (mapInfo.mapSequence > 0 && !mapInfo.processed) {
                fixed (uint* data = mapInfo.data) {
                    var reader = new NetworkReader(data, mapInfo.schema);
                    loop.OnMapUpdate(ref reader);
                    mapInfo.processed = true;
                }
            }
        }

        public void ReadPackage(byte[] packageData, ISnapshotConsumer snapshotConsumer, INetworkCallbacks networkClientConsumer) {
            counters.bytesIn += packageData.Length;

            NetworkMessage content;
            int headerSize;
            var packageSequence = ProcessPackageHeader(packageData, out content, out headerSize);
            // The package was dropped (duplicate or too old) or if it was a fragment not yet assembled, bail out here
            if (packageSequence == 0) {
                return;
            }

            var input = new RawInputStream();
            input.Initialize(packageData, headerSize);

            if ((content & NetworkMessage.ClientInfo) != 0)
                ReadClientInfo(ref input);

            if ((content & NetworkMessage.MapInfo) != 0)
                ReadMapInfo(ref input);

            if ((content & NetworkMessage.Snapshot) != 0) {
                ReadSnapshot(packageSequence, ref input, snapshotConsumer);

                // Make sure the callback actually picked up the snapshot data. It is important that
                // every snapshot gets processed by the game so that the spawns, despawns and updates lists
                // does not end up containing stuff from different snapshots
                //GameDebug.Assert(spawns.Count == 0 && despawns.Count == 0 && updates.Count == 0, "Game did not consume snapshots");
            }

            if ((content & NetworkMessage.Events) != 0)
                ReadEvents(ref input, networkClientConsumer);
        }

        public void ReadClientInfo(ref RawInputStream input) {
            var newClientId = (int)input.ReadRawBits(8);
            serverTickRate = (int)input.ReadRawBits(8);

            if (connectionState == ConnectionState.Connected) return;

            ConnectionId = newClientId;
            GameDebug.Log("Client received client info");

            connectionState = ConnectionState.Connected;
        }

        void ReadMapInfo(ref RawInputStream input){
            //input.SetStatsType(NetworkCompressionReader.Type.MapInfo);
            var mapSequence = (ushort)input.ReadRawBits(16);
            var schemaIncluded = input.ReadRawBits(1) != 0;
            if (schemaIncluded) {
                mapInfo.schema = NetworkSchema.ReadSchema(ref input);   // might override previous definition
            }

            if (mapSequence > mapInfo.mapSequence) {
                mapInfo.mapSequence = mapSequence;
                mapInfo.ackSequence = inSequence;
                mapInfo.processed = false;
                NetworkSchema.CopyFieldsToBuffer(mapInfo.schema, ref input, mapInfo.data);
            } else
                NetworkSchema.SkipFields(mapInfo.schema, ref input);
        }

        unsafe void ReadSnapshot(int sequence, ref RawInputStream input, ISnapshotConsumer consumer) {
            var haveBaseline = input.ReadRawBits(1) == 1;
            var baseSequence = (int)input.ReadPackedIntDelta(sequence - 1, NetworkConfig.baseSequenceContext);

            bool enableNetworkPrediction = input.ReadRawBits(1) != 0;

            int baseSequence1 = 0;
            int baseSequence2 = 0;
            if (enableNetworkPrediction) {
                baseSequence1 = (int)input.ReadPackedIntDelta(baseSequence - 1, NetworkConfig.baseSequence1Context);
                baseSequence2 = (int)input.ReadPackedIntDelta(baseSequence1 - 1, NetworkConfig.baseSequence2Context);
            }

            var snapshotInfo = snapshots.Acquire(sequence);
            snapshotInfo.serverTime = (int)input.ReadPackedIntDelta(haveBaseline ? snapshots[baseSequence].serverTime : 0, NetworkConfig.serverTimeContext);
            //GameDebug.Log("baseSequence : " + baseSequence + "server time:" + (haveBaseline ? snapshots[baseSequence].serverTime.ToString() : ""));

            var temp = (int)input.ReadRawBits(8);
            serverSimTime = temp * 0.1f;

            // Only update time if received in-order.. 
            // TODO consider dropping out of order snapshots
            // TODO detecting out-of-order on pack sequences
            if (snapshotInfo.serverTime > serverTime) {
                serverTime = snapshotInfo.serverTime;
                snapshotReceivedTime = NetworkUtils.stopwatch.ElapsedMilliseconds;
            } else {
                GameDebug.Log(string.Format("NetworkClient. Dropping out of order snaphot. Server time:{0} snapshot time:{1}", serverTime, snapshotInfo.serverTime));
            }

            // Read schemas

            // Read new spawns

            // Read despawns

            // If we have no baseline, we need to clear all entities that are not being spawned
        }

        public void SendPackage() {
            if (connectionState != ConnectionState.Connected)//do not send anything until we receive client info
                return;

            var rawOutputStream = new BitOutputStream(m_PackageBuffer);

            // todo : only if there is anything to send

            PackageInfo info;
            BeginSendPackage(ref rawOutputStream, out info);

            int endOfHeaderPos = rawOutputStream.Align();
            var output = default(RawOutputStream);
            output.Initialize(m_PackageBuffer, endOfHeaderPos);

            WriteEvents(info, ref output);
            int compressedSize = output.Flush();
            rawOutputStream.SkipBytes(compressedSize);

            CompleteSendPackage(info, ref rawOutputStream);
        }

        class MapInfo
        {
            public bool processed;                  // Map reset was processed by game
            public ushort mapSequence;              // map identifier to discard duplicate messages
            public int ackSequence;                 // package sequence the map was acked in (discard packages before this)
            public NetworkSchema schema;            // Schema for the map info
            public uint[] data = new uint[256];     // Game specific map info payload
        }

        class SnapshotInfo
        {
            public int serverTime;
        }

        SequenceBuffer<SnapshotInfo> snapshots = new SequenceBuffer<SnapshotInfo>(NetworkConfig.snapshotDeltaCacheSize, () => new SnapshotInfo());

        public int serverTickRate;
        public int serverTime;
        private MapInfo mapInfo = new MapInfo();
    }
}

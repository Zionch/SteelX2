using NetworkCompression;
using Photon.Pun;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

public class NetworkServer
{
    public delegate void DataGenerator(ref NetworkWriter writer);

    private enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    public class ServerPackageInfo : PackageInfo
    {
        public int serverSequence;
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

    private ConnectionState _connectionState = ConnectionState.Disconnected;

    private INetworkTransport _transport;
    private ServerConnection _serverConnection;

    public bool IsConnected { get { return _connectionState == ConnectionState.Connected; } }

    public NetworkServer(INetworkTransport transport) {
        _transport = transport;
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

    public void SendData() {
        foreach(var connection in _serverConnections.Values) {
            connection.SendPackage();
        }
    }

    public void OnData(int connectionId, byte[] data, INetworkCallbacks loop) {
        if (!_serverConnections.ContainsKey(connectionId))
            return;

        _serverConnections[connectionId].ReadPackage(data);
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

        public void ReadPackage(byte[] packageData) {
            counters.bytesIn += packageData.Length;

            NetworkMessage content;

            int headerSize;
            var packageSequence = ProcessPackageHeader(packageData, out content, out headerSize);

            var input = new RawInputStream(packageData, headerSize);

            //if ((content & NetworkMessage.ClientConfig) != 0)
            //    ReadClientConfig(ref input);
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

            // The ifs below are in essence the 'connection handshake' logic.
            if (!clientInfoAcked) {
                // Keep sending client info until it is acked
                WriteClientInfo(ref output);
            }else if (!mapAcked) {
                if (_server.m_MapInfo.serverInitSequence > 0) {
                    // Keep sending map info until it is acked
                    WriteMapInfo(ref output);
                }
            }

            int compressedSize = output.Flush();
            rawOutputStream.SkipBytes(compressedSize);

            CompleteSendPackage(packageInfo, ref rawOutputStream);
        }

        public void WriteClientInfo(ref RawOutputStream output) {
            AddMessageContentFlag(NetworkMessage.ClientInfo);
            output.WriteRawBits((uint)ConnectionId, 8);
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

        protected override void NotifyDelivered(int sequence, ServerPackageInfo info, bool madeIt) {
            base.NotifyDelivered(sequence, info, madeIt);

            if (madeIt) {
                if ((info.Content & NetworkMessage.ClientInfo) != 0) {
                    clientInfoAcked = true;
                    GameDebug.Log("client acked client info");
                }

                // Check if the client received the map info
                if ((info.Content & NetworkMessage.MapInfo) != 0 && info.serverSequence >= _server.m_MapInfo.serverInitSequence) {
                    GameDebug.Log("client acked map info");
                    mapAcked = true;
                    mapSchemaAcked = true;
                }
            }
        }

        public void Reset() {
            mapAcked = false;
        }

        private bool mapAcked, mapSchemaAcked;
        private bool clientInfoAcked;
        NetworkServer _server;
    }

    public Dictionary<int, ServerConnection> GetConnections() {
        return _serverConnections;
    }

    int m_ServerSequence = 1;
    MapInfo m_MapInfo = new MapInfo();
    private Dictionary<int, ServerConnection> _serverConnections = new Dictionary<int, ServerConnection>();
}


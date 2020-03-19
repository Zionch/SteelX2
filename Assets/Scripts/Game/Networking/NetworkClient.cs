
public class NetworkClient
{
    protected enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }
    protected ConnectionState _connectionState = ConnectionState.Disconnected;

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
    private ClientConnection _clientConnection;

    public bool IsConnected { get { return _connectionState == ConnectionState.Connected; } }

    public NetworkClient(INetworkTransport transport) {
        _transport = transport;
        _clientConfig = new ClientConfig();
    }

    public void Disconnect() {
        _transport.Disconnect();
    }

    public void Connect() {
        GameDebug.Assert(_connectionState == ConnectionState.Disconnected);
        GameDebug.Assert(_clientConnection == null);

        _transport.Connect();

        _connectionState = ConnectionState.Connecting;
    }

    public void Update() {
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
                OnData(e.Data);
                break;
            }
        }
    }

    public void SendData() {
        if (_clientConnection == null)
            return;

        _clientConnection.SendPackage();
    }

    public void OnData(byte[] data) {
        if (_clientConnection == null)
            return;

        _clientConnection.ReadPackage(data);
    }

    public void OnConnect(int connectionId) {//connect to photon
        GameDebug.Assert(_connectionState == ConnectionState.Connecting);

        _clientConnection = new ClientConnection(connectionId, _transport, _clientConfig);
    }

    public void OnDisconnect(int connectionId) {
        if (_clientConnection == null) return;

        if(_clientConnection.ConnectionId != connectionId) {
            GameDebug.LogWarning("Receive disconnect event but not towards this player");
            return;
        }
        
        GameDebug.Log("Disconnected");
        _connectionState = ConnectionState.Disconnected;
        _clientConnection = null;
    }

    private class ClientConnection : NetworkConnection<PackageInfo, NetworkClient.Counters>
    {
        private ClientConfig _clientConfig;

        public ClientConnection(int connectionId, INetworkTransport transport, ClientConfig clientConfig) :  base(connectionId, transport){
        }

        public void ReadPackage(byte[] packageData) {
            counters.bytesIn += packageData.Length;

            NetworkMessage content;
            int headerSize;
            var packageSequence = ProcessPackageHeader(packageData, out content, out headerSize);

            // The package was dropped (duplicate or too old) or if it was a fragment not yet assembled, bail out here
            if (packageSequence == 0) {
                return;
            }

            var input = new BitInputStream(packageData);
            input.SkipBytes(headerSize);

            if ((content & NetworkMessage.ClientInfo) != 0)
                ReadClientInfo(ref input);
        }

        public void ReadClientInfo(ref BitInputStream input) {
            var newClientId = (int)input.ReadBits(8);

            if (receiveClientInfo) return;

            GameDebug.Log("Client received client info");

            receiveClientInfo = true;
        }

        public void SendPackage() {
            if (!receiveClientInfo)//do not send anything until we receive client info
                return;

            var rawOutputStream = new BitOutputStream(m_PackageBuffer);

            // todo : only if there is anything to send

            PackageInfo info;
            BeginSendPackage(ref rawOutputStream, out info);

            CompleteSendPackage(info, ref rawOutputStream);
        }

        private bool receiveClientInfo;
    }
}

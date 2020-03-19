using Photon.Pun;
using System.Collections.Generic;

public class NetworkServer
{
    private enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    public class ServerPackageInfo : PackageInfo
    {

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

    public void Update() {
        _transport.Update();

        TransportEvent e = new TransportEvent();
        while (_transport.NextEvent(ref e)) {
            switch (e.type) {
                case TransportEvent.Type.Connect:
                OnConnect(e.ConnectionId);
                break;
                case TransportEvent.Type.Disconnect:
                OnDisconnect(e.ConnectionId);
                break;
                case TransportEvent.Type.Data:
                OnData(e.ConnectionId, e.Data);
                break;
            }
        }
    }

    public void SendData() {
        foreach(var connection in _serverConnections.Values) {
            connection.SendPackage();
        }
    }

    public void OnData(int connectionId, byte[] data) {
        if (!_serverConnections.ContainsKey(connectionId))
            return;

        _serverConnections[connectionId].ReadPackage(data);
    }

    public void OnConnect(int connectionId) {
        if(connectionId == PhotonNetwork.LocalPlayer.ActorNumber) {
            _connectionState = ConnectionState.Connected;
            return;
        }
        GameDebug.Log($"Player {connectionId} is connected");

        if(!_serverConnections.ContainsKey(connectionId))
            _serverConnections.Add(connectionId, new ServerConnection(connectionId, _transport));
    }

    public void OnDisconnect(int connectionId) {
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

    private class ServerConnection : NetworkConnection<ServerPackageInfo>
    {
        public ServerConnection(int connectionId, INetworkTransport transport) : base(connectionId, transport) {
        }

        public void ReadPackage(byte[] packageData) {
            NetworkMessage content;

            int headerSize;
            var packageSequence = ProcessPackageHeader(packageData, out content, out headerSize);

            var input = new BitInputStream(packageData);
            input.SkipBytes(headerSize);

            //if ((content & NetworkMessage.ClientConfig) != 0)
            //    ReadClientConfig(ref input);
        }

        //public void ReadClientConfig(ref BitInputStream inpuf) {

        //}

        public void SendPackage() {
            var rawOutputStream = new BitOutputStream(m_PackageBuffer);

            ServerPackageInfo packageInfo;
            BeginSendPackage(ref rawOutputStream, out packageInfo);

            // The ifs below are in essence the 'connection handshake' logic.
            if (!clientInfoAcked) {
                // Keep sending client info until it is acked
                WriteClientInfo(ref rawOutputStream);
            }

            CompleteSendPackage(packageInfo, ref rawOutputStream);
        }

        protected override void NotifyDelivered(int sequence, ServerPackageInfo info, bool madeIt) {
            base.NotifyDelivered(sequence, info, madeIt);

            if (madeIt) {
                if ((info.Content & NetworkMessage.ClientInfo) != 0) {
                    clientInfoAcked = true;
                    GameDebug.Log("client acked client info yay");
                }
            }
        }

        public void WriteClientInfo(ref BitOutputStream output) {
            AddMessageContentFlag(NetworkMessage.ClientInfo);
            output.WriteBits((uint)ConnectionId, 8);
        }

        private bool clientInfoAcked;
    }

    
    private Dictionary<int, ServerConnection> _serverConnections = new Dictionary<int, ServerConnection>();
}


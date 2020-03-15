
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
                OnData(e.Data);
                break;
            }
        }
    }

    public void SendData() {
    }

    public void OnData(byte[] data) {
    }

    public void OnConnect(int connectionId) {
        if(connectionId == PhotonNetwork.LocalPlayer.ActorNumber) {
            _connectionState = ConnectionState.Connected;
            return;
        }
        GameDebug.Log($"Player {connectionId} is connected");

        _transport.SendData(connectionId, TransportEvent.Type.Connect, null);

        if(!_serverConnections.ContainsKey(connectionId))
            _serverConnections.Add(connectionId, new ServerConnection(connectionId));
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

    private class ServerConnection
    {
        public readonly int ConnectionId;

        public ServerConnection(int connectionId) {
            ConnectionId = connectionId;
        }
    }

    private Dictionary<int, ServerConnection> _serverConnections = new Dictionary<int, ServerConnection>();
}


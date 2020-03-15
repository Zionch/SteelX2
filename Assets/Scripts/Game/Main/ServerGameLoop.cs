
public class ServerGameLoop : Game.IGameLoop
{
    private enum ServerState
    {
        Connecting,
        Loading,
        Active,
    }

    private NetworkServer _networkServer;

    public bool Init(string[] args) {
        _networkServer = new NetworkServer(new ServerPhotonNetworkTransport());

        _networkServer.Connect();

        return true;
    }

    public void Update() {
        _networkServer.Update();

        _networkServer.SendData();
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
    }

    public void Shutdown() {
        _networkServer.Shutdown();
    }
}


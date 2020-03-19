public class ClientGameLoop : Game.IGameLoop
{
    private enum ClientState
    {
        Connecting,
        Loading,
        Playing,
        Leaving,
    }

    private NetworkClient _networkClient;

    public bool Init(string[] args) {
        _networkClient = new NetworkClient(new ClientPhotonNetworkTransport());

        _networkClient.Connect();

        return true;
    }

    public void Update() {
        _networkClient.Update();

        _networkClient.SendData();
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
    }

    public void Shutdown() {
        _networkClient.Disconnect();
    }
}


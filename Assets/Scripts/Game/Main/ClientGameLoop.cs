
public class ClientGameLoop : Game.IGameLoop
{
    private enum ClientState
    {
        Connecting,
        Loading,
        Playing,
        Leaving,
    }

    public bool Init(string[] args) {
        return true;
    }

    public void Update() {
    }

    public void FixedUpdate() {
    }

    public void LateUpdate() {
    }

    public void Shutdown() {
    }
}


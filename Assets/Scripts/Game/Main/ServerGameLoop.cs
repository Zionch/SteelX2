
public class ServerGameLoop : Game.IGameLoop
{
    private enum ServerState
    {
        Connecting,
        Loading,
        Active,
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


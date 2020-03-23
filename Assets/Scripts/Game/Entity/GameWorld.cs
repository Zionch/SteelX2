
public class GameWorld
{
    public GameTime worldTime;

    public int lastServerTick;

    public float frameDuration;

    public GameWorld(string name) {
        worldTime.tickRate = 60;
    }

    public void Shutdown() {

    }
}

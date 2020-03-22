
public class GameWorld
{
    public GameTime worldTime;

    public double nextTickTime = 0;

    public int lastServerTick;

    public float frameDuration;

    public GameWorld(string name) {
        worldTime.tickRate = 60;

        nextTickTime = Game.frameTime;
    }

    public void Shutdown() {

    }
}

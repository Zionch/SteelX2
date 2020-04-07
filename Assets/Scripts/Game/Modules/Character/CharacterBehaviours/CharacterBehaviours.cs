public class CharacterBehaviours
{
    public static void CreateHandleSpawnSystems(GameWorld world, SystemCollection systems, BundledResourceManager resourceManager, bool server) {
        systems.Add(world.GetECSWorld().CreateSystem<HandleCharacterSpawn>(world, resourceManager, server)); // TODO needs to be done first as it creates presentation
        //systems.Add(world.GetECSWorld().CreateManager<HandleAnimStateCtrlSpawn>(world));
    }

    public static void CreateHandleDespawnSystems(GameWorld world, SystemCollection systems) {
        systems.Add(world.GetECSWorld().CreateSystem<HandleCharacterDespawn>(world));  // TODO HandleCharacterDespawn dewpans char presentation and needs to be called before other HandleDespawn. How do we ensure this ?   
        //systems.Add(world.GetECSWorld().CreateManager<HandleAnimStateCtrlDespawn>(world));
    }
}

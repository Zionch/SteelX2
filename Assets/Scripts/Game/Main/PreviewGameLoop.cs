using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

[DisableAutoCreation]
public class PreviewGameMode : BaseComponentSystem
{
    public int respawnDelay = 20;

    PlayerState m_Player;
    Vector3 m_SpawnPos;
    Quaternion m_SpawnRot;

    bool m_respawnPending;
    float m_respawnTime;

    public PreviewGameMode(GameWorld world, PlayerState Player) : base(world) {
        m_Player = Player;

        // Fallback spawnpos!
        m_SpawnPos = new Vector3(0.0f, 2.0f, 0.0f);
        m_SpawnRot = new Quaternion();
    }

    protected override void OnUpdate() {
        var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = m_world.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);

        if (m_Player.controlledEntity == Entity.Null) {
            GameDebug.Log(string.Format("PreviewGameMode. Spawning as we have to char. Mechtype:{0}", charControl.RequestedMechSettings.MechType));

            Spawn(false);
            return;
        }
    }

    void Spawn(bool keepCharPosition) {
        var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = EntityManager.GetComponentObject<PlayerCharacterControl>(playerEntity);

        if (keepCharPosition && m_Player.controlledEntity != Entity.Null &&
            m_world.GetEntityManager().HasComponent<CharacterInterpolatedData>(m_Player.controlledEntity))//m_world.GetEntityManager() == EntityManager
        {
            var charPresentationState = m_world.GetEntityManager().GetComponentData<CharacterInterpolatedData>(m_Player.controlledEntity);
            m_SpawnPos = charPresentationState.position;
            m_SpawnRot = Quaternion.Euler(0f, charPresentationState.rotation, 0f);
        } //else
        //    FindSpawnTransform();

        // Despawn old controlled
        if (m_Player.controlledEntity != Entity.Null) {
            if (EntityManager.HasComponent<Character>(m_Player.controlledEntity)) {
                CharacterDespawnRequest.Create(PostUpdateCommands, m_Player.controlledEntity);
            }

            m_Player.controlledEntity = Entity.Null;
        }

        CharacterSpawnRequest.Create(PostUpdateCommands, charControl.RequestedMechSettings, m_SpawnPos, m_SpawnRot, playerEntity);
    }

    void FindSpawnTransform() {
        // Find random spawnpoint that matches teamIndex
        //var spawnpoints = Object.FindObjectsOfType<SpawnPoint>();
        //var offset = UnityEngine.Random.Range(0, spawnpoints.Length);
        //for (var i = 0; i < spawnpoints.Length; ++i) {
        //    var sp = spawnpoints[(i + offset) % spawnpoints.Length];
        //    if (sp.teamIndex != m_Player.teamIndex) continue;
        //    m_SpawnPos = sp.transform.position;
        //    m_SpawnRot = sp.transform.rotation;
        //    return;
        //}
    }
}


public class PreviewGameLoop : Game.IGameLoop
{
    public bool Init(string[] args) {
        m_StateMachine = new StateMachine<PreviewState>();
        m_StateMachine.Add(PreviewState.Loading, null, UpdateLoadingState, null);
        m_StateMachine.Add(PreviewState.Active, EnterActiveState, UpdateStateActive, LeaveActiveState);

        Console.AddCommand("nextchar", CmdNextHero, "Select next character", GetHashCode());
        Console.AddCommand("nextteam", CmdNextTeam, "Select next character", GetHashCode());
        Console.AddCommand("spectator", CmdSpectatorCam, "Select spectator cam", GetHashCode());
        Console.AddCommand("respawn", CmdRespawn, "Force a respawn. Optional argument defines now many seconds untill respawn", this.GetHashCode());

        Console.SetOpen(false);

        m_GameWorld = new GameWorld("World[PreviewGameLoop]");

        if (args.Length > 0) {
            Game.Instance.levelManager.LoadLevel(args[0]);
            m_StateMachine.SwitchTo(PreviewState.Loading);
        } else {
            m_StateMachine.SwitchTo(PreviewState.Active);
        }

        GameDebug.Log("Preview initialized");
        return true;
    }

    public void Shutdown() {
        GameDebug.Log("PreviewGameState shutdown");
        Console.RemoveCommandsWithTag(this.GetHashCode());

        m_StateMachine.Shutdown();

        m_PlayerModuleServer.Shutdown();

        Game.Instance.levelManager.UnloadLevel();

        m_GameWorld.Shutdown();
    }

    void UpdateLoadingState() {
        if (Game.Instance.levelManager.IsCurrentLevelLoaded())
            m_StateMachine.SwitchTo(PreviewState.Active);
    }

    public void Update() {
        m_StateMachine.Update();
    }

    void EnterActiveState() {
        m_GameWorld.RegisterSceneEntities();

        m_resourceSystem = new BundledResourceManager(m_GameWorld, "BundledResources/Client");

        // Create serializers so we get errors in preview build
        var dataComponentSerializers = new DataComponentSerializers();

        m_CharacterModule = new CharacterModulePreview(m_GameWorld, m_resourceSystem);
        m_PlayerModuleClient = new PlayerModuleClient(m_GameWorld);
        m_PlayerModuleServer = new PlayerModuleServer(m_GameWorld, m_resourceSystem);
        //m_EffectModule = new EffectModuleClient(m_GameWorld, m_resourceSystem);

        //m_moverUpdate = m_GameWorld.GetECSWorld().CreateSystem<MoverUpdate>(m_GameWorld);

        m_UpdateReplicatedOwnerFlag = m_GameWorld.GetECSWorld().CreateSystem<UpdateReplicatedOwnerFlag>(m_GameWorld);

        m_PlayerModuleClient.RegisterLocalPlayer(0, null);

        // Spawn PlayerState, Character and link up LocalPlayer
        m_Player = m_PlayerModuleServer.CreatePlayer(m_GameWorld, 0, "LocalMech", true);

        var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        //var charControl = m_Player.gameObject.GetComponent<PlayerCharacterControl>();
        var charControl = m_GameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);
        charControl.RequestedMechSettings = new MechSettings();
        m_Player.teamIndex = 0;

        m_previewGameMode = m_GameWorld.GetECSWorld().CreateSystem<PreviewGameMode>(m_GameWorld, m_Player);

        Game.SetMousePointerLock(true);
    }

    void LeaveActiveState() {
        m_CharacterModule.Shutdown();
        m_PlayerModuleClient.Shutdown();
        m_PlayerModuleServer.Shutdown();
        m_GameWorld.GetECSWorld().DestroySystem(m_previewGameMode);

        m_GameWorld.GetECSWorld().DestroySystem(m_UpdateReplicatedOwnerFlag);

        m_resourceSystem.Shutdown();
    }

    void UpdateStateActive() {
        // Sample input
        bool userInputEnabled = Game.GetMousePointerLock();
        m_PlayerModuleClient.SampleInput(userInputEnabled, Time.deltaTime, 0);

        if (gameTime.tickRate != Game.serverTickRate.IntValue)
            gameTime.tickRate = Game.serverTickRate.IntValue;

        //if (Game.Input.GetKeyUp(KeyCode.H) && Game.allowCharChange.IntValue == 1) {
        //    CmdNextHero(null);
        //}

        bool commandWasConsumed = false;
        while (Game.frameTime > m_GameWorld.nextTickTime) {
            gameTime.tick++;
            gameTime.tickDuration = gameTime.tickInterval;

            commandWasConsumed = true;

            PreviewTickUpdate();
            m_GameWorld.nextTickTime += m_GameWorld.WorldTime.tickInterval;
        }
        if (commandWasConsumed)
            m_PlayerModuleClient.ResetInput(userInputEnabled);
    }


    public void FixedUpdate() {
    }

    public void PreviewTickUpdate() {
        m_GameWorld.WorldTime = gameTime;
        m_GameWorld.frameDuration = gameTime.tickDuration;

        m_PlayerModuleClient.ResolveReferenceFromLocalPlayerToPlayer();//link player state to local player
        m_PlayerModuleClient.HandleCommandReset();
        m_PlayerModuleClient.StoreCommand(m_GameWorld.WorldTime.tick);

        // Game mode update
        m_previewGameMode.Update();

        // Handle spawn requests
        m_CharacterModule.HandleSpawnRequests();

        m_UpdateReplicatedOwnerFlag.Update();

        // Apply command for frame
        m_PlayerModuleClient.RetrieveCommand(m_GameWorld.WorldTime.tick);

        // Handle spawn
        m_CharacterModule.HandleSpawns(); ; // TODO (mogensh) creates presentations, so it needs to be done first. Find better solution for ordering
      
        m_PlayerModuleClient.HandleSpawn();
      
        // Handle controlled entity changed
        m_PlayerModuleClient.HandleControlledEntityChanged();

        // Update movement of scene objects. Projectiles and grenades can also start update as they use collision data from last frame
  

        // Update movement of player controlled units (depends on moveable scene objects being done)
        m_CharacterModule.AbilityRequestUpdate();
        m_CharacterModule.MovementStart();
        m_CharacterModule.MovementResolve();
        m_CharacterModule.AbilityStart();
        m_CharacterModule.AbilityResolve();


        // Handle damage        
        m_CharacterModule.HandleDamage();

        // Update presentation
        m_CharacterModule.UpdatePresentation();

        // Handle despawns
        m_GameWorld.ProcessDespawns();
    }

    public void LateUpdate() {
        // TODO (petera) Should the state machine actually have a lateupdate so we don't have to do this always?
        if (m_StateMachine.CurrentState() == PreviewState.Active) {
            m_GameWorld.frameDuration = Time.deltaTime;

            m_CharacterModule.LateUpdate();

            // Update camera
            m_PlayerModuleClient.CameraUpdate();


            // Update UI
            m_CharacterModule.UpdateUI();

            // Finalize jobs that needs to be done before rendering
        }
    }

    void CmdNextHero(string[] args) {
        //if (m_Player == null)
        //    return;

        //if (Game.allowCharChange.IntValue != 1)
        //    return;

        //var charSetupRegistry = m_resourceSystem.GetResourceRegistry<HeroTypeRegistry>();
        //var charSetupCount = charSetupRegistry.entries.Count;

        //var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        //var charControl = m_GameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);

        //charControl.requestedCharacterType = charControl.characterType + 1;
        //if (charControl.requestedCharacterType >= charSetupCount)
        //    charControl.requestedCharacterType = 0;

        //GameDebug.Log(string.Format("PreviewGameLoop. Requesting char:{0}", charControl.requestedCharacterType));
    }

    void CmdSpectatorCam(string[] args) {
        //if (m_Player == null)
        //    return;

        //if (Game.allowCharChange.IntValue != 1)
        //    return;

        //var playerEntity = m_Player.gameObject.GetComponent<GameObjectEntity>().Entity;
        //var charControl = m_GameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);

        //// Until we have better way of controlling other units than character, the spectator cam gets type 1000         
        //charControl.requestedCharacterType = 1000;
    }

    void CmdRespawn(string[] args) {
        //if (m_Player == null)
        //    return;

        //m_previewGameMode.respawnDelay = args.Length == 0 ? 3 : int.Parse(args[0]);

        //var healthState = m_GameWorld.GetEntityManager().GetComponentData<HealthStateData>(m_Player.controlledEntity);
        //healthState.health = 0;
        //m_GameWorld.GetEntityManager().SetComponentData(m_Player.controlledEntity, healthState);
    }


    void CmdNextTeam(string[] args) {
        if (m_Player == null)
            return;

        m_Player.teamIndex++;
        if (m_Player.teamIndex > 1)
            m_Player.teamIndex = 0;
    }

    enum PreviewState
    {
        Loading,
        Active
    }
    StateMachine<PreviewState> m_StateMachine;

    BundledResourceManager m_resourceSystem;

    GameWorld m_GameWorld;
    CharacterModulePreview m_CharacterModule;
    PlayerModuleClient m_PlayerModuleClient;
    PlayerModuleServer m_PlayerModuleServer;
    UpdateReplicatedOwnerFlag m_UpdateReplicatedOwnerFlag;

    PreviewGameMode m_previewGameMode;

    PlayerState m_Player;

    GameTime gameTime = new GameTime(60);
}

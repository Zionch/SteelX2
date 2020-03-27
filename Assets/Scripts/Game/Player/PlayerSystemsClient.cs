using Unity.Entities;

[DisableAutoCreation]
public class ResolvePlayerReference : BaseComponentSystem
{
    EntityQuery Group;

    public ResolvePlayerReference(GameWorld world) : base(world) { }

    protected override void OnCreate() {
        base.OnCreate();
        Group = GetEntityQuery(typeof(PlayerState));
    }

    public void SetLocalPlayer(LocalPlayer localPlayer) {
        m_LocalPlayer = localPlayer;
    }

    protected override void OnUpdate() {
        if (m_LocalPlayer == null)
            return;

        // Find player with correct player id
        var playerStateArray = Group.ToComponentArray<PlayerState>();
        for (var playerIndex = 0; playerIndex < playerStateArray.Length; playerIndex++) {
            if (playerStateArray[playerIndex].playerId == m_LocalPlayer.playerId) {
                m_LocalPlayer.playerState = playerStateArray[playerIndex];
                break;
            }
        }
    }

    LocalPlayer m_LocalPlayer;
}
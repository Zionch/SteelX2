// Component specifies that entity is on server or is predicted
using Unity.Entities;

public struct ServerEntity : IComponentData    // TODO  move to ReplicatedModule rename to something relevant (as it is now tied to replicated entity predictiongPlayer) 
{
    public int foo;
}
﻿using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class SystemCollection
{
    List<ComponentSystemBase> systems = new List<ComponentSystemBase>();

    public void Add(ComponentSystemBase system) {
        systems.Add(system);
    }

    public void Update() {
        foreach (var system in systems)
            system.Update();
    }

    public void Shutdown(World world) {
        foreach (var system in systems)
            world.DestroySystem(system);
    }
}

public abstract class CharacterModuleShared
{
    protected GameWorld m_world;

    protected SystemCollection m_HandleSpawnSystems = new SystemCollection();
    protected SystemCollection m_HandleDespawnSystems = new SystemCollection();

    protected SystemCollection m_AbilityRequestUpdateSystems = new SystemCollection();
    protected SystemCollection m_MovementStartSystems = new SystemCollection();
    protected SystemCollection m_MovementResolveSystems = new SystemCollection();
    protected SystemCollection m_AbilityStartSystems = new SystemCollection();
    protected SystemCollection m_AbilityResolveSystems = new SystemCollection();


    public CharacterModuleShared(GameWorld world) {
        m_world = world;
    }

    public virtual void Shutdown() {
        m_HandleSpawnSystems.Shutdown(m_world.GetECSWorld());
        m_HandleDespawnSystems.Shutdown(m_world.GetECSWorld());
        m_AbilityRequestUpdateSystems.Shutdown(m_world.GetECSWorld());
        m_MovementStartSystems.Shutdown(m_world.GetECSWorld());
        m_MovementResolveSystems.Shutdown(m_world.GetECSWorld());
        m_AbilityStartSystems.Shutdown(m_world.GetECSWorld());
        m_AbilityResolveSystems.Shutdown(m_world.GetECSWorld());
    }

    public void HandleSpawns() {
        m_HandleSpawnSystems.Update();
    }

    public void HandleDespawns() {
        m_HandleDespawnSystems.Update();
    }

    public void AbilityRequestUpdate() {
        m_AbilityRequestUpdateSystems.Update();
    }

    public void MovementStart() {
        m_MovementStartSystems.Update();
    }

    public void MovementResolve() {
        m_MovementResolveSystems.Update();
    }

    public void AbilityStart() {
        m_AbilityStartSystems.Update();
    }

    public void AbilityResolve() {
        m_AbilityResolveSystems.Update();
    }
}

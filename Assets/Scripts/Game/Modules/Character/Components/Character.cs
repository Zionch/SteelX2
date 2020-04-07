using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class Character : MonoBehaviour{
    [NonSerialized] public Entity presentation;    // Main char presentation used updating animation state 
    //[NonSerialized] public WeaponPresentationSetup[] weaponPresentations = new WeaponPresentationSetup[4];

    [NonSerialized] public List<MechPresentationSetup> presentations = new List<MechPresentationSetup>();

    [NonSerialized] public MechTypeAsset MechTypeData;

    [NonSerialized] public Vector3 m_TeleportToPosition;
    [NonSerialized] public Quaternion m_TeleportToRotation;
    [NonSerialized] public bool m_TeleportPending;

    public void TeleportTo(Vector3 position, Quaternion rotation) {
        m_TeleportPending = true;
        m_TeleportToPosition = position;
        m_TeleportToRotation = rotation;
    }
}
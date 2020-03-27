
using System;
using UnityEngine;

public class Character : MonoBehaviour{
    [NonSerialized] public Vector3 m_TeleportToPosition;
    [NonSerialized] public Quaternion m_TeleportToRotation;
    [NonSerialized] public bool m_TeleportPending;

    public void TeleportTo(Vector3 position, Quaternion rotation) {
        m_TeleportPending = true;
        m_TeleportToPosition = position;
        m_TeleportToRotation = rotation;
    }
}
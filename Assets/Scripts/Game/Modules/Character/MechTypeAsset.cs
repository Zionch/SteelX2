using System;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "MechType", menuName = "SteelX/Mech/MechType")]
public class MechTypeAsset : ScriptableObject
{
    //[Serializable]
    //public class ItemEntry
    //{
    //    public ItemTypeDefinition itemType;
    //}

    [Serializable]
    public class SprintCameraSettings
    {
        public float FOVFactor = 0.93f;
        public float FOVInceraetSpeed = 1.0f;
        public float FOVDecreaseSpeed = 0.2f;
    }

    public float health = 100;
    public SprintCameraSettings sprintCameraSettings = new SprintCameraSettings();
    public float eyeHeight = 1.8f;
    //public CharacterMoveQuery.Settings characterMovementSettings;

    public MechTypeDefinition Mech;
    //public ItemEntry[] items;
}

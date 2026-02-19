using System;
using RoR2;
using UnityEngine;

namespace MuscleMemory
{
    internal enum SkillSlotKind
    {
        Primary = 0,
        Secondary = 1,
        Utility = 2,
        Special = 3
    }

    internal sealed class SlotProgress
    {
        internal double Proficiency;
        internal int Level;
        internal float LastActivatedTime = -999f;
    }

    internal sealed class ReplicatedLevelState
    {
        internal readonly int[] Levels = new int[Constants.SlotCount];
        internal readonly float[] Progress = new float[Constants.SlotCount];
        internal bool FlowActive;

        internal int GetLevel(SkillSlotKind slot)
        {
            return Levels[(int)slot];
        }
    }

    internal sealed class PlayerProgressState
    {
        internal readonly CharacterMaster Master;
        internal readonly SlotProgress[] Slots = new SlotProgress[Constants.SlotCount];

        internal CharacterBody LastBody;
        internal Vector3 LastPosition;
        internal float LastCombinedHealth;
        internal bool SnapshotInitialized;

        internal int LastPrimaryStock;
        internal int LastSecondaryStock;
        internal int LastUtilityStock;
        internal int LastSpecialStock;

        internal SkillSlotKind LastActivatedSlot;
        internal float LastActivatedTime = -999f;

        internal float UtilityDistanceWindowEnd;
        internal float FlowWindowEnd;
        internal bool FlowWasActive;

        internal PlayerProgressState(CharacterMaster master)
        {
            Master = master;
            for (int i = 0; i < Slots.Length; i++)
            {
                Slots[i] = new SlotProgress();
            }
        }

        internal int GetLastStock(SkillSlotKind slot)
        {
            switch (slot)
            {
                case SkillSlotKind.Primary: return LastPrimaryStock;
                case SkillSlotKind.Secondary: return LastSecondaryStock;
                case SkillSlotKind.Utility: return LastUtilityStock;
                case SkillSlotKind.Special: return LastSpecialStock;
                default: return 0;
            }
        }

        internal void SetLastStock(SkillSlotKind slot, int value)
        {
            switch (slot)
            {
                case SkillSlotKind.Primary: LastPrimaryStock = value; return;
                case SkillSlotKind.Secondary: LastSecondaryStock = value; return;
                case SkillSlotKind.Utility: LastUtilityStock = value; return;
                case SkillSlotKind.Special: LastSpecialStock = value; return;
            }
        }
    }

    internal static class Constants
    {
        internal const int SlotCount = 4;
        internal const float MinCooldownIntervalMultiplier = 0.15f;
        internal const float MinDamageMultiplier = 0.15f;
    }
}

using System;
using RoR2;

namespace MuscleMemory
{
    internal sealed class MilestoneSystem
    {
        private readonly MuscleMemoryConfig _config;

        internal MilestoneSystem(MuscleMemoryConfig config)
        {
            _config = config;
        }

        internal void BroadcastMilestoneUnlocks(string playerName, SkillSlotKind slot, int previousLevel, int nextLevel)
        {
            switch (slot)
            {
                case SkillSlotKind.Primary:
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.PrimaryMilestone1Level.Value, "Primary Tier 1", "primary hits gain bonus <style=cIsHealing>crit chance</style>");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.PrimaryMilestone2Level.Value, "Primary Tier 2", "primary hits now inflict <style=cIsHealing>Bleed</style>");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.PrimaryMilestone3Level.Value, "Primary Tier 3", "bonus <style=cIsHealing>attack speed</style> activated");
                    break;

                case SkillSlotKind.Secondary:
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.SecondaryMilestone1Level.Value, "Secondary Tier 1", "bonus <style=cIsHealing>cooldown reduction</style> activated");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.SecondaryMilestone2Level.Value, "Secondary Tier 2", "kills during attribution refund a <style=cIsHealing>stock</style>");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.SecondaryMilestone3Level.Value, "Secondary Tier 3", "further <style=cIsHealing>cooldown reduction</style> activated");
                    break;

                case SkillSlotKind.Utility:
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.UtilityMilestone1Level.Value, "Utility Tier 1", "enhanced <style=cIsHealing>flow speed</style> activated");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.UtilityMilestone2Level.Value, "Utility Tier 2", "<style=cIsHealing>armor</style> during flow activated");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.UtilityMilestone3Level.Value, "Utility Tier 3", "extended <style=cIsHealing>flow duration</style> activated");
                    break;

                case SkillSlotKind.Special:
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.SpecialMilestone1Level.Value, "Special Tier 1", "bonus <style=cIsHealing>barrier</style> on cast activated");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.SpecialMilestone2Level.Value, "Special Tier 2", "special cast <style=cIsHealing>refunds cooldowns</style> on other skills");
                    CheckAndBroadcast(playerName, previousLevel, nextLevel,
                        _config.SpecialMilestone3Level.Value, "Special Tier 3", "greater <style=cIsHealing>barrier</style> on cast activated");
                    break;
            }
        }

        internal bool HasMilestone(SkillSlotKind slot, int tier, int level)
        {
            int required = GetMilestoneLevel(slot, tier);
            return level >= Math.Max(1, required);
        }

        internal int GetMilestoneLevel(SkillSlotKind slot, int tier)
        {
            switch (slot)
            {
                case SkillSlotKind.Primary:
                    if (tier == 1) return _config.PrimaryMilestone1Level.Value;
                    if (tier == 2) return _config.PrimaryMilestone2Level.Value;
                    if (tier == 3) return _config.PrimaryMilestone3Level.Value;
                    break;
                case SkillSlotKind.Secondary:
                    if (tier == 1) return _config.SecondaryMilestone1Level.Value;
                    if (tier == 2) return _config.SecondaryMilestone2Level.Value;
                    if (tier == 3) return _config.SecondaryMilestone3Level.Value;
                    break;
                case SkillSlotKind.Utility:
                    if (tier == 1) return _config.UtilityMilestone1Level.Value;
                    if (tier == 2) return _config.UtilityMilestone2Level.Value;
                    if (tier == 3) return _config.UtilityMilestone3Level.Value;
                    break;
                case SkillSlotKind.Special:
                    if (tier == 1) return _config.SpecialMilestone1Level.Value;
                    if (tier == 2) return _config.SpecialMilestone2Level.Value;
                    if (tier == 3) return _config.SpecialMilestone3Level.Value;
                    break;
            }

            return int.MaxValue;
        }

        private static void CheckAndBroadcast(string playerName, int previousLevel, int nextLevel, int milestoneLevel, string milestoneName, string description)
        {
            int target = Math.Max(1, milestoneLevel);
            if (previousLevel < target && nextLevel >= target)
            {
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                {
                    baseToken = $"<style=cIsUtility>{playerName}</style> unlocked <style=cIsDamage>{milestoneName}</style>: {description}."
                });
            }
        }
    }
}

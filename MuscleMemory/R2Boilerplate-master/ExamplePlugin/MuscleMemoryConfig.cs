using BepInEx.Configuration;

namespace MuscleMemory
{
    internal sealed class MuscleMemoryConfig
    {
        // Progression
        internal ConfigEntry<double> LevelCurveK;
        internal ConfigEntry<int> CurveEaseUntilLevel;
        internal ConfigEntry<float> CurveEarlyEaseMultiplier;
        internal ConfigEntry<int> CurveHardeningStartLevel;
        internal ConfigEntry<float> CurveHardeningPerLevel;
        internal ConfigEntry<int> LowLevelEaseUntil;
        internal ConfigEntry<float> LowLevelXpMultiplier;
        internal ConfigEntry<float> AttributionWindowSeconds;
        internal ConfigEntry<float> DamageToXp;
        internal ConfigEntry<float> FlatHitXp;
        internal ConfigEntry<float> HealingToXp;
        internal ConfigEntry<float> UtilityDistanceToXp;
        internal ConfigEntry<float> UtilityDistanceWindowSeconds;
        internal ConfigEntry<float> SurvivorScalingMultiplier;
        internal ConfigEntry<bool> SplitUnattributedXp;
        internal ConfigEntry<float> ActivationFlatXp;

        // Bonuses
        internal ConfigEntry<float> PrimaryDamagePerLevel;
        internal ConfigEntry<float> SecondaryCooldownReductionPerLevel;
        internal ConfigEntry<float> UtilityCooldownReductionPerLevel;
        internal ConfigEntry<float> SpecialCooldownReductionPerLevel;
        internal ConfigEntry<float> UtilityFlowMoveSpeedBonus;
        internal ConfigEntry<float> UtilityFlowDurationSeconds;
        internal ConfigEntry<float> SpecialBarrierFraction;

        // Cold Start
        internal ConfigEntry<bool> EnableColdStart;
        internal ConfigEntry<float> ColdStartCooldownPenalty;
        internal ConfigEntry<float> ColdStartPrimaryDamagePenalty;

        // Feedback
        internal ConfigEntry<bool> EnableLevelUpEffects;
        internal ConfigEntry<string> LevelUpEffectPrefabPath;
        internal ConfigEntry<string> LevelUpSoundEvent;

        // UI
        internal ConfigEntry<bool> ShowSkillHud;
        internal ConfigEntry<float> SkillHudScale;

        // Chat
        internal ConfigEntry<bool> BroadcastAllLevelUps;
        internal ConfigEntry<bool> BroadcastMilestones;

        // Milestones - Primary
        internal ConfigEntry<int> PrimaryMilestone1Level;
        internal ConfigEntry<float> PrimaryMilestone1CritChance;
        internal ConfigEntry<int> PrimaryMilestone2Level;
        internal ConfigEntry<float> PrimaryMilestone2BleedDuration;
        internal ConfigEntry<int> PrimaryMilestone3Level;
        internal ConfigEntry<float> PrimaryMilestone3AttackSpeedBonus;

        // Milestones - Secondary
        internal ConfigEntry<int> SecondaryMilestone1Level;
        internal ConfigEntry<float> SecondaryMilestone1CooldownReduction;
        internal ConfigEntry<int> SecondaryMilestone2Level;
        internal ConfigEntry<int> SecondaryMilestone3Level;
        internal ConfigEntry<float> SecondaryMilestone3CooldownReduction;

        // Milestones - Utility
        internal ConfigEntry<int> UtilityMilestone1Level;
        internal ConfigEntry<float> UtilityMilestone1FlowBonus;
        internal ConfigEntry<int> UtilityMilestone2Level;
        internal ConfigEntry<float> UtilityMilestone2ArmorBonus;
        internal ConfigEntry<int> UtilityMilestone3Level;
        internal ConfigEntry<float> UtilityMilestone3FlowDurationExtension;

        // Milestones - Special
        internal ConfigEntry<int> SpecialMilestone1Level;
        internal ConfigEntry<float> SpecialMilestone1BarrierBonus;
        internal ConfigEntry<int> SpecialMilestone2Level;
        internal ConfigEntry<float> SpecialMilestone2CooldownRefund;
        internal ConfigEntry<int> SpecialMilestone3Level;
        internal ConfigEntry<float> SpecialMilestone3BarrierBonus;

        // Decay
        internal ConfigEntry<bool> EnableProficiencyDecay;
        internal ConfigEntry<float> DecayIdleThresholdSeconds;
        internal ConfigEntry<float> DecayRatePerSecond;

        // Networking
        internal ConfigEntry<float> ReplicationInterval;

        internal void Bind(ConfigFile config)
        {
            // Progression
            LevelCurveK = config.Bind("Progression", "LevelCurveK", 60d,
                "Base K for proficiency requirements. Lower K makes leveling easier.");
            CurveEaseUntilLevel = config.Bind("Progression", "CurveEaseUntilLevel", 6,
                "Required proficiency is reduced for levels up to this level.");
            CurveEarlyEaseMultiplier = config.Bind("Progression", "CurveEarlyEaseMultiplier", 0.8f,
                "Multiplier applied to required proficiency for early levels. Lower values are easier.");
            CurveHardeningStartLevel = config.Bind("Progression", "CurveHardeningStartLevel", 8,
                "After this level, each next level requires additional proficiency growth.");
            CurveHardeningPerLevel = config.Bind("Progression", "CurveHardeningPerLevel", 0.06f,
                "Additional requirement per level above CurveHardeningStartLevel.");
            LowLevelEaseUntil = config.Bind("Progression", "LowLevelEaseUntil", 4,
                "Slots below this level gain boosted proficiency to speed up early leveling.");
            LowLevelXpMultiplier = config.Bind("Progression", "LowLevelXpMultiplier", 1.5f,
                "Proficiency multiplier applied while a slot is below LowLevelEaseUntil.");
            AttributionWindowSeconds = config.Bind("Progression", "AttributionWindowSeconds", 5f,
                "Seconds after a skill activation during which damage/healing is attributed to that slot. Increase for characters with persistent effects.");
            DamageToXp = config.Bind("Progression", "DamageToXp", 0.02f,
                "Proficiency gained per point of damage dealt.");
            FlatHitXp = config.Bind("Progression", "FlatHitXp", 0.35f,
                "Flat proficiency gained per damaging hit.");
            HealingToXp = config.Bind("Progression", "HealingToXp", 0.05f,
                "Proficiency gained per point healed.");
            UtilityDistanceToXp = config.Bind("Progression", "UtilityDistanceToXp", 0.2f,
                "Utility proficiency gained per meter moved in utility distance window.");
            UtilityDistanceWindowSeconds = config.Bind("Progression", "UtilityDistanceWindowSeconds", 1.25f,
                "Window after utility activation where movement grants utility proficiency.");
            SurvivorScalingMultiplier = config.Bind("Progression", "SurvivorScalingMultiplier", 1f,
                "Global multiplier applied to all proficiency gains. Adjust per-profile to tune for different survivors.");
            SplitUnattributedXp = config.Bind("Progression", "SplitUnattributedXp", false,
                "When true, damage that cannot be attributed to any recently-used skill is split evenly across all slots. When false, it defaults to Primary.");
            ActivationFlatXp = config.Bind("Progression", "ActivationFlatXp", 0.5f,
                "Flat proficiency granted each time a skill is activated, so skills that deal no damage (utility dashes, shields) still progress.");

            // Bonuses
            PrimaryDamagePerLevel = config.Bind("Bonuses", "PrimaryDamagePerLevel", 0.03f,
                "Damage multiplier per primary level.");
            SecondaryCooldownReductionPerLevel = config.Bind("Bonuses", "SecondaryCooldownReductionPerLevel", 0.025f,
                "Cooldown reduction per secondary level.");
            UtilityCooldownReductionPerLevel = config.Bind("Bonuses", "UtilityCooldownReductionPerLevel", 0.03f,
                "Cooldown reduction per utility level.");
            SpecialCooldownReductionPerLevel = config.Bind("Bonuses", "SpecialCooldownReductionPerLevel", 0.02f,
                "Cooldown reduction per special level.");
            UtilityFlowMoveSpeedBonus = config.Bind("Bonuses", "UtilityFlowMoveSpeedBonus", 0.15f,
                "Temporary move speed bonus after utility use.");
            UtilityFlowDurationSeconds = config.Bind("Bonuses", "UtilityFlowDurationSeconds", 1.25f,
                "Duration of utility flow movement bonus.");
            SpecialBarrierFraction = config.Bind("Bonuses", "SpecialBarrierFraction", 0.04f,
                "Barrier fraction of full combined health granted on special cast.");

            // Cold Start
            EnableColdStart = config.Bind("ColdStart", "EnableColdStart", true,
                "Enable rust-style penalties for slots below level 1.");
            ColdStartCooldownPenalty = config.Bind("ColdStart", "CooldownPenalty", 0.1f,
                "Additional cooldown interval multiplier for slots below level 1.");
            ColdStartPrimaryDamagePenalty = config.Bind("ColdStart", "PrimaryDamagePenalty", 0.05f,
                "Primary damage penalty while primary level is below 1.");

            // Feedback
            EnableLevelUpEffects = config.Bind("Feedback", "EnableLevelUpEffects", true,
                "Play level-up VFX and sound when a slot levels up.");
            LevelUpEffectPrefabPath = config.Bind("Feedback", "LevelUpEffectPrefabPath",
                "Prefabs/Effects/LevelUpEffect", "Prefab path for level-up VFX.");
            LevelUpSoundEvent = config.Bind("Feedback", "LevelUpSoundEvent",
                "Play_UI_levelUp", "Wwise event to play on level-up.");

            // UI
            ShowSkillHud = config.Bind("UI", "ShowSkillHud", true,
                "Show local slot levels near the skill bar.");
            SkillHudScale = config.Bind("UI", "SkillHudScale", 1f,
                "Scale multiplier for the local skill HUD.");

            // Chat
            BroadcastAllLevelUps = config.Bind("Chat", "BroadcastAllLevelUps", false,
                "Broadcast every level-up to chat. When false, only milestones are announced.");
            BroadcastMilestones = config.Bind("Chat", "BroadcastMilestones", true,
                "Broadcast milestone unlocks to chat.");

            // Milestones - Primary
            PrimaryMilestone1Level = config.Bind("Milestones.Primary", "Tier1Level", 5,
                "Primary tier-1 milestone: grants bonus crit chance on primary-attributed hits.");
            PrimaryMilestone1CritChance = config.Bind("Milestones.Primary", "Tier1CritChance", 15f,
                "Crit chance (percent) added by the primary tier-1 milestone.");
            PrimaryMilestone2Level = config.Bind("Milestones.Primary", "Tier2Level", 10,
                "Primary tier-2 milestone: primary hits inflict bleed.");
            PrimaryMilestone2BleedDuration = config.Bind("Milestones.Primary", "Tier2BleedDuration", 4.5f,
                "Bleed duration (seconds) applied by the primary tier-2 milestone.");
            PrimaryMilestone3Level = config.Bind("Milestones.Primary", "Tier3Level", 15,
                "Primary tier-3 milestone: grants bonus attack speed.");
            PrimaryMilestone3AttackSpeedBonus = config.Bind("Milestones.Primary", "Tier3AttackSpeedBonus", 0.25f,
                "Attack speed bonus granted by the primary tier-3 milestone.");

            // Milestones - Secondary
            SecondaryMilestone1Level = config.Bind("Milestones.Secondary", "Tier1Level", 5,
                "Secondary tier-1 milestone: grants bonus cooldown reduction.");
            SecondaryMilestone1CooldownReduction = config.Bind("Milestones.Secondary", "Tier1CooldownReduction", 0.06f,
                "Flat cooldown reduction from the secondary tier-1 milestone.");
            SecondaryMilestone2Level = config.Bind("Milestones.Secondary", "Tier2Level", 10,
                "Secondary tier-2 milestone: kills during attribution window refund a stock.");
            SecondaryMilestone3Level = config.Bind("Milestones.Secondary", "Tier3Level", 15,
                "Secondary tier-3 milestone: grants further cooldown reduction.");
            SecondaryMilestone3CooldownReduction = config.Bind("Milestones.Secondary", "Tier3CooldownReduction", 0.10f,
                "Flat cooldown reduction from the secondary tier-3 milestone.");

            // Milestones - Utility
            UtilityMilestone1Level = config.Bind("Milestones.Utility", "Tier1Level", 5,
                "Utility tier-1 milestone: grants additional flow move speed.");
            UtilityMilestone1FlowBonus = config.Bind("Milestones.Utility", "Tier1FlowBonus", 0.18f,
                "Additional move speed bonus during flow from the utility tier-1 milestone.");
            UtilityMilestone2Level = config.Bind("Milestones.Utility", "Tier2Level", 10,
                "Utility tier-2 milestone: grants armor during flow.");
            UtilityMilestone2ArmorBonus = config.Bind("Milestones.Utility", "Tier2ArmorBonus", 50f,
                "Armor bonus during flow from the utility tier-2 milestone.");
            UtilityMilestone3Level = config.Bind("Milestones.Utility", "Tier3Level", 15,
                "Utility tier-3 milestone: extends flow duration.");
            UtilityMilestone3FlowDurationExtension = config.Bind("Milestones.Utility", "Tier3FlowDurationExtension", 1.25f,
                "Additional seconds added to flow duration from the utility tier-3 milestone.");

            // Milestones - Special
            SpecialMilestone1Level = config.Bind("Milestones.Special", "Tier1Level", 5,
                "Special tier-1 milestone: grants bonus barrier on special cast.");
            SpecialMilestone1BarrierBonus = config.Bind("Milestones.Special", "Tier1BarrierBonus", 0.04f,
                "Additional barrier fraction from the special tier-1 milestone.");
            SpecialMilestone2Level = config.Bind("Milestones.Special", "Tier2Level", 10,
                "Special tier-2 milestone: special cast reduces all other skill cooldowns.");
            SpecialMilestone2CooldownRefund = config.Bind("Milestones.Special", "Tier2CooldownRefund", 0.25f,
                "Fraction of remaining cooldown refunded on other skills when special is cast.");
            SpecialMilestone3Level = config.Bind("Milestones.Special", "Tier3Level", 15,
                "Special tier-3 milestone: grants a larger barrier bonus.");
            SpecialMilestone3BarrierBonus = config.Bind("Milestones.Special", "Tier3BarrierBonus", 0.07f,
                "Additional barrier fraction from the special tier-3 milestone.");

            // Decay
            EnableProficiencyDecay = config.Bind("Decay", "EnableProficiencyDecay", false,
                "Enable slow proficiency decay for slots not used recently (reinforces the 'muscle memory' theme).");
            DecayIdleThresholdSeconds = config.Bind("Decay", "IdleThresholdSeconds", 30f,
                "Seconds a slot must be idle before decay begins.");
            DecayRatePerSecond = config.Bind("Decay", "DecayRatePerSecond", 0.5f,
                "Proficiency lost per second once decay is active.");

            // Networking
            ReplicationInterval = config.Bind("Networking", "ReplicationInterval", 0.2f,
                "Host-to-client level replication interval (seconds).");
        }
    }
}

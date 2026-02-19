using BepInEx;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;

namespace WarfrontDirector
{
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public sealed class WarfrontDirectorPlugin : BaseUnityPlugin
    {
        public const string PluginAuthor = "AuthorName";
        public const string PluginName = "WarfrontDirector";
        public const string PluginVersion = "0.4.0";
        public const string PluginGUID = PluginAuthor + "." + PluginName;

        internal static WarfrontDirectorPlugin Instance { get; private set; }

        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<float> RollbackCapPercentPerAssault { get; private set; }
        internal static ConfigEntry<float> RollbackImmunitySeconds { get; private set; }
        internal static ConfigEntry<float> RollbackRatePerNegativeDelta { get; private set; }
        internal static ConfigEntry<float> BreachContestSeconds { get; private set; }
        internal static ConfigEntry<float> NodeRewardGold { get; private set; }
        internal static ConfigEntry<float> CommanderRewardRelayBase { get; private set; }
        internal static ConfigEntry<float> CommanderRewardForgeBase { get; private set; }
        internal static ConfigEntry<float> CommanderRewardSirenBase { get; private set; }
        internal static ConfigEntry<float> CommanderRewardCacheBase { get; private set; }
        internal static ConfigEntry<float> CommanderRewardStageScale { get; private set; }
        internal static ConfigEntry<float> CommanderRewardDifficultyScale { get; private set; }
        internal static ConfigEntry<float> CommanderRewardPlayerScale { get; private set; }
        internal static ConfigEntry<float> CommanderTetherDistance { get; private set; }
        internal static ConfigEntry<float> RoleBudgetMultiplier { get; private set; }
        internal static ConfigEntry<float> LoneWolfPressureThreshold { get; private set; }
        internal static ConfigEntry<float> LoneWolfContestPenalty { get; private set; }
        internal static ConfigEntry<float> MercyWindowSeconds { get; private set; }
        internal static ConfigEntry<float> MercyCooldownSeconds { get; private set; }

        private WarfrontDirectorController _controller;

        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);

            BindConfig();
            RegisterLanguageTokens();

            _controller = gameObject.AddComponent<WarfrontDirectorController>();

            Log.Info($"{PluginName} {PluginVersion} initialized.");
        }

        private void OnDestroy()
        {
            if (_controller)
            {
                _controller.Shutdown();
            }
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("General", "Enabled", true, "Enable Warfront Director mode. Warfront is always-on while this is true.");
            RollbackCapPercentPerAssault = Config.Bind("Teleporter", "Rollback Cap Per Assault (%)", 15f, "Maximum teleporter rollback allowed during one assault window.");
            RollbackImmunitySeconds = Config.Bind("Teleporter", "Rollback Immunity Seconds", 30f, "Seconds after rollback before another rollback can occur.");
            RollbackRatePerNegativeDelta = Config.Bind("Teleporter", "Rollback Rate Per Negative Delta", 0.0125f, "Charge rollback per second for each point of negative contest delta.");
            BreachContestSeconds = Config.Bind("Teleporter", "Breach Contest Seconds", 9f, "Seconds of sustained enemy contest needed to trigger a breach push.");
            NodeRewardGold = Config.Bind("Commanders", "Fallback Reward Gold", 35f, "Fallback reward if a commander type base reward cannot be resolved.");
            CommanderRewardRelayBase = Config.Bind("Commanders", "Relay Commander Base Reward", 28f, "Base gold reward for defeating a Relay Commander before scaling.");
            CommanderRewardForgeBase = Config.Bind("Commanders", "Forge Commander Base Reward", 35f, "Base gold reward for defeating a Forge Commander before scaling.");
            CommanderRewardSirenBase = Config.Bind("Commanders", "Siren Commander Base Reward", 32f, "Base gold reward for defeating a Siren Commander before scaling.");
            CommanderRewardCacheBase = Config.Bind("Commanders", "Cache Commander Base Reward", 40f, "Base gold reward for defeating a Cache Commander before scaling.");
            CommanderRewardStageScale = Config.Bind("Commanders", "Reward Stage Scale", 0.08f, "Extra reward multiplier per cleared stage.");
            CommanderRewardDifficultyScale = Config.Bind("Commanders", "Reward Difficulty Scale", 0.07f, "Extra reward multiplier per difficulty tier.");
            CommanderRewardPlayerScale = Config.Bind("Commanders", "Reward Player Scale", 0.15f, "Extra reward multiplier per additional alive player beyond one.");
            CommanderTetherDistance = Config.Bind("Commanders", "Commander Tether Distance", 54f, "Preferred max distance command elites can stray from their command zone before return logic engages.");
            RoleBudgetMultiplier = Config.Bind("V1 Roles", "Role Budget Multiplier", 1f, "Global multiplier for V1 role-driven assault spawn budgets.");
            LoneWolfPressureThreshold = Config.Bind("V1 Fairness", "Objective Attendance Threshold", 0.5f, "Minimum fraction of alive players expected to defend objective before lone-wolf pressure rises.");
            LoneWolfContestPenalty = Config.Bind("V1 Fairness", "Lone-Wolf Contest Penalty", 0.35f, "Maximum enemy contest weight multiplier from poor objective attendance.");
            MercyWindowSeconds = Config.Bind("V1 Fairness", "Revive Mercy Window Seconds", 8f, "Duration of temporary easing around down/revive events.");
            MercyCooldownSeconds = Config.Bind("V1 Fairness", "Revive Mercy Cooldown Seconds", 20f, "Minimum gap between revive-mercy windows.");
        }

        private void RegisterLanguageTokens()
        {
            LanguageAPI.Add("WARFRONT_NODE_CONTEXT", "Neutralize Command Elite");
            LanguageAPI.Add("WARFRONT_NODE_RELAY_NAME", "Relay Commander");
            LanguageAPI.Add("WARFRONT_NODE_FORGE_NAME", "Forge Commander");
            LanguageAPI.Add("WARFRONT_NODE_SIREN_NAME", "Siren Commander");
            LanguageAPI.Add("WARFRONT_NODE_SPAWNCACHE_NAME", "Cache Commander");
            LanguageAPI.Add("WARFRONT_STAGE_BANNER", "War Warnings: {0}, {1} - Anomaly: {2}");
        }
    }
}

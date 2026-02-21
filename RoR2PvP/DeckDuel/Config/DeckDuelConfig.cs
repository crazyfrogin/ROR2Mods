using BepInEx.Configuration;

namespace DeckDuel.Config
{
    public class DeckDuelConfig
    {
        // === Budget ===
        public ConfigEntry<int> Budget { get; }
        public ConfigEntry<int> MaxDeckSize { get; }
        public ConfigEntry<bool> AllowEquipment { get; }

        // === Tier Costs ===
        public ConfigEntry<int> CostWhite { get; }
        public ConfigEntry<int> CostGreen { get; }
        public ConfigEntry<int> CostRed { get; }
        public ConfigEntry<int> CostYellow { get; }
        public ConfigEntry<int> CostPurple { get; }
        public ConfigEntry<int> CostBlue { get; }
        public ConfigEntry<int> CostEquipment { get; }
        public ConfigEntry<int> CostLunarEquipment { get; }
        public ConfigEntry<int> CostDroneDefault { get; }

        // === Stacking ===
        public ConfigEntry<float> StackMultiplier2nd { get; }
        public ConfigEntry<float> StackMultiplier3rd { get; }

        // === Banlist ===
        public ConfigEntry<string> BannedItems { get; }
        public ConfigEntry<string> BannedEquipment { get; }

        // === Match Settings (FFA Stock Ring Duel) ===
        public ConfigEntry<int> MinPlayers { get; }
        public ConfigEntry<int> BestOf { get; }
        public ConfigEntry<int> StocksPerGame { get; }
        public ConfigEntry<float> GameDuration { get; }
        public ConfigEntry<int> TiebreakStocks { get; }
        public ConfigEntry<float> TiebreakDuration { get; }
        public ConfigEntry<float> WarmupDuration { get; }
        public ConfigEntry<float> SpawnMarkDuration { get; }
        public ConfigEntry<float> RespawnInvulnDuration { get; }
        public ConfigEntry<int> StartingCards { get; }
        public ConfigEntry<float> CardInterval { get; }

        // === AI Opponent ===
        public ConfigEntry<bool> EnableAIOpponent { get; }
        public ConfigEntry<string> AISurvivor { get; }

        // === Arena ===
        public ConfigEntry<string> PreferredScenes { get; }
        public ConfigEntry<float> ArenaRadius { get; }

        // === Ring Phases ===
        public ConfigEntry<float> PhaseAEnd { get; }
        public ConfigEntry<float> PhaseBEnd { get; }
        public ConfigEntry<float> PhaseBShrinkRate { get; }
        public ConfigEntry<float> PhaseCShrinkRate { get; }
        public ConfigEntry<float> PhaseBBaseDPS { get; }
        public ConfigEntry<float> PhaseCBaseDPS { get; }
        public ConfigEntry<float> OutsideRampPerSecond { get; }
        public ConfigEntry<float> RegenPauseDuration { get; }

        public DeckDuelConfig(ConfigFile config)
        {
            // Budget
            Budget = config.Bind("Budget", "Budget", 50,
                "Total point budget for deck building.");
            MaxDeckSize = config.Bind("Budget", "MaxDeckSize", 24,
                "Maximum number of cards (item stacks) in a deck.");
            AllowEquipment = config.Bind("Budget", "AllowEquipment", true,
                "Allow players to include one equipment in their deck.");

            // Tier Costs
            CostWhite = config.Bind("Tier Costs", "White", 1,
                "Point cost for White/Common items.");
            CostGreen = config.Bind("Tier Costs", "Green", 3,
                "Point cost for Green/Uncommon items.");
            CostRed = config.Bind("Tier Costs", "Red", 7,
                "Point cost for Red/Legendary items.");
            CostYellow = config.Bind("Tier Costs", "Yellow", 6,
                "Point cost for Yellow/Boss items.");
            CostPurple = config.Bind("Tier Costs", "Purple", 6,
                "Point cost for Purple/Void items.");
            CostBlue = config.Bind("Tier Costs", "Blue", 5,
                "Point cost for Blue/Lunar items.");
            CostEquipment = config.Bind("Tier Costs", "Equipment", 6,
                "Point cost for regular Equipment.");
            CostLunarEquipment = config.Bind("Tier Costs", "LunarEquipment", 7,
                "Point cost for Lunar Equipment.");
            CostDroneDefault = config.Bind("Tier Costs", "DroneDefault", 4,
                "Default point cost for drones (overridden per-drone in Drone Costs section).");

            // Stacking
            StackMultiplier2nd = config.Bind("Stacking", "SecondCopyMultiplier", 1.5f,
                "Cost multiplier for the 2nd copy of the same item (rounded up).");
            StackMultiplier3rd = config.Bind("Stacking", "ThirdPlusCopyMultiplier", 2.0f,
                "Cost multiplier for the 3rd+ copy of the same item.");

            // Banlist
            BannedItems = config.Bind("Banlist", "BannedItems", "",
                "Comma-separated internal item names to ban (e.g. 'ShieldOnly,LunarBadLuck').");
            BannedEquipment = config.Bind("Banlist", "BannedEquipment", "",
                "Comma-separated internal equipment names to ban.");

            // Match Settings (FFA Stock Ring Duel)
            MinPlayers = config.Bind("Match", "MinPlayers", 2,
                "Minimum number of players required to start a match (including AI in solo mode).");
            BestOf = config.Bind("Match", "BestOf", 3,
                "Best-of-N games to win a match (last player standing wins each game).");
            StocksPerGame = config.Bind("Match", "StocksPerGame", 3,
                "Number of lives (stocks) each player starts with per game.");
            GameDuration = config.Bind("Match", "GameDuration", 180f,
                "Game duration in seconds (3:00 hard cap).");
            TiebreakStocks = config.Bind("Match", "TiebreakStocks", 1,
                "Stocks per player in a tiebreak game.");
            TiebreakDuration = config.Bind("Match", "TiebreakDuration", 60f,
                "Tiebreak game duration in seconds (1:00).");
            WarmupDuration = config.Bind("Match", "WarmupDuration", 3f,
                "Brief warmup for positioning before the game clock starts.");
            SpawnMarkDuration = config.Bind("Match", "SpawnMarkDuration", 10f,
                "Duration in seconds both players are marked (visible through walls) on spawn.");
            RespawnInvulnDuration = config.Bind("Match", "RespawnInvulnDuration", 2f,
                "Invulnerability duration in seconds after respawning from a stock loss.");
            StartingCards = config.Bind("Match", "StartingCards", 6,
                "Number of cards dealt at game start.");
            CardInterval = config.Bind("Match", "CardInterval", 12f,
                "Seconds between each additional card being dealt.");

            // AI Opponent
            EnableAIOpponent = config.Bind("AI Opponent", "EnableAIOpponent", true,
                "When playing solo, spawn an AI-controlled opponent to duel against.");
            AISurvivor = config.Bind("AI Opponent", "AISurvivor", "",
                "Survivor body name for the AI opponent (e.g. 'CommandoBody'). Empty = mirror the player's survivor.");

            // Arena
            PreferredScenes = config.Bind("Arena", "PreferredScenes", "",
                "Comma-separated scene names to prefer for the arena (empty = use current stage).");
            ArenaRadius = config.Bind("Arena", "ArenaRadius", 60f,
                "Initial arena radius in world units (Phase A ring size).");

            // Ring Phases
            PhaseAEnd = config.Bind("Ring Phases", "PhaseAEnd", 30f,
                "Game-clock seconds when Phase A ends and Phase B begins (ring starts shrinking).");
            PhaseBEnd = config.Bind("Ring Phases", "PhaseBEnd", 105f,
                "Game-clock seconds when Phase B ends and Phase C begins (1:45).");
            PhaseBShrinkRate = config.Bind("Ring Phases", "PhaseBShrinkRate", 0.3f,
                "Arena radius shrink rate (units/sec) during Phase B.");
            PhaseCShrinkRate = config.Bind("Ring Phases", "PhaseCShrinkRate", 0.8f,
                "Arena radius shrink rate (units/sec) during Phase C.");
            PhaseBBaseDPS = config.Bind("Ring Phases", "PhaseBBaseDPS", 5f,
                "Base damage per second outside the ring during Phase B (light).");
            PhaseCBaseDPS = config.Bind("Ring Phases", "PhaseCBaseDPS", 20f,
                "Base damage per second outside the ring during Phase C (heavy).");
            OutsideRampPerSecond = config.Bind("Ring Phases", "OutsideRampPerSecond", 3f,
                "Additional DPS added per second spent continuously outside the ring.");
            RegenPauseDuration = config.Bind("Ring Phases", "RegenPauseDuration", 3f,
                "Seconds that health regen is paused after taking ring damage.");
        }

        public int GetTierCost(RoR2.ItemTier tier)
        {
            switch (tier)
            {
                case RoR2.ItemTier.Tier1: return CostWhite.Value;
                case RoR2.ItemTier.Tier2: return CostGreen.Value;
                case RoR2.ItemTier.Tier3: return CostRed.Value;
                case RoR2.ItemTier.Boss: return CostYellow.Value;
                case RoR2.ItemTier.Lunar: return CostBlue.Value;
                case RoR2.ItemTier.VoidTier1:
                case RoR2.ItemTier.VoidTier2:
                case RoR2.ItemTier.VoidTier3:
                case RoR2.ItemTier.VoidBoss:
                    return CostPurple.Value;
                default: return CostWhite.Value;
            }
        }

        public int ComputeStackCost(int baseCost, int copyNumber)
        {
            if (copyNumber <= 1) return baseCost;
            if (copyNumber == 2) return (int)System.Math.Ceiling(baseCost * StackMultiplier2nd.Value);
            return (int)System.Math.Ceiling(baseCost * StackMultiplier3rd.Value);
        }
    }
}

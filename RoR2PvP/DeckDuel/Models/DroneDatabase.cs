using System.Collections.Generic;
using RoR2;

namespace DeckDuel.Models
{
    public static class DroneDatabase
    {
        public struct DroneInfo
        {
            public string MasterPrefabName;
            public string DisplayName;
            public int Cost;
        }

        private static readonly List<DroneInfo> _drones = new List<DroneInfo>();
        private static readonly HashSet<string> _droneItemNames = new HashSet<string>();
        private static readonly Dictionary<string, DroneInfo> _byPrefabName = new Dictionary<string, DroneInfo>();

        private static readonly string[] KnownDroneMasters = new string[]
        {
            "DroneBackup",
            "DroneMissile",
            "Drone1",
            "Drone2",
            "EmergencyDrone",
            "FlameDrone",
            "MegaDrone",
            "EquipmentDrone",
            "Turret1",
            "DroneCommander"
        };

        private static readonly Dictionary<string, string> DroneFriendlyNames = new Dictionary<string, string>
        {
            { "DroneBackup", "Strike Drone (Backup)" },
            { "DroneMissile", "Missile Drone" },
            { "Drone1", "Gunner Drone" },
            { "Drone2", "Healing Drone" },
            { "EmergencyDrone", "Emergency Drone" },
            { "FlameDrone", "Incinerator Drone" },
            { "MegaDrone", "TC-280 Prototype" },
            { "EquipmentDrone", "Equipment Drone" },
            { "Turret1", "Gunner Turret" },
            { "DroneCommander", "Col. Droneman" }
        };

        private static readonly Dictionary<string, int> DroneDefaultCosts = new Dictionary<string, int>
        {
            { "DroneBackup", 3 },
            { "DroneMissile", 5 },
            { "Drone1", 2 },
            { "Drone2", 3 },
            { "EmergencyDrone", 5 },
            { "FlameDrone", 4 },
            { "MegaDrone", 7 },
            { "EquipmentDrone", 5 },
            { "Turret1", 3 },
            { "DroneCommander", 6 }
        };

        public static void Initialize()
        {
            _drones.Clear();
            _droneItemNames.Clear();
            _byPrefabName.Clear();

            int defaultCost = DeckDuelPlugin.Cfg.CostDroneDefault.Value;

            foreach (var masterName in KnownDroneMasters)
            {
                int cost = DroneDefaultCosts.ContainsKey(masterName) ? DroneDefaultCosts[masterName] : defaultCost;
                string displayName = DroneFriendlyNames.ContainsKey(masterName) ? DroneFriendlyNames[masterName] : masterName;

                var info = new DroneInfo
                {
                    MasterPrefabName = masterName,
                    DisplayName = displayName,
                    Cost = cost
                };

                _drones.Add(info);
                _byPrefabName[masterName] = info;
            }

            Log.Info($"DroneDatabase initialized with {_drones.Count} drone types.");
        }

        public static IReadOnlyList<DroneInfo> GetAllDrones() => _drones;

        public static bool IsDroneItem(ItemDef itemDef)
        {
            return _droneItemNames.Contains(itemDef.name);
        }

        public static int GetDroneCost(ItemDef itemDef)
        {
            return DeckDuelPlugin.Cfg.CostDroneDefault.Value;
        }

        public static int GetDroneCostByPrefab(string prefabName)
        {
            if (_byPrefabName.TryGetValue(prefabName, out var info))
                return info.Cost;
            return DeckDuelPlugin.Cfg.CostDroneDefault.Value;
        }

        public static string GetDroneDisplayName(string prefabName)
        {
            if (_byPrefabName.TryGetValue(prefabName, out var info))
                return info.DisplayName;
            return prefabName;
        }
    }
}

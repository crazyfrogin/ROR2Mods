using System.Collections.Generic;
using RoR2;

namespace DeckDuel.Models
{
    public static class ItemTierCosts
    {
        private static readonly HashSet<string> _bannedItems = new HashSet<string>();
        private static readonly HashSet<string> _bannedEquipment = new HashSet<string>();

        public static void Initialize()
        {
            _bannedItems.Clear();
            _bannedEquipment.Clear();

            var cfg = DeckDuelPlugin.Cfg;
            if (!string.IsNullOrWhiteSpace(cfg.BannedItems.Value))
            {
                foreach (var name in cfg.BannedItems.Value.Split(','))
                {
                    var trimmed = name.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        _bannedItems.Add(trimmed);
                }
            }
            if (!string.IsNullOrWhiteSpace(cfg.BannedEquipment.Value))
            {
                foreach (var name in cfg.BannedEquipment.Value.Split(','))
                {
                    var trimmed = name.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        _bannedEquipment.Add(trimmed);
                }
            }

            Log.Info($"ItemTierCosts initialized. Banned items: {_bannedItems.Count}, Banned equipment: {_bannedEquipment.Count}");
        }

        public static int GetBaseCostForItem(ItemIndex itemIndex)
        {
            var itemDef = ItemCatalog.GetItemDef(itemIndex);
            if (itemDef == null) return 999;

            if (DroneDatabase.IsDroneItem(itemDef))
            {
                return DroneDatabase.GetDroneCost(itemDef);
            }

            return DeckDuelPlugin.Cfg.GetTierCost(itemDef.tier);
        }

        public static int GetBaseCostForEquipment(EquipmentIndex equipIndex)
        {
            var equipDef = EquipmentCatalog.GetEquipmentDef(equipIndex);
            if (equipDef == null) return 999;
            return equipDef.isLunar
                ? DeckDuelPlugin.Cfg.CostLunarEquipment.Value
                : DeckDuelPlugin.Cfg.CostEquipment.Value;
        }

        public static int ComputeStackCost(int baseCost, int copyNumber)
        {
            return DeckDuelPlugin.Cfg.ComputeStackCost(baseCost, copyNumber);
        }

        public static bool IsItemBanned(ItemIndex itemIndex)
        {
            var itemDef = ItemCatalog.GetItemDef(itemIndex);
            if (itemDef == null) return true;
            return _bannedItems.Contains(itemDef.name);
        }

        public static bool IsEquipmentBanned(EquipmentIndex equipIndex)
        {
            var equipDef = EquipmentCatalog.GetEquipmentDef(equipIndex);
            if (equipDef == null) return true;
            return _bannedEquipment.Contains(equipDef.name);
        }

        public static bool IsItemPickable(ItemDef itemDef)
        {
            if (itemDef == null) return false;
            if (itemDef.hidden) return false;
            if (itemDef.tier == ItemTier.NoTier) return false;
            if (_bannedItems.Contains(itemDef.name)) return false;
            return true;
        }

        public static bool IsEquipmentPickable(EquipmentDef equipDef)
        {
            if (equipDef == null) return false;
            if (_bannedEquipment.Contains(equipDef.name)) return false;
            return true;
        }
    }
}

using System.Collections.Generic;
using RoR2;

namespace DeckDuel.Models
{
    public static class DeckValidator
    {
        public struct ValidationResult
        {
            public bool IsValid;
            public string Reason;

            public static ValidationResult Valid() => new ValidationResult { IsValid = true, Reason = string.Empty };
            public static ValidationResult Invalid(string reason) => new ValidationResult { IsValid = false, Reason = reason };
        }

        public static ValidationResult Validate(Deck deck)
        {
            var cfg = DeckDuelPlugin.Cfg;

            // Check deck size
            if (deck.Cards.Count > cfg.MaxDeckSize.Value)
                return ValidationResult.Invalid($"Deck has {deck.Cards.Count} cards, max is {cfg.MaxDeckSize.Value}.");

            if (deck.Cards.Count == 0)
                return ValidationResult.Invalid("Deck is empty.");

            // Check equipment
            if ((EquipmentIndex)deck.EquipmentIndex != EquipmentIndex.None)
            {
                if (!cfg.AllowEquipment.Value)
                    return ValidationResult.Invalid("Equipment is not allowed in this match.");

                if (ItemTierCosts.IsEquipmentBanned((EquipmentIndex)deck.EquipmentIndex))
                    return ValidationResult.Invalid("Deck contains banned equipment.");
            }

            // Validate each card and recalculate costs
            var itemCopyCounts = new Dictionary<int, int>();
            int recalcTotalCost = 0;

            foreach (var card in deck.Cards)
            {
                // Check bans
                if (card.CardType == DeckCardType.Item)
                {
                    if (ItemTierCosts.IsItemBanned((ItemIndex)card.ItemOrEquipIndex))
                        return ValidationResult.Invalid($"Deck contains banned item: {card.GetDisplayName()}.");
                }

                // Track copies for stacking cost
                int key = GetCardKey(card);
                if (!itemCopyCounts.ContainsKey(key))
                    itemCopyCounts[key] = 0;
                itemCopyCounts[key]++;

                // Recompute cost
                int baseCost = GetBaseCardCost(card);
                int stackCost = ItemTierCosts.ComputeStackCost(baseCost, itemCopyCounts[key]);
                recalcTotalCost += stackCost;
            }

            // Add equipment cost
            if ((EquipmentIndex)deck.EquipmentIndex != EquipmentIndex.None)
            {
                recalcTotalCost += ItemTierCosts.GetBaseCostForEquipment((EquipmentIndex)deck.EquipmentIndex);
            }

            // Check budget
            if (recalcTotalCost > cfg.Budget.Value)
                return ValidationResult.Invalid($"Deck costs {recalcTotalCost} points, budget is {cfg.Budget.Value}.");

            return ValidationResult.Valid();
        }

        private static int GetCardKey(DeckCard card)
        {
            // Unique key per card identity (type + index/prefab)
            switch (card.CardType)
            {
                case DeckCardType.Item:
                    return card.ItemOrEquipIndex;
                case DeckCardType.Drone:
                    return card.DroneMasterPrefabName.GetHashCode();
                default:
                    return card.ItemOrEquipIndex + 100000;
            }
        }

        private static int GetBaseCardCost(DeckCard card)
        {
            switch (card.CardType)
            {
                case DeckCardType.Item:
                    return ItemTierCosts.GetBaseCostForItem((ItemIndex)card.ItemOrEquipIndex);
                case DeckCardType.Drone:
                    return DroneDatabase.GetDroneCostByPrefab(card.DroneMasterPrefabName);
                default:
                    return 1;
            }
        }
    }
}

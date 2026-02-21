using System;
using System.IO;
using RoR2;

namespace DeckDuel.Models
{
    public enum DeckCardType : byte
    {
        Item = 0,
        Equipment = 1,
        Drone = 2
    }

    [Serializable]
    public class DeckCard
    {
        public DeckCardType CardType;
        public int ItemOrEquipIndex;
        public int CopyNumber;
        public int ResolvedCost;
        public string DroneMasterPrefabName;

        public DeckCard() { }

        public DeckCard(DeckCardType type, int index, int copyNumber, int resolvedCost, string dronePrefab = null)
        {
            CardType = type;
            ItemOrEquipIndex = index;
            CopyNumber = copyNumber;
            ResolvedCost = resolvedCost;
            DroneMasterPrefabName = dronePrefab ?? string.Empty;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)CardType);
            writer.Write(ItemOrEquipIndex);
            writer.Write(CopyNumber);
            writer.Write(ResolvedCost);
            writer.Write(DroneMasterPrefabName ?? string.Empty);
        }

        public static DeckCard Deserialize(BinaryReader reader)
        {
            var card = new DeckCard();
            card.CardType = (DeckCardType)reader.ReadByte();
            card.ItemOrEquipIndex = reader.ReadInt32();
            card.CopyNumber = reader.ReadInt32();
            card.ResolvedCost = reader.ReadInt32();
            card.DroneMasterPrefabName = reader.ReadString();
            return card;
        }

        public string GetDisplayName()
        {
            switch (CardType)
            {
                case DeckCardType.Item:
                    var itemDef = ItemCatalog.GetItemDef((ItemIndex)ItemOrEquipIndex);
                    return itemDef != null ? Language.GetString(itemDef.nameToken) : "Unknown Item";
                case DeckCardType.Equipment:
                    var equipDef = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)ItemOrEquipIndex);
                    return equipDef != null ? Language.GetString(equipDef.nameToken) : "Unknown Equipment";
                case DeckCardType.Drone:
                    return DroneDatabase.GetDroneDisplayName(DroneMasterPrefabName);
                default:
                    return "Unknown";
            }
        }
    }
}

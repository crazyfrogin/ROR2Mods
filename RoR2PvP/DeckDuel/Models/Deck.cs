using System;
using System.Collections.Generic;
using System.IO;
using RoR2;

namespace DeckDuel.Models
{
    [Serializable]
    public class Deck
    {
        public List<DeckCard> Cards = new List<DeckCard>();
        public int EquipmentIndex = (int)RoR2.EquipmentIndex.None;
        public int SurvivorIndex = 0;
        public int TotalCost;

        public int EquipmentCost;

        public void RecalculateTotalCost()
        {
            TotalCost = 0;
            foreach (var card in Cards)
            {
                TotalCost += card.ResolvedCost;
            }
            TotalCost += EquipmentCost;
        }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(SurvivorIndex);
                writer.Write(EquipmentIndex);
                writer.Write(EquipmentCost);
                writer.Write(Cards.Count);
                foreach (var card in Cards)
                {
                    card.Serialize(writer);
                }
                return ms.ToArray();
            }
        }

        public static Deck Deserialize(byte[] data)
        {
            var deck = new Deck();
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                deck.SurvivorIndex = reader.ReadInt32();
                deck.EquipmentIndex = reader.ReadInt32();
                deck.EquipmentCost = reader.ReadInt32();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    deck.Cards.Add(DeckCard.Deserialize(reader));
                }
            }
            deck.RecalculateTotalCost();
            return deck;
        }
    }
}

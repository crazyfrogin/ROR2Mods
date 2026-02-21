using System;
using System.Collections.Generic;
using DeckDuel.Models;
using DeckDuel.Networking;
using R2API.Networking.Interfaces;
using R2API.Networking;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace DeckDuel.Match
{
    public class CardDealer
    {
        private Dictionary<NetworkInstanceId, Queue<DeckCard>> _cardQueues = new Dictionary<NetworkInstanceId, Queue<DeckCard>>();
        private Dictionary<NetworkInstanceId, List<GameObject>> _spawnedDrones = new Dictionary<NetworkInstanceId, List<GameObject>>();
        private Dictionary<NetworkInstanceId, int> _cardsDealt = new Dictionary<NetworkInstanceId, int>();
        private Dictionary<NetworkInstanceId, int> _totalCards = new Dictionary<NetworkInstanceId, int>();

        public void SetupDeck(CharacterMaster player, Deck deck)
        {
            Log.Info($">>> CardDealer.SetupDeck: player={player?.name ?? "NULL"}, netIdentity={player?.networkIdentity != null}, deckCards={deck?.Cards?.Count}");
            if (player == null || player.networkIdentity == null)
            {
                Log.Error($"CardDealer.SetupDeck: ABORTING — player={player?.name ?? "NULL"}, networkIdentity={(player?.networkIdentity != null ? "OK" : "NULL")}");
                return;
            }
            var netId = player.networkIdentity.netId;

            var queue = new Queue<DeckCard>();
            foreach (var card in deck.Cards)
            {
                queue.Enqueue(card);
            }
            _cardQueues[netId] = queue;
            _spawnedDrones[netId] = new List<GameObject>();
            _cardsDealt[netId] = 0;
            _totalCards[netId] = deck.Cards.Count;

            // Handle equipment separately — dealt at round start
            if ((EquipmentIndex)deck.EquipmentIndex != EquipmentIndex.None)
            {
                var body = player.GetBody();
                if (body != null && body.inventory != null)
                {
                    body.inventory.SetEquipmentIndex((EquipmentIndex)deck.EquipmentIndex, false);
                    Log.Info($"Equipped {EquipmentCatalog.GetEquipmentDef((EquipmentIndex)deck.EquipmentIndex)?.name} on {body.GetUserName()}");
                }
            }

            Log.Info($"CardDealer: deck loaded for {player.GetBody()?.GetUserName()} with {queue.Count} cards.");
        }

        public bool DealCards(CharacterMaster player, int count)
        {
            // Unity destroyed objects fool C#'s ?. operator — use explicit check
            if (player == null || !player)
            {
                Log.Error("CardDealer.DealCards: ABORTING — player is null or destroyed.");
                return false;
            }
            Log.Info($">>> CardDealer.DealCards: player={player.name}, count={count}, netIdentity={player.networkIdentity != null}");
            if (player.networkIdentity == null)
            {
                Log.Error($"CardDealer.DealCards: ABORTING — player={player.name}, networkIdentity=NULL");
                return false;
            }
            var netId = player.networkIdentity.netId;

            if (!_cardQueues.ContainsKey(netId)) return false;
            var queue = _cardQueues[netId];

            int dealt = 0;
            for (int i = 0; i < count && queue.Count > 0; i++)
            {
                var card = queue.Dequeue();
                ApplyCard(player, card);
                _cardsDealt[netId]++;
                dealt++;

                // Broadcast card deal to all clients for HUD
                new DealCardMessage(netId.Value, card).Send(NetworkDestination.Clients);
            }

            if (dealt > 0)
                Log.Info($"Dealt {dealt} card(s) to {player.GetBody()?.GetUserName()}. Remaining: {queue.Count}");

            return dealt > 0;
        }

        public void DealAllRemaining(CharacterMaster player)
        {
            if (player == null || player.networkIdentity == null) return;
            var netId = player.networkIdentity.netId;

            if (!_cardQueues.ContainsKey(netId)) return;
            var queue = _cardQueues[netId];

            int count = queue.Count;
            DealCards(player, count);
            Log.Info($"Dumped all {count} remaining cards to {player.GetBody()?.GetUserName()}.");
        }

        public int GetCardsRemaining(NetworkInstanceId netId)
        {
            if (_cardQueues.TryGetValue(netId, out var queue))
                return queue.Count;
            return 0;
        }

        public int GetCardsDealt(NetworkInstanceId netId)
        {
            if (_cardsDealt.TryGetValue(netId, out var dealt))
                return dealt;
            return 0;
        }

        private void ApplyCard(CharacterMaster player, DeckCard card)
        {
            var body = player.GetBody();
            if (body == null) return;

            switch (card.CardType)
            {
                case DeckCardType.Item:
                    body.inventory.GiveItemPermanent((ItemIndex)card.ItemOrEquipIndex, 1);
                    Log.Debug($"Gave item {ItemCatalog.GetItemDef((ItemIndex)card.ItemOrEquipIndex)?.name} to {body.GetUserName()}");
                    break;

                case DeckCardType.Drone:
                    SpawnDrone(player, card);
                    break;

                default:
                    Log.Warning($"Unknown card type: {card.CardType}");
                    break;
            }
        }

        private void SpawnDrone(CharacterMaster owner, DeckCard card)
        {
            if (string.IsNullOrEmpty(card.DroneMasterPrefabName)) return;

            var body = owner.GetBody();
            if (body == null) return;

            string masterName = card.DroneMasterPrefabName + "Master";
            var masterPrefab = MasterCatalog.FindMasterPrefab(masterName);
            if (masterPrefab == null)
            {
                // Try without "Master" suffix
                masterPrefab = MasterCatalog.FindMasterPrefab(card.DroneMasterPrefabName);
            }

            if (masterPrefab == null)
            {
                Log.Warning($"Could not find drone master prefab: {card.DroneMasterPrefabName}");
                return;
            }

            var summon = new MasterSummon
            {
                masterPrefab = masterPrefab,
                position = body.transform.position + Vector3.up * 3f,
                rotation = body.transform.rotation,
                summonerBodyObject = body.gameObject,
                ignoreTeamMemberLimit = true
            };

            var droneResult = summon.Perform();
            if (droneResult != null)
            {
                var netId = owner.networkIdentity.netId;
                if (_spawnedDrones.ContainsKey(netId))
                {
                    _spawnedDrones[netId].Add(droneResult.gameObject);
                }
                Log.Info($"Spawned drone {card.DroneMasterPrefabName} for {body.GetUserName()}");
            }
        }

        public void CleanupRound()
        {
            // Destroy all spawned drones
            foreach (var kvp in _spawnedDrones)
            {
                foreach (var droneObj in kvp.Value)
                {
                    if (droneObj != null)
                    {
                        var master = droneObj.GetComponent<CharacterMaster>();
                        if (master != null)
                        {
                            var droneBody = master.GetBody();
                            if (droneBody != null)
                            {
                                droneBody.healthComponent.Suicide();
                            }
                        }
                        UnityEngine.Object.Destroy(droneObj);
                    }
                }
                kvp.Value.Clear();
            }

            // Strip all items from players
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.master == null) continue;
                StripInventory(pc.master);
            }

            // Also strip AI opponent if present
            var aiMaster = DeckDuelPlugin.Instance.AIOpponent?.AIMaster;
            if (aiMaster != null)
            {
                StripInventory(aiMaster);
            }

            _cardQueues.Clear();
            _cardsDealt.Clear();
            _totalCards.Clear();

            Log.Info("CardDealer: round cleanup complete.");
        }

        private void StripInventory(CharacterMaster master)
        {
            var body = master.GetBody();
            if (body != null && body.inventory != null)
            {
                for (int i = 0; i < ItemCatalog.itemCount; i++)
                {
                    var idx = (ItemIndex)i;
                    int count = body.inventory.GetItemCountPermanent(idx);
                    if (count > 0)
                        body.inventory.RemoveItemPermanent(idx, count);
                }
                body.inventory.SetEquipmentIndex(EquipmentIndex.None, true);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using DeckDuel.Models;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace DeckDuel.Match
{
    public class AIOpponent : IDisposable
    {
        private CharacterMaster _aiMaster;
        private Deck _aiDeck;

        public CharacterMaster AIMaster => _aiMaster;
        public Deck AIDeck => _aiDeck;
        public bool IsSpawned => _aiMaster != null;

        public void SpawnAI(CharacterMaster playerMaster)
        {
            if (!NetworkServer.active)
            {
                Log.Error("AIOpponent.SpawnAI: Not server, aborting.");
                return;
            }
            if (_aiMaster != null)
            {
                Log.Warning("AIOpponent.SpawnAI: Already spawned, skipping.");
                return;
            }

            // Determine which body prefab to use for the AI
            GameObject bodyPrefab = ResolveAIBodyPrefab(playerMaster);
            if (bodyPrefab == null)
            {
                Log.Error("AIOpponent.SpawnAI: Could not resolve body prefab. playerMaster=" +
                    (playerMaster != null ? playerMaster.name : "null") +
                    ", bodyPrefab=" + (playerMaster?.bodyPrefab != null ? playerMaster.bodyPrefab.name : "null"));
                return;
            }

            // Find a real master prefab that has BaseAI + AISkillDrivers already configured
            GameObject masterPrefab = FindMasterPrefabForBody(bodyPrefab);
            if (masterPrefab == null)
            {
                Log.Error("AIOpponent.SpawnAI: Could not find any master prefab.");
                return;
            }

            // Find spawn position near the player
            Vector3 spawnPos = Vector3.zero;
            if (playerMaster?.GetBody() != null)
            {
                spawnPos = playerMaster.GetBody().transform.position + Vector3.right * 10f;
            }
            else
            {
                var spawnPoints = SpawnPoint.readOnlyInstancesList;
                if (spawnPoints != null && spawnPoints.Count > 0)
                {
                    spawnPos = spawnPoints[0].transform.position;
                }
            }

            Log.Info($"AIOpponent.SpawnAI: Using MasterSummon. masterPrefab={masterPrefab.name}, bodyPrefab={bodyPrefab.name}, pos={spawnPos}");

            // Use MasterSummon — the proven RoR2 spawning pattern (same as drone code)
            try
            {
                var summon = new MasterSummon
                {
                    masterPrefab = masterPrefab,
                    position = spawnPos,
                    rotation = Quaternion.identity,
                    ignoreTeamMemberLimit = true,
                    teamIndexOverride = DeckDuelPlugin.Instance.PvPSetup.GetTeamForPlayer(1)
                };

                _aiMaster = summon.Perform();
            }
            catch (Exception ex)
            {
                Log.Error($"AIOpponent.SpawnAI: MasterSummon threw exception: {ex}");
                return;
            }

            if (_aiMaster == null)
            {
                Log.Error("AIOpponent.SpawnAI: MasterSummon.Perform() returned null!");
                return;
            }

            // Prevent master from being destroyed when the body dies — we need it alive for respawns
            _aiMaster.destroyOnBodyDeath = false;

            Log.Info($"AIOpponent.SpawnAI: MasterSummon succeeded. master={_aiMaster.name}, netId={_aiMaster.networkIdentity?.netId}");

            // If the master's default body doesn't match what we want, override it
            if (_aiMaster.bodyPrefab != bodyPrefab)
            {
                Log.Info($"AIOpponent.SpawnAI: Overriding body from {_aiMaster.bodyPrefab?.name} to {bodyPrefab.name}");
                var currentBody = _aiMaster.GetBody();
                if (currentBody != null)
                {
                    currentBody.healthComponent.Suicide();
                }
                _aiMaster.bodyPrefab = bodyPrefab;
                _aiMaster.Respawn(spawnPos, Quaternion.identity);
            }

            // Configure AI targeting
            var baseAI = _aiMaster.gameObject.GetComponent<BaseAI>();
            if (baseAI != null)
            {
                baseAI.fullVision = true;
                baseAI.neverRetaliateFriendlies = true;

                if (playerMaster?.GetBodyObject() != null)
                {
                    baseAI.currentEnemy = new BaseAI.Target(baseAI);
                    baseAI.currentEnemy.gameObject = playerMaster.GetBodyObject();
                    baseAI.enemyAttention = float.MaxValue;
                    Log.Info("AIOpponent.SpawnAI: AI target set to player.");
                }
                else
                {
                    Log.Warning("AIOpponent.SpawnAI: Player has no body object, AI target not set yet.");
                }
            }
            else
            {
                Log.Warning("AIOpponent.SpawnAI: No BaseAI on spawned master.");
            }

            var aiBody = _aiMaster.GetBody();
            Log.Info($"AIOpponent.SpawnAI: COMPLETE. Body={aiBody?.name ?? "NULL"}, alive={aiBody?.healthComponent?.alive}");
        }

        public Deck GenerateRandomDeck()
        {
            _aiDeck = new Deck();
            var cfg = DeckDuelPlugin.Cfg;
            int budget = cfg.Budget.Value;
            int maxCards = cfg.MaxDeckSize.Value;
            int remaining = budget;

            var rng = new System.Random();

            // Collect all pickable items
            var pickableItems = new List<ItemIndex>();
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                var itemDef = ItemCatalog.GetItemDef((ItemIndex)i);
                if (itemDef == null) continue;
                if (!ItemTierCosts.IsItemPickable(itemDef)) continue;
                if (itemDef.tier == ItemTier.NoTier) continue;
                if (itemDef.ContainsTag(ItemTag.WorldUnique)) continue;
                pickableItems.Add((ItemIndex)i);
            }

            Log.Info($"AIOpponent.GenerateRandomDeck: {pickableItems.Count} pickable items found, budget={budget}, maxCards={maxCards}");

            if (pickableItems.Count == 0)
            {
                Log.Warning("AIOpponent.GenerateRandomDeck: No pickable items!");
                return _aiDeck;
            }

            var copyCounts = new Dictionary<int, int>();
            int attempts = 0;
            while (_aiDeck.Cards.Count < maxCards && remaining > 0 && attempts < 200)
            {
                attempts++;
                var itemIndex = pickableItems[rng.Next(pickableItems.Count)];
                int key = (int)itemIndex;

                if (!copyCounts.ContainsKey(key)) copyCounts[key] = 0;
                int nextCopy = copyCounts[key] + 1;
                if (nextCopy > 3) continue;

                int baseCost = ItemTierCosts.GetBaseCostForItem(itemIndex);
                int stackCost = ItemTierCosts.ComputeStackCost(baseCost, nextCopy);
                if (stackCost > remaining) continue;

                copyCounts[key] = nextCopy;
                remaining -= stackCost;
                var card = new DeckCard(DeckCardType.Item, (int)itemIndex, nextCopy, stackCost);
                _aiDeck.Cards.Add(card);
            }

            _aiDeck.RecalculateTotalCost();
            Log.Info($"AIOpponent.GenerateRandomDeck: Built deck with {_aiDeck.Cards.Count} cards, cost={_aiDeck.TotalCost}/{budget}");
            return _aiDeck;
        }

        public void UpdateAITarget(CharacterMaster playerMaster)
        {
            if (_aiMaster == null) return;
            var baseAI = _aiMaster.gameObject.GetComponent<BaseAI>();
            if (baseAI == null) return;

            var playerBody = playerMaster?.GetBodyObject();
            if (playerBody != null)
            {
                baseAI.currentEnemy = new BaseAI.Target(baseAI);
                baseAI.currentEnemy.gameObject = playerBody;
                baseAI.enemyAttention = float.MaxValue;
            }
        }

        public void DespawnAI()
        {
            if (_aiMaster != null)
            {
                try
                {
                    _aiMaster.TrueKill();
                }
                catch (Exception ex)
                {
                    Log.Warning($"AIOpponent.DespawnAI: TrueKill failed: {ex.Message}");
                    // Fallback: try destroying directly
                    try
                    {
                        var body = _aiMaster.GetBody();
                        if (body != null) body.healthComponent.Suicide();
                        if (NetworkServer.active)
                            NetworkServer.Destroy(_aiMaster.gameObject);
                    }
                    catch { }
                }
            }

            _aiMaster = null;
            _aiDeck = null;
            Log.Info("AIOpponent despawned.");
        }

        private GameObject ResolveAIBodyPrefab(CharacterMaster playerMaster)
        {
            var cfg = DeckDuelPlugin.Cfg;

            // If a specific AI survivor is configured, use that
            if (!string.IsNullOrWhiteSpace(cfg.AISurvivor.Value))
            {
                var prefab = BodyCatalog.FindBodyPrefab(cfg.AISurvivor.Value);
                if (prefab != null)
                {
                    Log.Info($"AIOpponent.ResolveAIBodyPrefab: Using configured survivor '{cfg.AISurvivor.Value}'");
                    return prefab;
                }
                Log.Warning($"AIOpponent.ResolveAIBodyPrefab: Configured '{cfg.AISurvivor.Value}' not found, falling back.");
            }

            // Mirror the player's survivor
            if (playerMaster != null && playerMaster.bodyPrefab != null)
            {
                Log.Info($"AIOpponent.ResolveAIBodyPrefab: Mirroring player body '{playerMaster.bodyPrefab.name}'");
                return playerMaster.bodyPrefab;
            }

            // Fallback to Commando
            Log.Warning("AIOpponent.ResolveAIBodyPrefab: No player body, falling back to CommandoBody.");
            return BodyCatalog.FindBodyPrefab("CommandoBody");
        }

        private GameObject FindMasterPrefabForBody(GameObject bodyPrefab)
        {
            string bodyName = bodyPrefab.name;
            string baseName = bodyName.Replace("Body", "");

            // RoR2 convention: "CommandoBody" → "CommandoMonsterMaster"
            var masterPrefab = MasterCatalog.FindMasterPrefab(baseName + "MonsterMaster");
            if (masterPrefab != null)
            {
                Log.Info($"AIOpponent.FindMasterPrefab: Found '{baseName}MonsterMaster' for '{bodyName}'");
                return masterPrefab;
            }

            // Fallback to CommandoMonsterMaster
            masterPrefab = MasterCatalog.FindMasterPrefab("CommandoMonsterMaster");
            if (masterPrefab != null)
            {
                Log.Warning($"AIOpponent.FindMasterPrefab: No master for '{bodyName}', using CommandoMonsterMaster.");
                return masterPrefab;
            }

            // Last resort
            masterPrefab = MasterCatalog.FindMasterPrefab("LemurianMaster");
            Log.Warning($"AIOpponent.FindMasterPrefab: Last resort LemurianMaster = {(masterPrefab != null ? "found" : "NULL")}");
            return masterPrefab;
        }

        public void Dispose()
        {
            DespawnAI();
        }
    }
}

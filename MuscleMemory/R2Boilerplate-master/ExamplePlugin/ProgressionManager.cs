using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace MuscleMemory
{
    internal sealed class ProgressionManager
    {
        private readonly MuscleMemoryConfig _config;
        private readonly MilestoneSystem _milestones;

        internal readonly Dictionary<CharacterMaster, PlayerProgressState> PlayerStates =
            new Dictionary<CharacterMaster, PlayerProgressState>();

        internal readonly Dictionary<NetworkInstanceId, ReplicatedLevelState> ReplicatedStates =
            new Dictionary<NetworkInstanceId, ReplicatedLevelState>();

        private readonly HashSet<CharacterMaster> _activeMasterScratch = new HashSet<CharacterMaster>();

        internal ProgressionManager(MuscleMemoryConfig config, MilestoneSystem milestones)
        {
            _config = config;
            _milestones = milestones;
        }

        internal void HandleRunTransition()
        {
            PlayerStates.Clear();
            ReplicatedStates.Clear();
        }

        internal void ServerTick(float now)
        {
            _activeMasterScratch.Clear();

            int playerCount = PlayerCharacterMasterController.instances.Count;
            for (int i = 0; i < playerCount; i++)
            {
                PlayerCharacterMasterController player = PlayerCharacterMasterController.instances[i];
                if (!player || !player.master)
                {
                    continue;
                }

                CharacterMaster master = player.master;
                _activeMasterScratch.Add(master);

                PlayerProgressState state = GetOrCreateProgressState(master);
                TickPlayerProgress(state, now);
            }

            PruneDisconnectedStates();
        }

        internal PlayerProgressState GetOrCreateProgressState(CharacterMaster master)
        {
            if (!PlayerStates.TryGetValue(master, out PlayerProgressState state))
            {
                state = new PlayerProgressState(master);
                PlayerStates[master] = state;
            }

            return state;
        }

        internal bool TryGetTrackedState(CharacterBody attackerBody, out PlayerProgressState state)
        {
            state = null;
            if (!attackerBody || !attackerBody.master)
            {
                return false;
            }

            CharacterMaster master = attackerBody.master;
            if (!master.playerCharacterMasterController)
            {
                return false;
            }

            state = GetOrCreateProgressState(master);
            return true;
        }

        internal bool TryGetAttributedSlot(PlayerProgressState state, float now, out SkillSlotKind slot, bool allowPrimaryFallback = false)
        {
            if (state != null && now - state.LastActivatedTime <= Mathf.Max(0.1f, _config.AttributionWindowSeconds.Value))
            {
                slot = state.LastActivatedSlot;
                return true;
            }

            slot = SkillSlotKind.Primary;
            return allowPrimaryFallback;
        }

        internal bool TryGetEffectiveLevels(CharacterBody body, out int primary, out int secondary,
            out int utility, out int special, out bool flowActive)
        {
            primary = 0;
            secondary = 0;
            utility = 0;
            special = 0;
            flowActive = false;

            if (body == null || !body.master)
            {
                return false;
            }

            if (PlayerStates.TryGetValue(body.master, out PlayerProgressState serverState))
            {
                primary = serverState.Slots[(int)SkillSlotKind.Primary].Level;
                secondary = serverState.Slots[(int)SkillSlotKind.Secondary].Level;
                utility = serverState.Slots[(int)SkillSlotKind.Utility].Level;
                special = serverState.Slots[(int)SkillSlotKind.Special].Level;
                flowActive = Time.fixedTime <= serverState.FlowWindowEnd;
                return true;
            }

            NetworkInstanceId netId = body.master.netId;
            if (netId != NetworkInstanceId.Invalid && ReplicatedStates.TryGetValue(netId, out ReplicatedLevelState replicated))
            {
                primary = replicated.Levels[(int)SkillSlotKind.Primary];
                secondary = replicated.Levels[(int)SkillSlotKind.Secondary];
                utility = replicated.Levels[(int)SkillSlotKind.Utility];
                special = replicated.Levels[(int)SkillSlotKind.Special];
                flowActive = replicated.FlowActive;
                return true;
            }

            return false;
        }

        internal bool TryGetEffectiveSlotLevel(CharacterBody body, SkillSlotKind slot, out int level)
        {
            if (TryGetEffectiveLevels(body, out int primary, out int secondary, out int utility, out int special, out _))
            {
                switch (slot)
                {
                    case SkillSlotKind.Primary: level = primary; return true;
                    case SkillSlotKind.Secondary: level = secondary; return true;
                    case SkillSlotKind.Utility: level = utility; return true;
                    case SkillSlotKind.Special: level = special; return true;
                }
            }

            level = 0;
            return false;
        }

        internal bool TryGetLevelProgress(CharacterBody body, SkillSlotKind slot, out float progress)
        {
            progress = 0f;

            if (body == null || !body.master)
            {
                return false;
            }

            if (PlayerStates.TryGetValue(body.master, out PlayerProgressState serverState))
            {
                SlotProgress sp = serverState.Slots[(int)slot];
                progress = CalculateProgressFraction(sp.Proficiency, sp.Level);
                return true;
            }

            NetworkInstanceId netId = body.master.netId;
            if (netId != NetworkInstanceId.Invalid && ReplicatedStates.TryGetValue(netId, out ReplicatedLevelState replicated))
            {
                progress = replicated.Progress[(int)slot];
                return true;
            }

            return false;
        }

        internal float CalculateProgressFraction(double proficiency, int currentLevel)
        {
            double currentReq = currentLevel > 0 ? GetRequiredProficiencyForLevel(currentLevel) : 0d;
            double nextReq = GetRequiredProficiencyForLevel(currentLevel + 1);
            double range = nextReq - currentReq;
            if (range <= 0d)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)((proficiency - currentReq) / range));
        }

        internal void AddProficiencySplitEvenly(PlayerProgressState state, double totalAmount)
        {
            if (!NetworkServer.active || state == null || totalAmount <= 0d)
            {
                return;
            }

            double perSlot = totalAmount / Constants.SlotCount;
            for (int i = 0; i < Constants.SlotCount; i++)
            {
                AddProficiency(state, (SkillSlotKind)i, perSlot);
            }
        }

        internal void AddProficiency(PlayerProgressState state, SkillSlotKind slot, double amount)
        {
            if (!NetworkServer.active || state == null || amount <= 0d)
            {
                return;
            }

            SlotProgress slotProgress = state.Slots[(int)slot];
            double adjustedAmount = amount;

            float survivorScale = Mathf.Max(0.01f, _config.SurvivorScalingMultiplier.Value);
            adjustedAmount *= survivorScale;

            int lowLevelEaseUntil = Math.Max(0, _config.LowLevelEaseUntil.Value);
            if (slotProgress.Level < lowLevelEaseUntil)
            {
                adjustedAmount *= Mathf.Max(1f, _config.LowLevelXpMultiplier.Value);
            }

            slotProgress.Proficiency += adjustedAmount;

            int previousLevel = slotProgress.Level;
            int nextLevel = CalculateLevel(slotProgress.Proficiency);
            if (nextLevel != previousLevel)
            {
                slotProgress.Level = nextLevel;

                if (state.LastBody)
                {
                    state.LastBody.MarkAllStatsDirty();
                }

                if (nextLevel > previousLevel)
                {
                    PlayLevelUpFeedback(state.LastBody);

                    string playerName = GetDisplayName(state);
                    if (_config.BroadcastAllLevelUps.Value)
                    {
                        Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                        {
                            baseToken = $"<style=cIsUtility>{playerName}</style> leveled <style=cIsDamage>{slot}</style> to <style=cIsHealing>Lv {nextLevel}</style>."
                        });
                    }

                    if (_config.BroadcastMilestones.Value)
                    {
                        _milestones.BroadcastMilestoneUnlocks(playerName, slot, previousLevel, nextLevel);
                    }

                    Log.Info($"{playerName} {slot} reached level {nextLevel} (P={slotProgress.Proficiency:0.0}).");
                }
            }
        }

        internal void TickDecay(PlayerProgressState state, float now)
        {
            if (!_config.EnableProficiencyDecay.Value)
            {
                return;
            }

            float idleThreshold = Mathf.Max(1f, _config.DecayIdleThresholdSeconds.Value);
            float decayRate = Mathf.Max(0f, _config.DecayRatePerSecond.Value);
            if (decayRate <= 0f)
            {
                return;
            }

            for (int i = 0; i < Constants.SlotCount; i++)
            {
                SlotProgress sp = state.Slots[i];
                float timeSinceUse = now - sp.LastActivatedTime;
                if (timeSinceUse > idleThreshold && sp.Proficiency > 0d)
                {
                    sp.Proficiency = Math.Max(0d, sp.Proficiency - decayRate * Time.fixedDeltaTime);
                    int newLevel = CalculateLevel(sp.Proficiency);
                    if (newLevel != sp.Level)
                    {
                        sp.Level = newLevel;
                        if (state.LastBody)
                        {
                            state.LastBody.MarkAllStatsDirty();
                        }
                    }
                }
            }
        }

        internal int CalculateLevel(double proficiency)
        {
            if (proficiency <= 0d)
            {
                return 0;
            }

            double k = Math.Max(1d, _config.LevelCurveK.Value);
            int baseLevel = (int)Math.Floor(Math.Log(1d + proficiency / k, 2d));
            if (baseLevel < 1)
            {
                return 0;
            }

            // Walk back from the base estimate to find the exact level respecting ease/hardening
            // The ease and hardening modifiers shift requirements, so we verify precisely.
            for (int level = Math.Max(1, baseLevel + 2); level >= 1; level--)
            {
                if (proficiency >= GetRequiredProficiencyForLevel(level))
                {
                    return level;
                }
            }

            return 0;
        }

        internal double GetRequiredProficiencyForLevel(int targetLevel)
        {
            int safeLevel = Math.Max(1, targetLevel);
            double k = Math.Max(1d, _config.LevelCurveK.Value);
            double requirement = k * (Math.Pow(2d, safeLevel) - 1d);

            int easeUntilLevel = Math.Max(0, _config.CurveEaseUntilLevel.Value);
            if (safeLevel <= easeUntilLevel)
            {
                double easeMultiplier = Math.Min(1d, Math.Max(0.1d, _config.CurveEarlyEaseMultiplier.Value));
                requirement *= easeMultiplier;
            }

            int hardeningStartLevel = Math.Max(0, _config.CurveHardeningStartLevel.Value);
            if (safeLevel > hardeningStartLevel)
            {
                double hardeningPerLevel = Math.Max(0d, _config.CurveHardeningPerLevel.Value);
                double levelsPastStart = safeLevel - hardeningStartLevel;
                requirement *= 1d + (levelsPastStart * hardeningPerLevel);
            }

            return Math.Max(0d, requirement);
        }

        internal void OnSkillActivated(PlayerProgressState state, CharacterBody body, SkillSlotKind slot, float now)
        {
            state.LastActivatedSlot = slot;
            state.LastActivatedTime = now;
            state.Slots[(int)slot].LastActivatedTime = now;

            if (slot == SkillSlotKind.Utility)
            {
                float flowDuration = Mathf.Max(0f, _config.UtilityFlowDurationSeconds.Value);
                int utilLevel = state.Slots[(int)SkillSlotKind.Utility].Level;
                if (utilLevel >= Math.Max(1, _config.UtilityMilestone3Level.Value))
                {
                    flowDuration += Mathf.Max(0f, _config.UtilityMilestone3FlowDurationExtension.Value);
                }

                state.UtilityDistanceWindowEnd = now + Mathf.Max(0f, _config.UtilityDistanceWindowSeconds.Value);
                state.FlowWindowEnd = now + flowDuration;
                body.MarkAllStatsDirty();
            }

            if (slot == SkillSlotKind.Special && body.healthComponent != null)
            {
                int specialLevel = state.Slots[(int)SkillSlotKind.Special].Level;
                float barrierFraction = Mathf.Max(0f, _config.SpecialBarrierFraction.Value);
                if (specialLevel >= Math.Max(1, _config.SpecialMilestone1Level.Value))
                {
                    barrierFraction += Mathf.Max(0f, _config.SpecialMilestone1BarrierBonus.Value);
                }

                if (specialLevel >= Math.Max(1, _config.SpecialMilestone3Level.Value))
                {
                    barrierFraction += Mathf.Max(0f, _config.SpecialMilestone3BarrierBonus.Value);
                }

                if (barrierFraction > 0f)
                {
                    float barrier = body.healthComponent.fullCombinedHealth * barrierFraction;
                    if (barrier > 0f)
                    {
                        body.healthComponent.AddBarrier(barrier);
                    }
                }

                // Special tier-2: refund cooldowns on other skills
                if (specialLevel >= Math.Max(1, _config.SpecialMilestone2Level.Value))
                {
                    float refundFraction = Mathf.Clamp01(_config.SpecialMilestone2CooldownRefund.Value);
                    if (refundFraction > 0f)
                    {
                        RefundOtherSkillCooldowns(body, SkillSlotKind.Special, refundFraction);
                    }
                }
            }
        }

        internal static bool TryResolveSlot(CharacterBody body, GenericSkill skill, out SkillSlotKind slot)
        {
            slot = SkillSlotKind.Primary;
            SkillLocator locator = body.skillLocator;
            if (locator == null)
            {
                return false;
            }

            if (locator.primary == skill) { slot = SkillSlotKind.Primary; return true; }
            if (locator.secondary == skill) { slot = SkillSlotKind.Secondary; return true; }
            if (locator.utility == skill) { slot = SkillSlotKind.Utility; return true; }
            if (locator.special == skill) { slot = SkillSlotKind.Special; return true; }

            return false;
        }

        internal float GetCooldownReductionPerLevel(SkillSlotKind slot)
        {
            switch (slot)
            {
                case SkillSlotKind.Secondary: return _config.SecondaryCooldownReductionPerLevel.Value;
                case SkillSlotKind.Utility: return _config.UtilityCooldownReductionPerLevel.Value;
                case SkillSlotKind.Special: return _config.SpecialCooldownReductionPerLevel.Value;
                default: return 0f;
            }
        }

        private void TickPlayerProgress(PlayerProgressState state, float now)
        {
            if (state == null || !state.Master)
            {
                return;
            }

            CharacterBody body = state.Master.GetBody();
            if (!body)
            {
                state.LastBody = null;
                state.SnapshotInitialized = false;
                return;
            }

            if (!state.SnapshotInitialized || state.LastBody != body)
            {
                InitializeBodySnapshot(state, body, now);
            }

            SkillLocator skillLocator = body.skillLocator;
            if (skillLocator != null)
            {
                DetectSkillActivation(state, body, skillLocator.primary, SkillSlotKind.Primary, now);
                DetectSkillActivation(state, body, skillLocator.secondary, SkillSlotKind.Secondary, now);
                DetectSkillActivation(state, body, skillLocator.utility, SkillSlotKind.Utility, now);
                DetectSkillActivation(state, body, skillLocator.special, SkillSlotKind.Special, now);
            }

            Vector3 position = body.corePosition;
            if (now <= state.UtilityDistanceWindowEnd)
            {
                float distanceMoved = Vector3.Distance(state.LastPosition, position);
                if (distanceMoved > 0.01f)
                {
                    AddProficiency(state, SkillSlotKind.Utility, distanceMoved * _config.UtilityDistanceToXp.Value);
                }
            }

            HealthComponent healthComponent = body.healthComponent;
            if (healthComponent != null)
            {
                float combinedHealth = healthComponent.combinedHealth;
                float healedAmount = combinedHealth - state.LastCombinedHealth;
                if (healedAmount > 0.01f && TryGetAttributedSlot(state, now, out SkillSlotKind healSlot))
                {
                    AddProficiency(state, healSlot, healedAmount * _config.HealingToXp.Value);
                }

                state.LastCombinedHealth = combinedHealth;
            }

            state.LastPosition = position;

            bool flowNow = now <= state.FlowWindowEnd;
            if (flowNow != state.FlowWasActive)
            {
                state.FlowWasActive = flowNow;
                body.MarkAllStatsDirty();
            }

            TickDecay(state, now);
        }

        private void InitializeBodySnapshot(PlayerProgressState state, CharacterBody body, float now)
        {
            state.LastBody = body;
            state.LastPosition = body.corePosition;
            state.LastCombinedHealth = body.healthComponent ? body.healthComponent.combinedHealth : 0f;
            state.FlowWasActive = now <= state.FlowWindowEnd;
            state.SnapshotInitialized = true;

            SkillLocator skillLocator = body.skillLocator;
            state.LastPrimaryStock = skillLocator != null && skillLocator.primary != null ? skillLocator.primary.stock : 0;
            state.LastSecondaryStock = skillLocator != null && skillLocator.secondary != null ? skillLocator.secondary.stock : 0;
            state.LastUtilityStock = skillLocator != null && skillLocator.utility != null ? skillLocator.utility.stock : 0;
            state.LastSpecialStock = skillLocator != null && skillLocator.special != null ? skillLocator.special.stock : 0;

            body.MarkAllStatsDirty();
        }

        private void DetectSkillActivation(PlayerProgressState state, CharacterBody body, GenericSkill skill, SkillSlotKind slot, float now)
        {
            if (skill == null)
            {
                state.SetLastStock(slot, 0);
                return;
            }

            int previousStock = state.GetLastStock(slot);
            int currentStock = skill.stock;
            if (currentStock < previousStock)
            {
                int activations = previousStock - currentStock;
                for (int i = 0; i < activations; i++)
                {
                    OnSkillActivated(state, body, slot, now);
                }
            }

            state.SetLastStock(slot, currentStock);
        }

        private void PlayLevelUpFeedback(CharacterBody body)
        {
            if (!_config.EnableLevelUpEffects.Value || body == null)
            {
                return;
            }

            string effectPath = _config.LevelUpEffectPrefabPath.Value;
            if (!string.IsNullOrWhiteSpace(effectPath))
            {
                GameObject effectPrefab = LegacyResourcesAPI.Load<GameObject>(effectPath);
                if (effectPrefab != null)
                {
                    EffectManager.SpawnEffect(effectPrefab, new EffectData
                    {
                        origin = body.corePosition,
                        scale = Mathf.Max(1f, body.bestFitRadius)
                    }, true);
                }
            }

            string soundEvent = _config.LevelUpSoundEvent.Value;
            if (!string.IsNullOrWhiteSpace(soundEvent))
            {
                Util.PlaySound(soundEvent, body.gameObject);
            }
        }

        private void PruneDisconnectedStates()
        {
            if (PlayerStates.Count == 0)
            {
                return;
            }

            List<CharacterMaster> toRemove = null;
            foreach (var entry in PlayerStates)
            {
                if (!entry.Key || !_activeMasterScratch.Contains(entry.Key))
                {
                    if (toRemove == null)
                    {
                        toRemove = new List<CharacterMaster>();
                    }

                    toRemove.Add(entry.Key);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                CharacterMaster removedMaster = toRemove[i];
                if (removedMaster)
                {
                    ReplicatedStates.Remove(removedMaster.netId);
                }

                PlayerStates.Remove(removedMaster);
            }
        }

        private static void RefundOtherSkillCooldowns(CharacterBody body, SkillSlotKind castSlot, float fraction)
        {
            SkillLocator locator = body.skillLocator;
            if (locator == null)
            {
                return;
            }

            GenericSkill[] allSkills = { locator.primary, locator.secondary, locator.utility, locator.special };
            SkillSlotKind[] allSlots = { SkillSlotKind.Primary, SkillSlotKind.Secondary, SkillSlotKind.Utility, SkillSlotKind.Special };

            for (int i = 0; i < allSkills.Length; i++)
            {
                if (allSlots[i] == castSlot || allSkills[i] == null)
                {
                    continue;
                }

                GenericSkill skill = allSkills[i];
                if (skill.stock < skill.maxStock && skill.rechargeStopwatch > 0f)
                {
                    float refund = skill.finalRechargeInterval * fraction;
                    skill.rechargeStopwatch += refund;
                }
            }
        }

        internal static string GetDisplayName(PlayerProgressState state)
        {
            if (state.Master == null)
            {
                return "Player";
            }

            PlayerCharacterMasterController pcmc = state.Master.playerCharacterMasterController;
            if (pcmc != null && pcmc.networkUser != null)
            {
                string userName = pcmc.networkUser.userName;
                if (!string.IsNullOrEmpty(userName))
                {
                    return userName;
                }
            }

            CharacterBody body = state.Master.GetBody();
            if (body != null)
            {
                string displayName = body.GetDisplayName();
                if (!string.IsNullOrEmpty(displayName))
                {
                    return displayName;
                }
            }

            return state.Master.name ?? "Player";
        }
    }
}

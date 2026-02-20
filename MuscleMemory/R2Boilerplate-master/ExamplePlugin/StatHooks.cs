using System;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace MuscleMemory
{
    internal sealed class StatHooks
    {
        private readonly MuscleMemoryConfig _config;
        private readonly ProgressionManager _progression;
        private readonly MilestoneSystem _milestones;
        private readonly NetworkSync _networkSync;

        internal StatHooks(MuscleMemoryConfig config, ProgressionManager progression, MilestoneSystem milestones, NetworkSync networkSync)
        {
            _config = config;
            _progression = progression;
            _milestones = milestones;
            _networkSync = networkSync;
        }

        internal void Register()
        {
            On.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;
            On.RoR2.GenericSkill.CalculateFinalRechargeInterval += GenericSkill_CalculateFinalRechargeInterval;
            On.RoR2.GenericSkill.ExecuteIfReady += GenericSkill_ExecuteIfReady;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
        }

        internal void Unregister()
        {
            On.RoR2.CharacterBody.RecalculateStats -= CharacterBody_RecalculateStats;
            On.RoR2.GenericSkill.CalculateFinalRechargeInterval -= GenericSkill_CalculateFinalRechargeInterval;
            On.RoR2.GenericSkill.ExecuteIfReady -= GenericSkill_ExecuteIfReady;
            On.RoR2.HealthComponent.TakeDamage -= HealthComponent_TakeDamage;
            On.RoR2.GlobalEventManager.OnHitEnemy -= GlobalEventManager_OnHitEnemy;
        }

        private bool GenericSkill_ExecuteIfReady(On.RoR2.GenericSkill.orig_ExecuteIfReady orig, GenericSkill self)
        {
            bool result = orig(self);
            if (result && self != null && self.characterBody != null)
            {
                CharacterBody body = self.characterBody;
                if (ProgressionManager.TryResolveSlot(body, self, out SkillSlotKind slot))
                {
                    if (NetworkServer.active)
                    {
                        if (_progression.TryGetTrackedState(body, out PlayerProgressState state))
                        {
                            _progression.OnSkillActivated(state, body, slot, Time.fixedTime);
                        }
                    }
                    else if (body.master != null)
                    {
                        _networkSync.SendSkillActivationToServer(body.master.netId, slot);
                    }
                }
            }

            return result;
        }

        private void CharacterBody_RecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);

            if (self == null || !self.master)
            {
                return;
            }

            if (!_progression.TryGetEffectiveLevels(self, out int primaryLevel, out _, out int utilityLevel, out _, out bool flowActive))
            {
                return;
            }

            // Primary tier-3: attack speed bonus
            if (_milestones.HasMilestone(SkillSlotKind.Primary, 3, primaryLevel))
            {
                self.attackSpeed *= 1f + Mathf.Max(0f, _config.PrimaryMilestone3AttackSpeedBonus.Value);
            }

            if (flowActive)
            {
                float flowBonus = Mathf.Max(0f, _config.UtilityFlowMoveSpeedBonus.Value);
                if (_milestones.HasMilestone(SkillSlotKind.Utility, 1, utilityLevel))
                {
                    flowBonus += Mathf.Max(0f, _config.UtilityMilestone1FlowBonus.Value);
                }

                self.moveSpeed *= 1f + flowBonus;

                // Utility tier-2: armor during flow
                if (_milestones.HasMilestone(SkillSlotKind.Utility, 2, utilityLevel))
                {
                    self.armor += Mathf.Max(0f, _config.UtilityMilestone2ArmorBonus.Value);
                }
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && damageInfo != null && damageInfo.attacker != null)
            {
                CharacterBody attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                if (_progression.TryGetTrackedState(attackerBody, out PlayerProgressState state)
                    && _progression.TryGetAttributedSlot(state, Time.fixedTime, out SkillSlotKind slot, allowPrimaryFallback: true)
                    && slot == SkillSlotKind.Primary)
                {
                    int primaryLevel = state.Slots[(int)SkillSlotKind.Primary].Level;
                    float primaryMultiplier = 1f + (Mathf.Max(0, primaryLevel) * Mathf.Max(0f, _config.PrimaryDamagePerLevel.Value));
                    if (_config.EnableColdStart.Value && primaryLevel < 1)
                    {
                        primaryMultiplier *= Mathf.Max(Constants.MinDamageMultiplier, 1f - Mathf.Max(0f, _config.ColdStartPrimaryDamagePenalty.Value));
                    }

                    damageInfo.damage *= Mathf.Max(Constants.MinDamageMultiplier, primaryMultiplier);

                    // Primary tier-1: crit chance boost
                    if (_milestones.HasMilestone(SkillSlotKind.Primary, 1, primaryLevel))
                    {
                        float bonusCrit = Mathf.Max(0f, _config.PrimaryMilestone1CritChance.Value);
                        if (bonusCrit > 0f && !damageInfo.crit)
                        {
                            if (UnityEngine.Random.Range(0f, 100f) < bonusCrit)
                            {
                                damageInfo.crit = true;
                            }
                        }
                    }

                    // Primary tier-2: bleed
                    if (_milestones.HasMilestone(SkillSlotKind.Primary, 2, primaryLevel) && self.gameObject != null)
                    {
                        DotController.InflictDot(self.gameObject, damageInfo.attacker, DotController.DotIndex.Bleed,
                            Mathf.Max(0.25f, _config.PrimaryMilestone2BleedDuration.Value), 1f);
                    }

                    // Secondary tier-2: kills refund stock (check after damage is applied)
                }
            }

            orig(self, damageInfo);
        }

        private void GlobalEventManager_OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);

            if (!NetworkServer.active || damageInfo == null || damageInfo.attacker == null)
            {
                return;
            }

            CharacterBody attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (!_progression.TryGetTrackedState(attackerBody, out PlayerProgressState state))
            {
                return;
            }

            double xp = (Mathf.Max(0f, damageInfo.damage) * _config.DamageToXp.Value) + _config.FlatHitXp.Value;
            if (xp <= 0d)
            {
                return;
            }

            if (_progression.TryGetAttributedSlot(state, Time.fixedTime, out SkillSlotKind slot, allowPrimaryFallback: !_config.SplitUnattributedXp.Value))
            {
                _progression.AddProficiency(state, slot, xp);
            }
            else
            {
                _progression.AddProficiencySplitEvenly(state, xp);
                slot = state.LastActivatedSlot;
            }

            // Secondary tier-2: check if victim was killed and refund a stock
            if (slot == SkillSlotKind.Secondary && victim != null)
            {
                int secondaryLevel = state.Slots[(int)SkillSlotKind.Secondary].Level;
                if (_milestones.HasMilestone(SkillSlotKind.Secondary, 2, secondaryLevel))
                {
                    HealthComponent victimHealth = victim.GetComponent<HealthComponent>();
                    if (victimHealth != null && !victimHealth.alive)
                    {
                        SkillLocator locator = attackerBody.skillLocator;
                        if (locator != null && locator.secondary != null && locator.secondary.stock < locator.secondary.maxStock)
                        {
                            locator.secondary.stock += 1;
                        }
                    }
                }
            }
        }

        private float GenericSkill_CalculateFinalRechargeInterval(On.RoR2.GenericSkill.orig_CalculateFinalRechargeInterval orig, GenericSkill self)
        {
            float interval = orig(self);
            if (self == null || !self.characterBody || !self.characterBody.master)
            {
                return interval;
            }

            if (!ProgressionManager.TryResolveSlot(self.characterBody, self, out SkillSlotKind slot))
            {
                return interval;
            }

            if (slot == SkillSlotKind.Primary)
            {
                return interval;
            }

            if (!_progression.TryGetEffectiveSlotLevel(self.characterBody, slot, out int level))
            {
                return interval;
            }

            float perLevelReduction = _progression.GetCooldownReductionPerLevel(slot);
            float reduction = level * Mathf.Max(0f, perLevelReduction);

            // Secondary tier-1: bonus CDR
            if (slot == SkillSlotKind.Secondary && _milestones.HasMilestone(SkillSlotKind.Secondary, 1, level))
            {
                reduction += Mathf.Max(0f, _config.SecondaryMilestone1CooldownReduction.Value);
            }

            // Secondary tier-3: further CDR
            if (slot == SkillSlotKind.Secondary && _milestones.HasMilestone(SkillSlotKind.Secondary, 3, level))
            {
                reduction += Mathf.Max(0f, _config.SecondaryMilestone3CooldownReduction.Value);
            }

            reduction = Mathf.Clamp(reduction, 0f, 0.85f);

            float intervalMultiplier = 1f - reduction;
            if (_config.EnableColdStart.Value && level < 1)
            {
                intervalMultiplier *= 1f + Mathf.Max(0f, _config.ColdStartCooldownPenalty.Value);
            }

            return interval * Mathf.Max(Constants.MinCooldownIntervalMultiplier, intervalMultiplier);
        }
    }
}

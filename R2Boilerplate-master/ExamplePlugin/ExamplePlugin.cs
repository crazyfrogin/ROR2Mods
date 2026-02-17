using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace ExamplePlugin
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ExamplePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "AuthorName";
        public const string PluginName = "BossRushOneHitWorld";
        public const string PluginVersion = "1.0.0";

        private const string ArtifactNameToken = "BOSSRUSH_ONE_HIT_WORLD_NAME";
        private const string ArtifactDescriptionToken = "BOSSRUSH_ONE_HIT_WORLD_DESC";

        private static ArtifactDef _artifactDef;

        private readonly HashSet<CharacterMaster> _protectedBossMasters = new HashSet<CharacterMaster>();
        private bool _teleporterScaledThisStage;

        private ConfigEntry<float> _overclockHealthMultiplier;
        private ConfigEntry<float> _overclockDamageMultiplier;
        private ConfigEntry<float> _overclockAttackSpeedMultiplier;
        private ConfigEntry<float> _overclockMoveSpeedMultiplier;
        private ConfigEntry<float> _nonBossDamageMultiplier;
        private ConfigEntry<float> _blinkDistance;
        private ConfigEntry<float> _blinkNoHitSeconds;
        private ConfigEntry<float> _blinkNoLineOfSightSeconds;
        private ConfigEntry<float> _blinkCooldownSeconds;
        private ConfigEntry<float> _blinkLandingOffset;

        public void Awake()
        {
            Log.Init(Logger);

            BindConfig();
            RegisterArtifact();
            RegisterHooks();

            Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            Stage.onServerStageBegin += Stage_onServerStageBegin;
        }

        private void OnDestroy()
        {
            UnregisterHooks();

            Run.onRunDestroyGlobal -= Run_onRunDestroyGlobal;
            Stage.onServerStageBegin -= Stage_onServerStageBegin;
        }

        private void BindConfig()
        {
            _overclockHealthMultiplier = Config.Bind("Overclock", "HealthMultiplier", 4.0f, "Teleporter boss health multiplier.");
            _overclockDamageMultiplier = Config.Bind("Overclock", "DamageMultiplier", 2.0f, "Teleporter boss damage multiplier.");
            _overclockAttackSpeedMultiplier = Config.Bind("Overclock", "AttackSpeedMultiplier", 1.4f, "Teleporter boss attack speed multiplier.");
            _overclockMoveSpeedMultiplier = Config.Bind("Overclock", "MoveSpeedMultiplier", 1.35f, "Teleporter boss movement speed multiplier.");

            _nonBossDamageMultiplier = Config.Bind("DangerTuning", "NonBossDamageMultiplier", 1.0f, "Optional outgoing damage multiplier for non-boss enemies. 1.0 means disabled.");

            _blinkDistance = Config.Bind("Blink", "DistanceThreshold", 40.0f, "Boss blink can trigger when farther than this distance from the target.");
            _blinkNoHitSeconds = Config.Bind("Blink", "NoHitSeconds", 2.0f, "Boss blink can trigger when boss has not hit a player for this many seconds.");
            _blinkNoLineOfSightSeconds = Config.Bind("Blink", "NoLineOfSightSeconds", 1.0f, "Boss blink requires line-of-sight to be broken for this many seconds.");
            _blinkCooldownSeconds = Config.Bind("Blink", "CooldownSeconds", 10.0f, "Minimum time between boss blinks.");
            _blinkLandingOffset = Config.Bind("Blink", "LandingOffset", 8.0f, "How close the blink places bosses near the target.");
        }

        private void RegisterArtifact()
        {
            LanguageAPI.Add(ArtifactNameToken, "Artifact of One-Hit World");
            LanguageAPI.Add(ArtifactDescriptionToken, "Non-boss enemies die in one hit. Teleporter bosses overclock, multiply, and blink to engage.");

            _artifactDef = ScriptableObject.CreateInstance<ArtifactDef>();
            _artifactDef.cachedName = "ARTIFACT_BOSSRUSH_ONE_HIT_WORLD";
            _artifactDef.nameToken = ArtifactNameToken;
            _artifactDef.descriptionToken = ArtifactDescriptionToken;
            _artifactDef.smallIconDeselectedSprite = Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/MiscIcons/texMysteryIcon.png").WaitForCompletion();
            _artifactDef.smallIconSelectedSprite = Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/MiscIcons/texMysteryIcon.png").WaitForCompletion();

            ContentAddition.AddArtifactDef(_artifactDef);
        }

        private void RegisterHooks()
        {
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;

            CharacterMaster.onStartGlobal += CharacterMaster_onStartGlobal;
            GlobalEventManager.onCharacterDeathGlobal += GlobalEventManager_onCharacterDeathGlobal;
            GlobalEventManager.onServerDamageDealt += GlobalEventManager_onServerDamageDealt;
        }

        private void UnregisterHooks()
        {
            On.RoR2.HealthComponent.TakeDamage -= HealthComponent_TakeDamage;
            On.RoR2.CharacterBody.RecalculateStats -= CharacterBody_RecalculateStats;

            CharacterMaster.onStartGlobal -= CharacterMaster_onStartGlobal;
            GlobalEventManager.onCharacterDeathGlobal -= GlobalEventManager_onCharacterDeathGlobal;
            GlobalEventManager.onServerDamageDealt -= GlobalEventManager_onServerDamageDealt;
        }

        private void Run_onRunDestroyGlobal(Run run)
        {
            ResetRuntimeState();
        }

        private void Stage_onServerStageBegin(Stage stage)
        {
            _teleporterScaledThisStage = false;
            _protectedBossMasters.Clear();
        }

        private void CharacterMaster_onStartGlobal(CharacterMaster master)
        {
            if (!NetworkServer.active || !master || !IsModifierActive())
            {
                return;
            }

            StartCoroutine(HandleMasterSpawn(master));
        }

        private void GlobalEventManager_onCharacterDeathGlobal(DamageReport report)
        {
            if (!report.victimMaster)
            {
                return;
            }

            _protectedBossMasters.Remove(report.victimMaster);
        }

        private IEnumerator HandleMasterSpawn(CharacterMaster master)
        {
            const float timeoutSeconds = 8f;
            float timeoutAt = Time.time + timeoutSeconds;

            while (Time.time < timeoutAt)
            {
                if (!master)
                {
                    yield break;
                }

                CharacterBody body = master.GetBody();
                if (!body)
                {
                    yield return null;
                    continue;
                }

                if (!IsEnemyBody(body) || !body.isBoss)
                {
                    yield break;
                }

                bool teleporterBoss = IsTeleporterBoss(body);
                bool eventBoss = IsEventBoss(body);
                if (!teleporterBoss && !eventBoss)
                {
                    yield break;
                }

                _protectedBossMasters.Add(master);

                if (!teleporterBoss)
                {
                    yield break;
                }

                StartCoroutine(ApplyOverclockWhenBodyReady(master));

                if (!_teleporterScaledThisStage)
                {
                    _teleporterScaledThisStage = true;
                    StartCoroutine(ScaleTeleporterBossesAfterDelay(master));
                }

                yield break;
            }
        }

        private IEnumerator ScaleTeleporterBossesAfterDelay(CharacterMaster templateMaster)
        {
            yield return new WaitForSeconds(0.6f);

            if (!templateMaster || !templateMaster.GetBody())
            {
                yield break;
            }

            TrySpawnAdditionalTeleporterBosses(templateMaster, templateMaster.GetBody());
        }

        private IEnumerator ApplyOverclockWhenBodyReady(CharacterMaster master)
        {
            const float timeoutSeconds = 8f;
            float timeoutAt = Time.time + timeoutSeconds;

            while (Time.time < timeoutAt)
            {
                if (!master)
                {
                    yield break;
                }

                CharacterBody body = master.GetBody();
                if (body)
                {
                    OverclockedBossTracker tracker = body.GetComponent<OverclockedBossTracker>();
                    if (!tracker)
                    {
                        tracker = body.gameObject.AddComponent<OverclockedBossTracker>();
                    }

                    tracker.Initialize(this);
                    body.RecalculateStats();

                    if (body.healthComponent)
                    {
                        body.healthComponent.Networkhealth = body.healthComponent.fullHealth;
                    }

                    Log.Info($"Applied Overclock to {body.GetDisplayName()}");
                    yield break;
                }

                yield return null;
            }
        }

        private void TrySpawnAdditionalTeleporterBosses(CharacterMaster templateMaster, CharacterBody templateBody)
        {
            int targetBossCount = GetTargetTeleporterBossCount();
            int currentCount = CountCurrentTeleporterBosses();
            int toSpawn = Mathf.Max(0, targetBossCount - currentCount);
            if (toSpawn <= 0)
            {
                return;
            }

            GameObject masterPrefab = MasterCatalog.GetMasterPrefab(templateMaster.masterIndex);
            if (!masterPrefab)
            {
                Log.Warning("Unable to spawn additional teleporter bosses: missing master prefab.");
                return;
            }

            TeamIndex teamIndex = templateBody && templateBody.teamComponent ? templateBody.teamComponent.teamIndex : TeamIndex.Monster;
            TeleporterInteraction teleporter = TeleporterInteraction.instance;
            Vector3 center = teleporter ? teleporter.transform.position : (templateBody ? templateBody.corePosition : Vector3.zero);

            Log.Info($"Teleporter boss scaling: target={targetBossCount}, current={currentCount}, spawning={toSpawn}");

            for (int i = 0; i < toSpawn; i++)
            {
                float angle = 360f * (i / Mathf.Max(1f, toSpawn));
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 20f;

                MasterSummon summon = new MasterSummon
                {
                    masterPrefab = masterPrefab,
                    position = center + offset,
                    rotation = Quaternion.identity,
                    teamIndexOverride = teamIndex,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true
                };

                CharacterMaster spawnedMaster = summon.Perform();
                if (!spawnedMaster)
                {
                    continue;
                }
            }
        }

        private int GetTargetTeleporterBossCount()
        {
            int stage = Run.instance ? Run.instance.stageClearCount + 1 : 1;
            float runMinutes = Run.instance ? Run.instance.GetRunStopwatch() / 60f : 0f;

            if (stage <= 1)
            {
                return 1;
            }

            if (stage <= 3)
            {
                return 2;
            }

            if (stage >= 6 || runMinutes >= 30f)
            {
                return 4;
            }

            return 3;
        }

        private static bool IsEnemyBody(CharacterBody body)
        {
            if (!body || !body.teamComponent)
            {
                return false;
            }

            if (body.teamComponent.teamIndex == TeamIndex.Player)
            {
                return false;
            }

            return !body.master || !body.master.playerCharacterMasterController;
        }

        private static bool IsTeleporterBoss(CharacterBody body)
        {
            if (!body || !body.isBoss)
            {
                return false;
            }

            TeleporterInteraction teleporter = TeleporterInteraction.instance;
            if (!teleporter || teleporter.isCharged)
            {
                return false;
            }

            float sqrDistance = (body.corePosition - teleporter.transform.position).sqrMagnitude;
            return sqrDistance <= 6400f;
        }

        private static bool IsEventBoss(CharacterBody body)
        {
            if (!body || !body.isBoss)
            {
                return false;
            }

            BossGroup[] bossGroups = FindObjectsOfType<BossGroup>();
            for (int i = 0; i < bossGroups.Length; i++)
            {
                BossGroup group = bossGroups[i];
                if (!group)
                {
                    continue;
                }

                float sqrDistance = (group.transform.position - body.corePosition).sqrMagnitude;
                if (sqrDistance <= 6400f)
                {
                    return true;
                }
            }

            return false;
        }

        private int CountCurrentTeleporterBosses()
        {
            int count = 0;

            foreach (CharacterMaster master in _protectedBossMasters)
            {
                if (!master)
                {
                    continue;
                }

                CharacterBody body = master.GetBody();
                if (body && body.healthComponent && body.healthComponent.alive && IsTeleporterBoss(body))
                {
                    count++;
                }
            }

            return Mathf.Max(1, count);
        }

        private void GlobalEventManager_onServerDamageDealt(DamageReport damageReport)
        {
            if (!NetworkServer.active || !IsModifierActive())
            {
                return;
            }

            CharacterBody attackerBody = damageReport.attackerBody;
            CharacterBody victimBody = damageReport.victimBody;
            if (!attackerBody || !victimBody || !victimBody.teamComponent)
            {
                return;
            }

            if (victimBody.teamComponent.teamIndex != TeamIndex.Player)
            {
                return;
            }

            OverclockedBossTracker tracker = attackerBody.GetComponent<OverclockedBossTracker>();
            if (tracker)
            {
                tracker.NotifyPlayerHit();
            }
        }

        private void CharacterBody_RecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);

            if (!self || !self.TryGetComponent(out OverclockedBossTracker tracker) || !tracker.enabled)
            {
                return;
            }

            self.maxHealth *= Mathf.Max(1f, _overclockHealthMultiplier.Value);
            self.damage *= Mathf.Max(1f, _overclockDamageMultiplier.Value);
            self.attackSpeed *= Mathf.Max(1f, _overclockAttackSpeedMultiplier.Value);
            self.moveSpeed *= Mathf.Max(1f, _overclockMoveSpeedMultiplier.Value);
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && IsModifierActive() && self && damageInfo.damage > 0f)
            {
                ApplyNonBossDamageReduction(self, ref damageInfo);

                if (ShouldForceOneHitKill(self.body))
                {
                    damageInfo.damage = Mathf.Max(damageInfo.damage, self.combinedHealth + self.barrier + 1f);
                    damageInfo.damageType |= DamageType.BypassBlock;
                }
            }

            orig(self, damageInfo);
        }

        private void ApplyNonBossDamageReduction(HealthComponent victimHealthComponent, ref DamageInfo damageInfo)
        {
            if (_nonBossDamageMultiplier.Value >= 0.999f)
            {
                return;
            }

            CharacterBody victimBody = victimHealthComponent.body;
            if (!victimBody || !victimBody.teamComponent || victimBody.teamComponent.teamIndex != TeamIndex.Player)
            {
                return;
            }

            CharacterBody attackerBody = null;
            if (damageInfo.attacker)
            {
                attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            }

            if (!attackerBody && damageInfo.inflictor)
            {
                attackerBody = damageInfo.inflictor.GetComponent<CharacterBody>();
            }

            if (!attackerBody || !attackerBody.teamComponent || attackerBody.teamComponent.teamIndex == TeamIndex.Player)
            {
                return;
            }

            if (IsProtectedBoss(attackerBody.master))
            {
                return;
            }

            damageInfo.damage *= Mathf.Clamp01(_nonBossDamageMultiplier.Value);
        }

        private bool ShouldForceOneHitKill(CharacterBody victimBody)
        {
            if (!victimBody || !victimBody.teamComponent)
            {
                return false;
            }

            if (victimBody.teamComponent.teamIndex == TeamIndex.Player)
            {
                return false;
            }

            if (victimBody.master && victimBody.master.playerCharacterMasterController)
            {
                return false;
            }

            return !IsProtectedBoss(victimBody.master);
        }

        private bool IsProtectedBoss(CharacterMaster master)
        {
            if (!master)
            {
                return false;
            }

            return _protectedBossMasters.Contains(master);
        }

        internal bool IsModifierActive()
        {
            return _artifactDef && RunArtifactManager.instance && RunArtifactManager.instance.IsArtifactEnabled(_artifactDef);
        }

        internal float BlinkDistance => Mathf.Max(5f, _blinkDistance.Value);
        internal float BlinkNoHitSeconds => Mathf.Max(0.1f, _blinkNoHitSeconds.Value);
        internal float BlinkNoLineOfSightSeconds => Mathf.Max(0f, _blinkNoLineOfSightSeconds.Value);
        internal float BlinkCooldownSeconds => Mathf.Max(0.5f, _blinkCooldownSeconds.Value);

        internal CharacterBody FindClosestLivingPlayerBody(Vector3 origin)
        {
            CharacterBody bestBody = null;
            float bestSqrDistance = float.MaxValue;

            foreach (PlayerCharacterMasterController controller in PlayerCharacterMasterController.instances)
            {
                CharacterBody body = controller.master ? controller.master.GetBody() : null;
                if (!body || !body.healthComponent || !body.healthComponent.alive)
                {
                    continue;
                }

                float sqrDistance = (body.corePosition - origin).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestBody = body;
                }
            }

            return bestBody;
        }

        internal bool HasLineOfSight(CharacterBody from, CharacterBody to)
        {
            if (!from || !to)
            {
                return true;
            }

            Vector3 fromPosition = from.corePosition;
            Vector3 toPosition = to.corePosition;

            return !Physics.Linecast(fromPosition, toPosition, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }

        internal void BlinkNearTarget(CharacterBody bossBody, CharacterBody targetBody)
        {
            if (!bossBody || !targetBody)
            {
                return;
            }

            Vector3 toBoss = bossBody.corePosition - targetBody.corePosition;
            toBoss.y = 0f;
            if (toBoss.sqrMagnitude < 0.01f)
            {
                toBoss = UnityEngine.Random.onUnitSphere;
                toBoss.y = 0f;
            }

            Vector3 blinkDirection = toBoss.normalized;
            float landingOffset = Mathf.Max(2f, _blinkLandingOffset.Value);
            Vector3 destination = targetBody.footPosition + blinkDirection * landingOffset;

#pragma warning disable CS0618
            TeleportHelper.TeleportBody(bossBody, destination);
#pragma warning restore CS0618
            Log.Debug($"Boss blinked near {targetBody.GetDisplayName()} at {destination}");
        }

        private void CleanupNullBossEntries()
        {
            _protectedBossMasters.RemoveWhere(master => !master);
        }

        private void ResetRuntimeState()
        {
            _protectedBossMasters.Clear();
            _teleporterScaledThisStage = false;
        }
    }

    internal class OverclockedBossTracker : MonoBehaviour
    {
        private ExamplePlugin _plugin;
        private CharacterBody _body;

        private float _lastPlayerHitTime;
        private float _lastBlinkTime;
        private float _lineOfSightBreakTime;

        internal void Initialize(ExamplePlugin plugin)
        {
            _plugin = plugin;
            _body = GetComponent<CharacterBody>();

            _lastPlayerHitTime = Time.time;
            _lastBlinkTime = -999f;
            _lineOfSightBreakTime = 0f;
        }

        internal void NotifyPlayerHit()
        {
            _lastPlayerHitTime = Time.time;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _plugin == null || !_plugin.IsModifierActive() || !_body || !_body.healthComponent || !_body.healthComponent.alive)
            {
                return;
            }

            CharacterBody target = _plugin.FindClosestLivingPlayerBody(_body.corePosition);
            if (!target)
            {
                return;
            }

            float distance = Vector3.Distance(_body.corePosition, target.corePosition);
            if (distance < _plugin.BlinkDistance)
            {
                _lineOfSightBreakTime = 0f;
                return;
            }

            bool hasLineOfSight = _plugin.HasLineOfSight(_body, target);
            if (hasLineOfSight)
            {
                _lineOfSightBreakTime = 0f;
                return;
            }

            _lineOfSightBreakTime += Time.fixedDeltaTime;

            if (Time.time - _lastPlayerHitTime < _plugin.BlinkNoHitSeconds)
            {
                return;
            }

            if (_lineOfSightBreakTime < _plugin.BlinkNoLineOfSightSeconds)
            {
                return;
            }

            if (Time.time - _lastBlinkTime < _plugin.BlinkCooldownSeconds)
            {
                return;
            }

            _plugin.BlinkNearTarget(_body, target);
            _lastBlinkTime = Time.time;
            _lineOfSightBreakTime = 0f;
        }
    }
}

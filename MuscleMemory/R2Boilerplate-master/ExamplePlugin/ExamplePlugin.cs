using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace ExamplePlugin
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public sealed class ExamplePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "AuthorName";
        public const string PluginName = "MuscleMemory";
        public const string PluginVersion = "1.0.0";

        private const int SlotCount = 4;
        private const short SkillSyncMessageType = 1097;
        private const float MinCooldownIntervalMultiplier = 0.15f;
        private const float MinDamageMultiplier = 0.15f;

        private readonly Dictionary<CharacterMaster, PlayerProgressState> _playerProgressStates = new Dictionary<CharacterMaster, PlayerProgressState>();
        private readonly Dictionary<NetworkInstanceId, ReplicatedLevelState> _replicatedLevelStates = new Dictionary<NetworkInstanceId, ReplicatedLevelState>();
        private readonly HashSet<CharacterMaster> _activeMasterScratch = new HashSet<CharacterMaster>();

        private Run _trackedRun;
        private float _nextReplicationAt;

        private NetworkClient _registeredClient;
        private bool _clientHandlerRegistered;

        private ConfigEntry<double> _levelCurveK;
        private ConfigEntry<int> _curveEaseUntilLevel;
        private ConfigEntry<float> _curveEarlyEaseMultiplier;
        private ConfigEntry<int> _curveHardeningStartLevel;
        private ConfigEntry<float> _curveHardeningPerLevel;
        private ConfigEntry<int> _lowLevelEaseUntil;
        private ConfigEntry<float> _lowLevelXpMultiplier;
        private ConfigEntry<float> _attributionWindowSeconds;
        private ConfigEntry<float> _damageToXp;
        private ConfigEntry<float> _flatHitXp;
        private ConfigEntry<float> _healingToXp;
        private ConfigEntry<float> _utilityDistanceToXp;
        private ConfigEntry<float> _utilityDistanceWindowSeconds;

        private ConfigEntry<float> _primaryDamagePerLevel;
        private ConfigEntry<float> _secondaryCooldownReductionPerLevel;
        private ConfigEntry<float> _utilityCooldownReductionPerLevel;
        private ConfigEntry<float> _specialCooldownReductionPerLevel;
        private ConfigEntry<float> _utilityFlowMoveSpeedBonus;
        private ConfigEntry<float> _utilityFlowDurationSeconds;
        private ConfigEntry<float> _specialBarrierFraction;

        private ConfigEntry<bool> _enableColdStart;
        private ConfigEntry<float> _coldStartCooldownPenalty;
        private ConfigEntry<float> _coldStartPrimaryDamagePenalty;

        private ConfigEntry<bool> _enableLevelUpEffects;
        private ConfigEntry<string> _levelUpEffectPrefabPath;
        private ConfigEntry<string> _levelUpSoundEvent;

        private ConfigEntry<bool> _showSkillHud;
        private ConfigEntry<float> _skillHudScale;

        private ConfigEntry<int> _primaryBleedMilestoneLevel;
        private ConfigEntry<float> _primaryBleedDurationSeconds;
        private ConfigEntry<int> _secondaryCooldownMilestoneLevel;
        private ConfigEntry<float> _secondaryMilestoneCooldownReduction;
        private ConfigEntry<int> _utilityFlowMilestoneLevel;
        private ConfigEntry<float> _utilityMilestoneFlowBonus;
        private ConfigEntry<int> _specialBarrierMilestoneLevel;
        private ConfigEntry<float> _specialMilestoneBarrierBonus;

        private ConfigEntry<float> _replicationInterval;

        private enum SkillSlotKind
        {
            Primary = 0,
            Secondary = 1,
            Utility = 2,
            Special = 3
        }

        private sealed class SlotProgress
        {
            public double Proficiency;
            public int Level;
        }

        private sealed class ReplicatedLevelState
        {
            public readonly int[] Levels = new int[SlotCount];
            public bool FlowActive;

            public int GetLevel(SkillSlotKind slot)
            {
                return Levels[(int)slot];
            }
        }

        private sealed class SkillStateSyncMessage : MessageBase
        {
            public NetworkInstanceId MasterNetId;
            public int PrimaryLevel;
            public int SecondaryLevel;
            public int UtilityLevel;
            public int SpecialLevel;
            public bool FlowActive;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(MasterNetId);
                writer.Write(Math.Max(0, PrimaryLevel));
                writer.Write(Math.Max(0, SecondaryLevel));
                writer.Write(Math.Max(0, UtilityLevel));
                writer.Write(Math.Max(0, SpecialLevel));
                writer.Write(FlowActive);
            }

            public override void Deserialize(NetworkReader reader)
            {
                MasterNetId = reader.ReadNetworkId();
                PrimaryLevel = reader.ReadInt32();
                SecondaryLevel = reader.ReadInt32();
                UtilityLevel = reader.ReadInt32();
                SpecialLevel = reader.ReadInt32();
                FlowActive = reader.ReadBoolean();
            }
        }

        private sealed class PlayerProgressState
        {
            public readonly CharacterMaster Master;
            public readonly SlotProgress[] Slots = new SlotProgress[SlotCount];

            public CharacterBody LastBody;
            public Vector3 LastPosition;
            public float LastCombinedHealth;
            public bool SnapshotInitialized;

            public int LastPrimaryStock;
            public int LastSecondaryStock;
            public int LastUtilityStock;
            public int LastSpecialStock;

            public SkillSlotKind LastActivatedSlot;
            public float LastActivatedTime = -999f;

            public float UtilityDistanceWindowEnd;
            public float FlowWindowEnd;
            public bool FlowWasActive;

            public PlayerProgressState(CharacterMaster master)
            {
                Master = master;
                for (int i = 0; i < Slots.Length; i++)
                {
                    Slots[i] = new SlotProgress();
                }
            }

            public int GetLastStock(SkillSlotKind slot)
            {
                switch (slot)
                {
                    case SkillSlotKind.Primary:
                        return LastPrimaryStock;
                    case SkillSlotKind.Secondary:
                        return LastSecondaryStock;
                    case SkillSlotKind.Utility:
                        return LastUtilityStock;
                    case SkillSlotKind.Special:
                        return LastSpecialStock;
                    default:
                        return 0;
                }
            }

            public void SetLastStock(SkillSlotKind slot, int value)
            {
                switch (slot)
                {
                    case SkillSlotKind.Primary:
                        LastPrimaryStock = value;
                        return;
                    case SkillSlotKind.Secondary:
                        LastSecondaryStock = value;
                        return;
                    case SkillSlotKind.Utility:
                        LastUtilityStock = value;
                        return;
                    case SkillSlotKind.Special:
                        LastSpecialStock = value;
                        return;
                }
            }
        }

        private void Awake()
        {
            Log.Init(Logger);
            BindConfig();
            RegisterHooks();
            TryRegisterClientMessageHandler();
            Log.Info("Muscle Memory initialized.");
        }

        private void OnDestroy()
        {
            UnregisterHooks();
            UnregisterClientMessageHandler();
        }

        private void FixedUpdate()
        {
            TryRegisterClientMessageHandler();

            if (_trackedRun != Run.instance)
            {
                HandleRunTransition(Run.instance);
            }

            if (!NetworkServer.active || Run.instance == null)
            {
                return;
            }

            float now = Time.fixedTime;
            ServerTick(now);
        }

        private void OnGUI()
        {
            if (!_showSkillHud.Value || Run.instance == null)
            {
                return;
            }

            CharacterBody localBody = TryGetLocalBody();
            if (localBody == null)
            {
                return;
            }

            if (!TryGetEffectiveLevels(localBody, out int primary, out int secondary, out int utility, out int special, out _))
            {
                return;
            }

            if (!TryGetSkillIconRects(localBody, out Rect primaryIconRect, out Rect secondaryIconRect, out Rect utilityIconRect, out Rect specialIconRect))
            {
                return;
            }

            float scale = Mathf.Clamp(_skillHudScale.Value, 0.75f, 2f);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(16f * scale)
            };
            style.normal.textColor = Color.white;

            DrawLevelAboveSkill(primaryIconRect, primary, scale, style);
            DrawLevelAboveSkill(secondaryIconRect, secondary, scale, style);
            DrawLevelAboveSkill(utilityIconRect, utility, scale, style);
            DrawLevelAboveSkill(specialIconRect, special, scale, style);
        }

        private static void DrawLevelAboveSkill(Rect iconRect, int level, float scale, GUIStyle style)
        {
            float labelHeight = Mathf.Clamp(iconRect.height * 0.5f, 14f * scale, 24f * scale);
            float gapAboveIcon = Mathf.Max(24f * scale, iconRect.height * 0.6f);
            float labelY = Mathf.Max(0f, iconRect.y - labelHeight - gapAboveIcon);
            Rect labelRect = new Rect(iconRect.x, labelY, iconRect.width, labelHeight);
            DrawSkillLevelLabel(labelRect, level.ToString(), style);
        }

        private static bool TryGetSkillIconRects(CharacterBody localBody, out Rect primaryRect, out Rect secondaryRect, out Rect utilityRect, out Rect specialRect)
        {
            primaryRect = default;
            secondaryRect = default;
            utilityRect = default;
            specialRect = default;

            if (localBody == null)
            {
                return false;
            }

            HUD localHud = null;
            for (int i = 0; i < HUD.readOnlyInstanceList.Count; i++)
            {
                HUD candidate = HUD.readOnlyInstanceList[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.targetBodyObject == localBody.gameObject)
                {
                    localHud = candidate;
                    break;
                }
            }

            if (localHud == null)
            {
                return false;
            }

            SkillIcon[] allSkillIcons = localHud.GetComponentsInChildren<SkillIcon>(true);
            if (allSkillIcons == null || allSkillIcons.Length == 0)
            {
                return false;
            }

            SkillLocator skillLocator = localBody.skillLocator;
            bool foundPrimary = false;
            bool foundSecondary = false;
            bool foundUtility = false;
            bool foundSpecial = false;

            List<Rect> visibleIconRects = new List<Rect>(allSkillIcons.Length);
            for (int i = 0; i < allSkillIcons.Length; i++)
            {
                SkillIcon icon = allSkillIcons[i];
                if (icon == null || !icon.gameObject.activeInHierarchy)
                {
                    continue;
                }

                RectTransform rectTransform = icon.transform as RectTransform;
                if (!TryConvertRectTransformToScreenRect(rectTransform, out Rect iconRect))
                {
                    continue;
                }

                if (iconRect.width < 18f || iconRect.height < 18f)
                {
                    continue;
                }

                visibleIconRects.Add(iconRect);

                GenericSkill targetSkill = TryGetIconTargetSkill(icon);
                if (targetSkill == null || skillLocator == null)
                {
                    continue;
                }

                if (!foundPrimary && targetSkill == skillLocator.primary)
                {
                    primaryRect = iconRect;
                    foundPrimary = true;
                    continue;
                }

                if (!foundSecondary && targetSkill == skillLocator.secondary)
                {
                    secondaryRect = iconRect;
                    foundSecondary = true;
                    continue;
                }

                if (!foundUtility && targetSkill == skillLocator.utility)
                {
                    utilityRect = iconRect;
                    foundUtility = true;
                    continue;
                }

                if (!foundSpecial && targetSkill == skillLocator.special)
                {
                    specialRect = iconRect;
                    foundSpecial = true;
                }
            }

            if (foundPrimary && foundSecondary && foundUtility && foundSpecial)
            {
                return true;
            }

            if (visibleIconRects.Count < SlotCount)
            {
                return false;
            }

            visibleIconRects.Sort((a, b) => a.x.CompareTo(b.x));
            int startIndex = visibleIconRects.Count - SlotCount;

            primaryRect = visibleIconRects[startIndex];
            secondaryRect = visibleIconRects[startIndex + 1];
            utilityRect = visibleIconRects[startIndex + 2];
            specialRect = visibleIconRects[startIndex + 3];
            return true;
        }

        private static GenericSkill TryGetIconTargetSkill(SkillIcon icon)
        {
            if (icon == null)
            {
                return null;
            }

            Type iconType = icon.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo targetSkillField = iconType.GetField("targetSkill", flags);
            if (targetSkillField != null)
            {
                return targetSkillField.GetValue(icon) as GenericSkill;
            }

            PropertyInfo targetSkillProperty = iconType.GetProperty("targetSkill", flags);
            if (targetSkillProperty != null)
            {
                return targetSkillProperty.GetValue(icon, null) as GenericSkill;
            }

            return null;
        }

        private static bool TryConvertRectTransformToScreenRect(RectTransform rectTransform, out Rect screenRect)
        {
            screenRect = default;
            if (rectTransform == null)
            {
                return false;
            }

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            Canvas parentCanvas = rectTransform.GetComponentInParent<Canvas>();
            Camera uiCamera = parentCanvas != null ? parentCanvas.worldCamera : null;

            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);

            float width = topRight.x - bottomLeft.x;
            float height = topRight.y - bottomLeft.y;
            if (width <= 0f || height <= 0f)
            {
                return false;
            }

            screenRect = new Rect(bottomLeft.x, Screen.height - topRight.y, width, height);
            return true;
        }

        private static CharacterBody TryGetLocalBody()
        {
            LocalUser localUser = LocalUserManager.GetFirstLocalUser();
            if (localUser == null)
            {
                return null;
            }

            if (localUser.cachedBody != null)
            {
                return localUser.cachedBody;
            }

            if (localUser.cachedMaster != null)
            {
                return localUser.cachedMaster.GetBody();
            }

            return null;
        }

        private static void DrawSkillLevelLabel(Rect rect, string text, GUIStyle style)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);

            GUI.color = Color.white;
            GUI.Label(rect, text, style);
            GUI.color = previousColor;
        }

        private void BindConfig()
        {
            _levelCurveK = Config.Bind("Progression", "LevelCurveK", 60d, "Base K for proficiency requirements. Lower K makes leveling easier.");
            _curveEaseUntilLevel = Config.Bind("Progression", "CurveEaseUntilLevel", 6, "Required proficiency is reduced for levels up to this level.");
            _curveEarlyEaseMultiplier = Config.Bind("Progression", "CurveEarlyEaseMultiplier", 0.8f, "Multiplier applied to required proficiency for early levels. Lower values are easier.");
            _curveHardeningStartLevel = Config.Bind("Progression", "CurveHardeningStartLevel", 8, "After this level, each next level requires additional proficiency growth.");
            _curveHardeningPerLevel = Config.Bind("Progression", "CurveHardeningPerLevel", 0.06f, "Additional requirement per level above CurveHardeningStartLevel.");
            _lowLevelEaseUntil = Config.Bind("Progression", "LowLevelEaseUntil", 4, "Slots below this level gain boosted proficiency to speed up early leveling.");
            _lowLevelXpMultiplier = Config.Bind("Progression", "LowLevelXpMultiplier", 1.5f, "Proficiency multiplier applied while a slot is below LowLevelEaseUntil.");
            _attributionWindowSeconds = Config.Bind("Progression", "AttributionWindowSeconds", 2.5f, "Skill output attribution window after a slot activation.");
            _damageToXp = Config.Bind("Progression", "DamageToXp", 0.02f, "Proficiency gained per point of damage dealt.");
            _flatHitXp = Config.Bind("Progression", "FlatHitXp", 0.35f, "Flat proficiency gained per damaging hit.");
            _healingToXp = Config.Bind("Progression", "HealingToXp", 0.05f, "Proficiency gained per point healed.");
            _utilityDistanceToXp = Config.Bind("Progression", "UtilityDistanceToXp", 0.2f, "Utility proficiency gained per meter moved in utility distance window.");
            _utilityDistanceWindowSeconds = Config.Bind("Progression", "UtilityDistanceWindowSeconds", 1.25f, "Window after utility activation where movement grants utility proficiency.");

            _primaryDamagePerLevel = Config.Bind("Bonuses", "PrimaryDamagePerLevel", 0.01f, "Damage multiplier per primary level.");
            _secondaryCooldownReductionPerLevel = Config.Bind("Bonuses", "SecondaryCooldownReductionPerLevel", 0.01f, "Cooldown reduction per secondary level.");
            _utilityCooldownReductionPerLevel = Config.Bind("Bonuses", "UtilityCooldownReductionPerLevel", 0.012f, "Cooldown reduction per utility level.");
            _specialCooldownReductionPerLevel = Config.Bind("Bonuses", "SpecialCooldownReductionPerLevel", 0.008f, "Cooldown reduction per special level.");
            _utilityFlowMoveSpeedBonus = Config.Bind("Bonuses", "UtilityFlowMoveSpeedBonus", 0.08f, "Temporary move speed bonus after utility use.");
            _utilityFlowDurationSeconds = Config.Bind("Bonuses", "UtilityFlowDurationSeconds", 1.25f, "Duration of utility flow movement bonus.");
            _specialBarrierFraction = Config.Bind("Bonuses", "SpecialBarrierFraction", 0.02f, "Barrier fraction of full combined health granted on special cast.");

            _enableColdStart = Config.Bind("ColdStart", "EnableColdStart", true, "Enable rust-style penalties for slots below level 1.");
            _coldStartCooldownPenalty = Config.Bind("ColdStart", "CooldownPenalty", 0.1f, "Additional cooldown interval multiplier for slots below level 1.");
            _coldStartPrimaryDamagePenalty = Config.Bind("ColdStart", "PrimaryDamagePenalty", 0.05f, "Primary damage penalty while primary level is below 1.");

            _enableLevelUpEffects = Config.Bind("Feedback", "EnableLevelUpEffects", true, "Play level-up VFX and sound when a slot levels up.");
            _levelUpEffectPrefabPath = Config.Bind("Feedback", "LevelUpEffectPrefabPath", "Prefabs/Effects/LevelUpEffect", "Prefab path for level-up VFX.");
            _levelUpSoundEvent = Config.Bind("Feedback", "LevelUpSoundEvent", "Play_UI_levelUp", "Wwise event to play on level-up.");

            _showSkillHud = Config.Bind("UI", "ShowSkillHud", true, "Show local slot levels near the skill bar.");
            _skillHudScale = Config.Bind("UI", "SkillHudScale", 1f, "Scale multiplier for the local skill HUD.");

            _primaryBleedMilestoneLevel = Config.Bind("Milestones", "PrimaryBleedLevel", 10, "Primary milestone level that unlocks bleed on primary-attributed hits.");
            _primaryBleedDurationSeconds = Config.Bind("Milestones", "PrimaryBleedDurationSeconds", 3f, "Bleed duration applied by the primary milestone perk.");
            _secondaryCooldownMilestoneLevel = Config.Bind("Milestones", "SecondaryCooldownBonusLevel", 5, "Secondary milestone level that grants extra cooldown reduction.");
            _secondaryMilestoneCooldownReduction = Config.Bind("Milestones", "SecondaryMilestoneCooldownReduction", 0.03f, "Flat additional cooldown reduction from the secondary milestone perk.");
            _utilityFlowMilestoneLevel = Config.Bind("Milestones", "UtilityFlowBonusLevel", 5, "Utility milestone level that grants additional flow move speed.");
            _utilityMilestoneFlowBonus = Config.Bind("Milestones", "UtilityMilestoneFlowBonus", 0.1f, "Additional move speed bonus while flow is active after the utility milestone.");
            _specialBarrierMilestoneLevel = Config.Bind("Milestones", "SpecialBarrierBonusLevel", 5, "Special milestone level that grants bonus barrier on special cast.");
            _specialMilestoneBarrierBonus = Config.Bind("Milestones", "SpecialMilestoneBarrierBonus", 0.02f, "Additional barrier fraction granted by the special milestone perk.");

            _replicationInterval = Config.Bind("Networking", "ReplicationInterval", 0.2f, "Host-to-client level replication interval (seconds).");
        }

        private void RegisterHooks()
        {
            On.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;
            On.RoR2.GenericSkill.CalculateFinalRechargeInterval += GenericSkill_CalculateFinalRechargeInterval;
            On.RoR2.HealthComponent.TakeDamage += HealthComponent_TakeDamage;
            On.RoR2.GlobalEventManager.OnHitEnemy += GlobalEventManager_OnHitEnemy;
        }

        private void UnregisterHooks()
        {
            On.RoR2.CharacterBody.RecalculateStats -= CharacterBody_RecalculateStats;
            On.RoR2.GenericSkill.CalculateFinalRechargeInterval -= GenericSkill_CalculateFinalRechargeInterval;
            On.RoR2.HealthComponent.TakeDamage -= HealthComponent_TakeDamage;
            On.RoR2.GlobalEventManager.OnHitEnemy -= GlobalEventManager_OnHitEnemy;
        }

        private void HandleRunTransition(Run nextRun)
        {
            _trackedRun = nextRun;
            _playerProgressStates.Clear();
            _replicatedLevelStates.Clear();
            _nextReplicationAt = 0f;
        }

        private void ServerTick(float now)
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

            if (now >= _nextReplicationAt)
            {
                _nextReplicationAt = now + Mathf.Max(0.05f, _replicationInterval.Value);
                BroadcastProgressSnapshotsToClients(now);
            }
        }

        private void PruneDisconnectedStates()
        {
            if (_playerProgressStates.Count == 0)
            {
                return;
            }

            List<CharacterMaster> toRemove = null;
            foreach (KeyValuePair<CharacterMaster, PlayerProgressState> entry in _playerProgressStates)
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
                    _replicatedLevelStates.Remove(removedMaster.netId);
                }

                _playerProgressStates.Remove(removedMaster);
            }
        }

        private void BroadcastProgressSnapshotsToClients(float now)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            foreach (KeyValuePair<CharacterMaster, PlayerProgressState> entry in _playerProgressStates)
            {
                CharacterMaster master = entry.Key;
                PlayerProgressState state = entry.Value;
                if (!master || master.netId == NetworkInstanceId.Invalid)
                {
                    continue;
                }

                int primary = state.Slots[(int)SkillSlotKind.Primary].Level;
                int secondary = state.Slots[(int)SkillSlotKind.Secondary].Level;
                int utility = state.Slots[(int)SkillSlotKind.Utility].Level;
                int special = state.Slots[(int)SkillSlotKind.Special].Level;
                bool flowActive = now <= state.FlowWindowEnd;

                UpsertReplicatedLevelState(master.netId, primary, secondary, utility, special, flowActive);

                var message = new SkillStateSyncMessage
                {
                    MasterNetId = master.netId,
                    PrimaryLevel = primary,
                    SecondaryLevel = secondary,
                    UtilityLevel = utility,
                    SpecialLevel = special,
                    FlowActive = flowActive
                };

                NetworkServer.SendToAll(SkillSyncMessageType, message);
            }
        }

        private void TryRegisterClientMessageHandler()
        {
            NetworkClient currentClient = null;
            if (NetworkManager.singleton != null)
            {
                currentClient = NetworkManager.singleton.client;
            }

            if (currentClient == null)
            {
                List<NetworkClient> allClients = NetworkClient.allClients;
                if (allClients != null)
                {
                    for (int i = 0; i < allClients.Count; i++)
                    {
                        if (allClients[i] != null)
                        {
                            currentClient = allClients[i];
                            break;
                        }
                    }
                }
            }

            if (currentClient == null)
            {
                if (_clientHandlerRegistered)
                {
                    UnregisterClientMessageHandler();
                }

                return;
            }

            if (_clientHandlerRegistered && _registeredClient == currentClient)
            {
                return;
            }

            if (_clientHandlerRegistered)
            {
                UnregisterClientMessageHandler();
            }

            currentClient.RegisterHandler(SkillSyncMessageType, OnClientSkillStateSyncMessageReceived);
            _registeredClient = currentClient;
            _clientHandlerRegistered = true;
        }

        private void UnregisterClientMessageHandler()
        {
            if (!_clientHandlerRegistered)
            {
                return;
            }

            if (_registeredClient != null)
            {
                _registeredClient.UnregisterHandler(SkillSyncMessageType);
            }

            _registeredClient = null;
            _clientHandlerRegistered = false;
        }

        private void OnClientSkillStateSyncMessageReceived(NetworkMessage networkMessage)
        {
            SkillStateSyncMessage message = networkMessage.ReadMessage<SkillStateSyncMessage>();
            UpsertReplicatedLevelState(
                message.MasterNetId,
                message.PrimaryLevel,
                message.SecondaryLevel,
                message.UtilityLevel,
                message.SpecialLevel,
                message.FlowActive);

            GameObject masterObject = ClientScene.FindLocalObject(message.MasterNetId);
            if (masterObject == null)
            {
                return;
            }

            CharacterMaster master = masterObject.GetComponent<CharacterMaster>();
            if (master == null)
            {
                return;
            }

            CharacterBody body = master.GetBody();
            if (body != null)
            {
                body.MarkAllStatsDirty();
            }
        }

        private void UpsertReplicatedLevelState(NetworkInstanceId masterNetId, int primary, int secondary, int utility, int special, bool flowActive)
        {
            if (masterNetId == NetworkInstanceId.Invalid)
            {
                return;
            }

            if (!_replicatedLevelStates.TryGetValue(masterNetId, out ReplicatedLevelState state))
            {
                state = new ReplicatedLevelState();
                _replicatedLevelStates[masterNetId] = state;
            }

            state.Levels[(int)SkillSlotKind.Primary] = Math.Max(0, primary);
            state.Levels[(int)SkillSlotKind.Secondary] = Math.Max(0, secondary);
            state.Levels[(int)SkillSlotKind.Utility] = Math.Max(0, utility);
            state.Levels[(int)SkillSlotKind.Special] = Math.Max(0, special);
            state.FlowActive = flowActive;
        }

        private PlayerProgressState GetOrCreateProgressState(CharacterMaster master)
        {
            if (!_playerProgressStates.TryGetValue(master, out PlayerProgressState state))
            {
                state = new PlayerProgressState(master);
                _playerProgressStates[master] = state;
            }

            return state;
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
                    AddProficiency(state, SkillSlotKind.Utility, distanceMoved * _utilityDistanceToXp.Value);
                }
            }

            HealthComponent healthComponent = body.healthComponent;
            if (healthComponent != null)
            {
                float combinedHealth = healthComponent.combinedHealth;
                float healedAmount = combinedHealth - state.LastCombinedHealth;
                if (healedAmount > 0.01f && TryGetAttributedSlot(state, now, out SkillSlotKind healSlot))
                {
                    AddProficiency(state, healSlot, healedAmount * _healingToXp.Value);
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

        private void OnSkillActivated(PlayerProgressState state, CharacterBody body, SkillSlotKind slot, float now)
        {
            state.LastActivatedSlot = slot;
            state.LastActivatedTime = now;

            if (slot == SkillSlotKind.Utility)
            {
                state.UtilityDistanceWindowEnd = now + Mathf.Max(0f, _utilityDistanceWindowSeconds.Value);
                state.FlowWindowEnd = now + Mathf.Max(0f, _utilityFlowDurationSeconds.Value);
                body.MarkAllStatsDirty();
            }

            if (slot == SkillSlotKind.Special && body.healthComponent != null)
            {
                float barrierFraction = Mathf.Max(0f, _specialBarrierFraction.Value);
                if (state.Slots[(int)SkillSlotKind.Special].Level >= Math.Max(1, _specialBarrierMilestoneLevel.Value))
                {
                    barrierFraction += Mathf.Max(0f, _specialMilestoneBarrierBonus.Value);
                }

                if (barrierFraction > 0f)
                {
                    float barrier = body.healthComponent.fullCombinedHealth * barrierFraction;
                    if (barrier > 0f)
                    {
                        body.healthComponent.AddBarrier(barrier);
                    }
                }
            }
        }

        private bool TryGetAttributedSlot(PlayerProgressState state, float now, out SkillSlotKind slot, bool allowPrimaryFallback = false)
        {
            if (state != null && now - state.LastActivatedTime <= Mathf.Max(0.1f, _attributionWindowSeconds.Value))
            {
                slot = state.LastActivatedSlot;
                return true;
            }

            slot = SkillSlotKind.Primary;
            return allowPrimaryFallback;
        }

        private void GlobalEventManager_OnHitEnemy(On.RoR2.GlobalEventManager.orig_OnHitEnemy orig, GlobalEventManager self, DamageInfo damageInfo, GameObject victim)
        {
            orig(self, damageInfo, victim);

            if (!NetworkServer.active || damageInfo == null || damageInfo.attacker == null)
            {
                return;
            }

            CharacterBody attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            if (!TryGetTrackedState(attackerBody, out PlayerProgressState state))
            {
                return;
            }

            if (!TryGetAttributedSlot(state, Time.fixedTime, out SkillSlotKind slot, allowPrimaryFallback: true))
            {
                return;
            }

            double xp = (Mathf.Max(0f, damageInfo.damage) * _damageToXp.Value) + _flatHitXp.Value;
            if (xp > 0d)
            {
                AddProficiency(state, slot, xp);
            }
        }

        private bool TryGetTrackedState(CharacterBody attackerBody, out PlayerProgressState state)
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

        private void AddProficiency(PlayerProgressState state, SkillSlotKind slot, double amount)
        {
            if (!NetworkServer.active || state == null || amount <= 0d)
            {
                return;
            }

            SlotProgress slotProgress = state.Slots[(int)slot];
            double adjustedAmount = amount;
            int lowLevelEaseUntil = Math.Max(0, _lowLevelEaseUntil.Value);
            if (slotProgress.Level < lowLevelEaseUntil)
            {
                adjustedAmount *= Mathf.Max(1f, _lowLevelXpMultiplier.Value);
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

                    string playerName = state.Master != null ? state.Master.name : "Player";
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                    {
                        baseToken = $"<style=cIsUtility>{playerName}</style> leveled <style=cIsDamage>{slot}</style> to <style=cIsHealing>Lv {nextLevel}</style>."
                    });

                    BroadcastMilestoneUnlock(state, slot, previousLevel, nextLevel);
                }

                Log.Info($"{state.Master.name} {slot} reached level {nextLevel} (P={slotProgress.Proficiency:0.0}).");
            }
        }

        private void PlayLevelUpFeedback(CharacterBody body)
        {
            if (!_enableLevelUpEffects.Value || body == null)
            {
                return;
            }

            string effectPath = _levelUpEffectPrefabPath.Value;
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

            string soundEvent = _levelUpSoundEvent.Value;
            if (!string.IsNullOrWhiteSpace(soundEvent))
            {
                Util.PlaySound(soundEvent, body.gameObject);
            }
        }

        private void BroadcastMilestoneUnlock(PlayerProgressState state, SkillSlotKind slot, int previousLevel, int nextLevel)
        {
            string playerName = state.Master != null ? state.Master.name : "Player";

            switch (slot)
            {
                case SkillSlotKind.Primary:
                    if (HasCrossedMilestone(previousLevel, nextLevel, _primaryBleedMilestoneLevel.Value))
                    {
                        Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                        {
                            baseToken = $"<style=cIsUtility>{playerName}</style> unlocked <style=cIsDamage>Primary Milestone</style>: primary hits now inflict <style=cIsHealing>Bleed</style>."
                        });
                    }

                    break;
                case SkillSlotKind.Secondary:
                    if (HasCrossedMilestone(previousLevel, nextLevel, _secondaryCooldownMilestoneLevel.Value))
                    {
                        Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                        {
                            baseToken = $"<style=cIsUtility>{playerName}</style> unlocked <style=cIsDamage>Secondary Milestone</style>: bonus cooldown reduction activated."
                        });
                    }

                    break;
                case SkillSlotKind.Utility:
                    if (HasCrossedMilestone(previousLevel, nextLevel, _utilityFlowMilestoneLevel.Value))
                    {
                        Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                        {
                            baseToken = $"<style=cIsUtility>{playerName}</style> unlocked <style=cIsDamage>Utility Milestone</style>: enhanced flow speed activated."
                        });
                    }

                    break;
                case SkillSlotKind.Special:
                    if (HasCrossedMilestone(previousLevel, nextLevel, _specialBarrierMilestoneLevel.Value))
                    {
                        Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                        {
                            baseToken = $"<style=cIsUtility>{playerName}</style> unlocked <style=cIsDamage>Special Milestone</style>: bonus barrier activated."
                        });
                    }

                    break;
            }
        }

        private static bool HasCrossedMilestone(int previousLevel, int nextLevel, int milestoneLevel)
        {
            int target = Math.Max(1, milestoneLevel);
            return previousLevel < target && nextLevel >= target;
        }

        private int CalculateLevel(double proficiency)
        {
            if (proficiency <= 0d)
            {
                return 0;
            }

            const int maxEvaluatedLevel = 512;
            for (int targetLevel = 1; targetLevel <= maxEvaluatedLevel; targetLevel++)
            {
                if (proficiency < GetRequiredProficiencyForLevel(targetLevel))
                {
                    return targetLevel - 1;
                }
            }

            return maxEvaluatedLevel;
        }

        private double GetRequiredProficiencyForLevel(int targetLevel)
        {
            int safeLevel = Math.Max(1, targetLevel);
            double k = Math.Max(1d, _levelCurveK.Value);
            double requirement = k * (Math.Pow(2d, safeLevel) - 1d);

            int easeUntilLevel = Math.Max(0, _curveEaseUntilLevel.Value);
            if (safeLevel <= easeUntilLevel)
            {
                double easeMultiplier = Math.Min(1d, Math.Max(0.1d, _curveEarlyEaseMultiplier.Value));
                requirement *= easeMultiplier;
            }

            int hardeningStartLevel = Math.Max(0, _curveHardeningStartLevel.Value);
            if (safeLevel > hardeningStartLevel)
            {
                double hardeningPerLevel = Math.Max(0d, _curveHardeningPerLevel.Value);
                double levelsPastStart = safeLevel - hardeningStartLevel;
                requirement *= 1d + (levelsPastStart * hardeningPerLevel);
            }

            return Math.Max(0d, requirement);
        }

        private void CharacterBody_RecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);

            if (self == null || !self.master)
            {
                return;
            }

            if (!TryGetEffectiveLevels(self, out _, out _, out int utilityLevel, out _, out bool flowActive))
            {
                return;
            }

            if (flowActive)
            {
                float flowBonus = Mathf.Max(0f, _utilityFlowMoveSpeedBonus.Value);
                if (utilityLevel >= Math.Max(1, _utilityFlowMilestoneLevel.Value))
                {
                    flowBonus += Mathf.Max(0f, _utilityMilestoneFlowBonus.Value);
                }

                self.moveSpeed *= 1f + flowBonus;
            }
        }

        private void HealthComponent_TakeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo damageInfo)
        {
            if (NetworkServer.active && damageInfo != null && damageInfo.attacker != null)
            {
                CharacterBody attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                if (TryGetTrackedState(attackerBody, out PlayerProgressState state)
                    && TryGetAttributedSlot(state, Time.fixedTime, out SkillSlotKind slot, allowPrimaryFallback: true)
                    && slot == SkillSlotKind.Primary)
                {
                    int primaryLevel = state.Slots[(int)SkillSlotKind.Primary].Level;
                    float primaryMultiplier = 1f + (Mathf.Max(0, primaryLevel) * Mathf.Max(0f, _primaryDamagePerLevel.Value));
                    if (_enableColdStart.Value && primaryLevel < 1)
                    {
                        primaryMultiplier *= Mathf.Max(MinDamageMultiplier, 1f - Mathf.Max(0f, _coldStartPrimaryDamagePenalty.Value));
                    }

                    damageInfo.damage *= Mathf.Max(MinDamageMultiplier, primaryMultiplier);

                    if (primaryLevel >= Math.Max(1, _primaryBleedMilestoneLevel.Value) && self.gameObject != null)
                    {
                        DotController.InflictDot(self.gameObject, damageInfo.attacker, DotController.DotIndex.Bleed, Mathf.Max(0.25f, _primaryBleedDurationSeconds.Value), 1f);
                    }
                }
            }

            orig(self, damageInfo);
        }

        private float GenericSkill_CalculateFinalRechargeInterval(On.RoR2.GenericSkill.orig_CalculateFinalRechargeInterval orig, GenericSkill self)
        {
            float interval = orig(self);
            if (self == null || !self.characterBody || !self.characterBody.master)
            {
                return interval;
            }

            if (!TryResolveSlot(self.characterBody, self, out SkillSlotKind slot))
            {
                return interval;
            }

            if (slot == SkillSlotKind.Primary)
            {
                return interval;
            }

            if (!TryGetEffectiveSlotLevel(self.characterBody, slot, out int level))
            {
                return interval;
            }

            float perLevelReduction = GetCooldownReductionPerLevel(slot);
            float reduction = level * Mathf.Max(0f, perLevelReduction);
            if (slot == SkillSlotKind.Secondary && level >= Math.Max(1, _secondaryCooldownMilestoneLevel.Value))
            {
                reduction += Mathf.Max(0f, _secondaryMilestoneCooldownReduction.Value);
            }

            reduction = Mathf.Clamp(reduction, 0f, 0.85f);

            float intervalMultiplier = 1f - reduction;
            if (_enableColdStart.Value && level < 1)
            {
                intervalMultiplier *= 1f + Mathf.Max(0f, _coldStartCooldownPenalty.Value);
            }

            return interval * Mathf.Max(MinCooldownIntervalMultiplier, intervalMultiplier);
        }

        private bool TryGetEffectiveSlotLevel(CharacterBody body, SkillSlotKind slot, out int level)
        {
            if (TryGetEffectiveLevels(body, out int primary, out int secondary, out int utility, out int special, out _))
            {
                switch (slot)
                {
                    case SkillSlotKind.Primary:
                        level = primary;
                        return true;
                    case SkillSlotKind.Secondary:
                        level = secondary;
                        return true;
                    case SkillSlotKind.Utility:
                        level = utility;
                        return true;
                    case SkillSlotKind.Special:
                        level = special;
                        return true;
                }
            }

            level = 0;
            return false;
        }

        private bool TryGetEffectiveLevels(CharacterBody body, out int primary, out int secondary, out int utility, out int special, out bool flowActive)
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

            if (_playerProgressStates.TryGetValue(body.master, out PlayerProgressState serverState))
            {
                primary = serverState.Slots[(int)SkillSlotKind.Primary].Level;
                secondary = serverState.Slots[(int)SkillSlotKind.Secondary].Level;
                utility = serverState.Slots[(int)SkillSlotKind.Utility].Level;
                special = serverState.Slots[(int)SkillSlotKind.Special].Level;
                flowActive = Time.fixedTime <= serverState.FlowWindowEnd;
                return true;
            }

            NetworkInstanceId netId = body.master.netId;
            if (netId != NetworkInstanceId.Invalid && _replicatedLevelStates.TryGetValue(netId, out ReplicatedLevelState replicated))
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

        private static bool TryResolveSlot(CharacterBody body, GenericSkill skill, out SkillSlotKind slot)
        {
            slot = SkillSlotKind.Primary;
            SkillLocator locator = body.skillLocator;
            if (locator == null)
            {
                return false;
            }

            if (locator.primary == skill)
            {
                slot = SkillSlotKind.Primary;
                return true;
            }

            if (locator.secondary == skill)
            {
                slot = SkillSlotKind.Secondary;
                return true;
            }

            if (locator.utility == skill)
            {
                slot = SkillSlotKind.Utility;
                return true;
            }

            if (locator.special == skill)
            {
                slot = SkillSlotKind.Special;
                return true;
            }

            return false;
        }

        private float GetCooldownReductionPerLevel(SkillSlotKind slot)
        {
            switch (slot)
            {
                case SkillSlotKind.Secondary:
                    return _secondaryCooldownReductionPerLevel.Value;
                case SkillSlotKind.Utility:
                    return _utilityCooldownReductionPerLevel.Value;
                case SkillSlotKind.Special:
                    return _specialCooldownReductionPerLevel.Value;
                default:
                    return 0f;
            }
        }
    }
}

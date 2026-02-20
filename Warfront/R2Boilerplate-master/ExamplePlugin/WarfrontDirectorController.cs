using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace WarfrontDirector
{
    [DefaultExecutionOrder(10000)]
    internal sealed class WarfrontDirectorController : MonoBehaviour
    {
        private static readonly WarWarning[] WarningPool =
        {
            WarWarning.Sappers,
            WarWarning.PhalanxDoctrine,
            WarWarning.ArtilleryDoctrine,
            WarWarning.HunterKiller,
            WarWarning.MedicNet,
            WarWarning.Attrition,
            WarWarning.SiegeEngine,
            WarWarning.PackTactics,
            WarWarning.SignalJamming,
            WarWarning.ReinforcedVanguard,
            WarWarning.ExecutionOrder,
            WarWarning.SupplyLine
        };

        private static readonly WarAnomaly[] AnomalyPool =
        {
            WarAnomaly.SilentMinute,
            WarAnomaly.WarDrums,
            WarAnomaly.FalseLull,
            WarAnomaly.CommandConfusion,
            WarAnomaly.Blackout,
            WarAnomaly.CounterIntel,
            WarAnomaly.BlitzOrder,
            WarAnomaly.IronRain
        };

        private static readonly WarfrontNodeType[] NodeTypeCycle =
        {
            WarfrontNodeType.Relay,
            WarfrontNodeType.Forge,
            WarfrontNodeType.Siren,
            WarfrontNodeType.SpawnCache
        };

        private static readonly string[] CuratedCommanderEliteTokens =
        {
            "BLAZING",
            "GLACIAL",
            "OVERLOADING",
            "MENDING",
            "AFFIXRED",
            "AFFIXBLUE",
            "AFFIXWHITE",
            "AFFIXPOISON",
            "EDFIRE",
            "EDICE",
            "EDLIGHTNING",
            "EDPOISON"
        };

        private static readonly WarfrontRole[] RoleRotation =
        {
            WarfrontRole.Contester,
            WarfrontRole.Peeler,
            WarfrontRole.Flanker,
            WarfrontRole.Artillery,
            WarfrontRole.Hunter,
            WarfrontRole.Anchor
        };

        private static readonly WarfrontDoctrineProfile[] DoctrinePool =
        {
            WarfrontDoctrineProfile.Balanced,
            WarfrontDoctrineProfile.SwarmFront,
            WarfrontDoctrineProfile.ArtilleryFront,
            WarfrontDoctrineProfile.HunterCell,
            WarfrontDoctrineProfile.SiegeFront,
            WarfrontDoctrineProfile.DisruptionFront
        };

        private const float TeleporterFogDotRefreshInterval = 0.5f;
        private const float TeleporterFogDotDuration = 1.25f;
        private const float TeleporterFogStrongThresholdSeconds = 9f;
        private const float TeleporterFogBaseDamageFractionPerSecond = 0.02f;
        private const float TeleporterFogRampDamageFractionPerSecond = 0.01f;
        private const float TeleporterFogMaxRampDamageFractionPerSecond = 0.09f;

        private static readonly TeamIndex[] EnemyTeams = { TeamIndex.Monster, TeamIndex.Void, TeamIndex.Lunar };

        private static readonly float[] SiegePulseIntervalScale = { 1f, 0.85f, 0.7f, 0.55f };
        private static readonly int[] SiegeSpawnCountBonus = { 0, 1, 2, 3 };
        private static readonly float[] SiegeBreatherScale = { 1f, 0.8f, 0.65f, 0.5f };
        private static readonly float[] SiegeHeavyChanceBonus = { 0f, 0.08f, 0.16f, 0.24f };
        private const float ZoneShrinkMinFractionDefault = 0.55f;

        private readonly List<WarfrontNode> _activeNodes = new List<WarfrontNode>();
        private readonly Dictionary<CombatDirector, DirectorDefaults> _directorDefaults = new Dictionary<CombatDirector, DirectorDefaults>();
        private readonly List<CombatDirector> _staleDirectorKeys = new List<CombatDirector>();
        private readonly HashSet<WarWarning> _activeWarnings = new HashSet<WarWarning>();
        private readonly Dictionary<WarfrontRole, float> _stageRoleSignals = new Dictionary<WarfrontRole, float>();
        private readonly Dictionary<WarfrontRole, float> _runRoleThreatSignals = new Dictionary<WarfrontRole, float>();
        private readonly List<EliteIndex> _curatedCommanderElites = new List<EliteIndex>();
        private readonly HashSet<int> _bossChallengeBuffedMasters = new HashSet<int>();
        private readonly HashSet<int> _bossRoleControllerAttached = new HashSet<int>();
        private readonly Dictionary<int, int> _bossPhaseReached = new Dictionary<int, int>();
        private readonly HashSet<int> _bossEnraged = new HashSet<int>();
        private readonly Dictionary<int, float> _teleporterFogExposureByMasterId = new Dictionary<int, float>();
        private readonly HashSet<int> _teleporterFogSeenPlayerMasterIds = new HashSet<int>();
        private readonly List<int> _teleporterFogStaleKeys = new List<int>();

        private static bool _voidFogEnumsCached;
        private static bool _hasVoidFogDotMild;
        private static bool _hasVoidFogDotStrong;
        private static bool _hasVoidFogBuffMild;
        private static bool _hasVoidFogBuffStrong;
        private static DotController.DotIndex _cachedVoidFogDotMild;
        private static DotController.DotIndex _cachedVoidFogDotStrong;
        private static BuffIndex _cachedVoidFogBuffMild;
        private static BuffIndex _cachedVoidFogBuffStrong;

        private bool _hooksInstalled;
        private bool _stageActive;
        private bool _teleporterEventActive;
        private bool _assaultActive;
        private bool _breachActive;
        private bool _falseLullPending;
        private bool _silentMinuteFinished;
        private bool _teleporterBossObservedAlive;
        private bool _postBossKillRespitesEnabled;
        private bool _postBossWaveActive;
        private bool _bossGateActive;
        private bool _siegeTier3Announced;
        private float _originalHoldoutRadius;

        private float _stageStopwatch;
        private float _windowTimer;
        private float _assaultPulseTimer;
        private float _reconPulseTimer;
        private float _intensity;
        private float _recentPlayerDamage;
        private float _silentMinuteTimer;
        private float _silentMinuteBufferedPulse;
        private float _medicPulseTimer;
        private float _networkSyncTimer;
        private float _loneWolfPressure;
        private float _mercyTimer;
        private float _mercyCooldownTimer;
        private float _stageDamageSignal;
        private float _stageContestSignal;
        private float _stageLoneWolfSignal;

        private float _runDamageSignal;
        private float _runContestSignal;
        private float _runLoneWolfSignal;
        private float _runBreachSignal;

        private string _operationSummary = string.Empty;
        private WarfrontOperationRoll _operationRoll;
        private WarfrontRole _dominantRole;
        private WarfrontDoctrineProfile _currentDoctrine = WarfrontDoctrineProfile.Balanced;
        private WarfrontDoctrineProfile _lastDoctrine = WarfrontDoctrineProfile.Balanced;
        private int _doctrineStreakCount;
        private int _roleRotationCursor = -1;
        private int _nodeTypeCursor;
        private int _stageCommanderQuota;
        private int _pendingTeleporterCommanderSpawns;
        private int _eventBuffId;
        private int _lastAlivePlayerCount;
        private int _stageBreachCount;
        private bool _stageSignalsCommitted;

        private float _eventBuffTimer;
        private float _eventBuffDuration;
        private float _eventBuffMagnitude;
        private float _hunterSquadTargetTimer;
        private float _bossChallengeScanTimer;
        private float _postBossKillRespiteTimer;
        private float _postBossKillRespiteCycleTimer;
        private float _pausedTeleporterCharge;
        private float _teleporterFogTickTimer;
        private float _breachCooldownTimer;
        private float _breachDurationTimer;
        private float _breachPulseTimer;
        private float _reactiveEscalationTimer;
        private float _recentPlayerKills;
        private float _sapperPulseTimer;
        private float _signalJamPulseTimer;
        private float _staggerDelayTimer;
        private int _staggerPhase;
        private CharacterBody _hunterSquadTarget;
        private bool _teleporterChargePaused;
        private bool _teleporterFogActive;

        private TeleporterInteraction _teleporter;
        private HoldoutZoneController _holdoutZone;

        private static WarfrontHudSnapshot _latestHudSnapshot;
        private static WarfrontHudSnapshot _remoteHudSnapshot;
        private static bool _hasRemoteSnapshot;
        private static string _lastBroadcastMessage = string.Empty;
        private static float _lastBroadcastTime = -100f;

        internal static bool TryGetHudSnapshot(out WarfrontHudSnapshot snapshot)
        {
            snapshot = _latestHudSnapshot;
            return snapshot.Active;
        }

        private void Awake()
        {
            WarfrontAssetCatalog.Load();
            InstallHooks();
            ResetDoctrineAdaptation();
            ResetRunState(clearNodes: true, restoreDirectors: false);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        internal void Shutdown()
        {
            if (_hooksInstalled)
            {
                UninstallHooks();
            }

            ResetRunState(clearNodes: true, restoreDirectors: true);
            _latestHudSnapshot = default;
        }

        private void FixedUpdate()
        {
            if (!WarfrontDirectorPlugin.Enabled.Value)
            {
                _latestHudSnapshot = default;
                return;
            }

            if (NetworkServer.active)
            {
                ServerFixedUpdate(Time.fixedDeltaTime);
                BuildHudSnapshot();
            }
        }

        private void InstallHooks()
        {
            if (_hooksInstalled)
            {
                return;
            }

            Run.onRunStartGlobal += OnRunStart;
            Run.onRunDestroyGlobal += OnRunDestroy;
            Stage.onServerStageBegin += OnServerStageBegin;
            TeleporterInteraction.onTeleporterBeginChargingGlobal += OnTeleporterBeginCharging;
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterCharged;
            TeleporterInteraction.onTeleporterFinishGlobal += OnTeleporterFinished;
            GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;
            On.RoR2.GenericSkill.OnExecute += OnSkillExecuted;
            On.RoR2.HealthComponent.Heal += OnHealthComponentHeal;
            On.RoR2.CharacterBody.RecalculateStats += OnRecalculateStats;
            NetworkManagerSystem.onStartClientGlobal += OnStartClient;
            NetworkManagerSystem.onStopClientGlobal += OnStopClient;

            _hooksInstalled = true;
        }

        private void UninstallHooks()
        {
            Run.onRunStartGlobal -= OnRunStart;
            Run.onRunDestroyGlobal -= OnRunDestroy;
            Stage.onServerStageBegin -= OnServerStageBegin;
            TeleporterInteraction.onTeleporterBeginChargingGlobal -= OnTeleporterBeginCharging;
            TeleporterInteraction.onTeleporterChargedGlobal -= OnTeleporterCharged;
            TeleporterInteraction.onTeleporterFinishGlobal -= OnTeleporterFinished;
            GlobalEventManager.onServerDamageDealt -= OnServerDamageDealt;
            On.RoR2.GenericSkill.OnExecute -= OnSkillExecuted;
            On.RoR2.HealthComponent.Heal -= OnHealthComponentHeal;
            On.RoR2.CharacterBody.RecalculateStats -= OnRecalculateStats;
            NetworkManagerSystem.onStartClientGlobal -= OnStartClient;
            NetworkManagerSystem.onStopClientGlobal -= OnStopClient;

            _hooksInstalled = false;
        }

        private void OnStartClient(NetworkClient client)
        {
            WarfrontNetworkSync.Register(client);
        }

        private void OnStopClient()
        {
            _hasRemoteSnapshot = false;
            _remoteHudSnapshot = default;
        }

        private void OnRunStart(Run _)
        {
            if (!NetworkServer.active || !WarfrontDirectorPlugin.Enabled.Value)
            {
                return;
            }

            ResetDoctrineAdaptation();
            ResetRunState(clearNodes: true, restoreDirectors: true);
            BroadcastWarfrontMessage("Warfront doctrine engaged.");
        }

        private void OnRunDestroy(Run _)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            CommitStageAdaptationSignals();
            ResetRunState(clearNodes: true, restoreDirectors: true);
            ResetDoctrineAdaptation();
        }

        private void OnServerStageBegin(Stage stage)
        {
            if (!NetworkServer.active || !WarfrontDirectorPlugin.Enabled.Value)
            {
                return;
            }

            CommitStageAdaptationSignals();
            ResetRunState(clearNodes: true, restoreDirectors: true);

            _stageActive = true;
            _stageStopwatch = 0f;
            _intensity = 6f;
            _recentPlayerDamage = 0f;
            _silentMinuteBufferedPulse = 0f;
            _silentMinuteTimer = 0f;
            _silentMinuteFinished = false;

            ResolveTeleporterReference();
            CacheDirectors();
            CacheCuratedCommanderElites();

            var stageIndex = Run.instance != null ? Run.instance.stageClearCount : 0;
            _currentDoctrine = SelectDoctrineProfile(stageIndex);
            _operationRoll = RollOperations(_currentDoctrine);
            _activeWarnings.Clear();
            _activeWarnings.Add(_operationRoll.WarningOne);
            _activeWarnings.Add(_operationRoll.WarningTwo);

            _falseLullPending = _operationRoll.Anomaly == WarAnomaly.FalseLull;
            _operationSummary = $"{ToDisplayName(_currentDoctrine)} | {ToDisplayName(_operationRoll.WarningOne)}, {ToDisplayName(_operationRoll.WarningTwo)} - {ToDisplayName(_operationRoll.Anomaly)}";

            SpawnEnemyNodes();
            BroadcastWarfrontMessage($"Doctrine shift: {ToDisplayName(_currentDoctrine)}.", 2f);
            BroadcastWarfrontMessage(string.Format(Language.GetString("WARFRONT_STAGE_BANNER"), ToDisplayName(_operationRoll.WarningOne), ToDisplayName(_operationRoll.WarningTwo), ToDisplayName(_operationRoll.Anomaly)));

            _reconPulseTimer = 6f;
            SetDirectorCadence(assault: false, recon: true);

            Log.Info($"Warfront stage start: {_operationSummary}");
        }

        private void OnTeleporterBeginCharging(TeleporterInteraction teleporter)
        {
            if (!NetworkServer.active || !_stageActive || !WarfrontDirectorPlugin.Enabled.Value)
            {
                return;
            }

            _teleporter = teleporter;
            _holdoutZone = teleporter ? teleporter.holdoutZoneController : null;
            if (_holdoutZone == null && teleporter)
            {
                _holdoutZone = teleporter.GetComponent<HoldoutZoneController>();
            }
            _teleporterChargePaused = false;
            _pausedTeleporterCharge = _holdoutZone != null ? _holdoutZone.charge : 0f;
            _teleporterFogActive = true;
            _teleporterFogTickTimer = 0f;
            _teleporterFogExposureByMasterId.Clear();
            _teleporterFogSeenPlayerMasterIds.Clear();

            _teleporterEventActive = true;
            _assaultActive = false;
            _breachActive = false;
            _lastAlivePlayerCount = GetAlivePlayerCount();
            _teleporterBossObservedAlive = false;
            _postBossKillRespitesEnabled = false;
            _postBossWaveActive = false;
            _postBossKillRespiteTimer = 0f;
            _postBossKillRespiteCycleTimer = 0f;
            _bossGateActive = true;
            _siegeTier3Announced = false;

            if (_holdoutZone != null)
            {
                _originalHoldoutRadius = _holdoutZone.baseRadius;
            }

            BeginBreather(initial: true);
            SpawnTeleporterCommanders();
            AttachCommanderRoleControllers();
            SetDirectorCadenceWavePause();
            BroadcastWarfrontMessage("Teleporter activated. Defeat the boss to begin charging.");
        }

        private void OnTeleporterCharged(TeleporterInteraction _)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            _teleporterEventActive = false;
            _assaultActive = false;
            _breachActive = false;
            _windowTimer = 0f;
            _teleporterChargePaused = false;
            _teleporterFogActive = false;
            _teleporterFogTickTimer = 0f;
            _teleporterFogExposureByMasterId.Clear();
            _teleporterFogSeenPlayerMasterIds.Clear();
            _teleporterBossObservedAlive = false;
            _postBossKillRespitesEnabled = false;
            _postBossWaveActive = false;
            _postBossKillRespiteTimer = 0f;
            _postBossKillRespiteCycleTimer = 0f;
            _bossGateActive = false;
            RestoreHoldoutRadius();
            DetachCommanderRoleControllers();
            SetDirectorCadence(assault: false, recon: false);
        }

        private void OnTeleporterFinished(TeleporterInteraction _)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            _teleporterEventActive = false;
            _assaultActive = false;
            _breachActive = false;
            _windowTimer = 0f;
            _teleporterChargePaused = false;
            _teleporterFogActive = false;
            _teleporterFogTickTimer = 0f;
            _teleporterFogExposureByMasterId.Clear();
            _teleporterFogSeenPlayerMasterIds.Clear();
            _teleporterBossObservedAlive = false;
            _postBossKillRespitesEnabled = false;
            _postBossWaveActive = false;
            _postBossKillRespiteTimer = 0f;
            _postBossKillRespiteCycleTimer = 0f;
            _bossGateActive = false;
            RestoreHoldoutRadius();
            DetachCommanderRoleControllers();
            SetDirectorCadence(assault: false, recon: false);
            CommitStageAdaptationSignals();
            ResetRunState(clearNodes: true, restoreDirectors: true);
        }

        private void OnServerDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active || !_stageActive || report == null || report.victimBody == null || report.attackerBody == null)
            {
                return;
            }

            var victimTeam = report.victimBody.teamComponent ? report.victimBody.teamComponent.teamIndex : TeamIndex.None;
            var attackerTeam = report.attackerBody.teamComponent ? report.attackerBody.teamComponent.teamIndex : TeamIndex.None;
            if (victimTeam == TeamIndex.Player && attackerTeam != TeamIndex.Player)
            {
                var dealt = Mathf.Max(0f, report.damageDealt);
                _recentPlayerDamage += dealt;
                _stageDamageSignal += dealt;

                if (_assaultActive && _dominantRole != WarfrontRole.None)
                {
                    AccumulateRoleSignal(_dominantRole, dealt * 0.03f);
                }
            }

            if (attackerTeam == TeamIndex.Player && victimTeam != TeamIndex.Player &&
                report.victimBody.healthComponent != null && !report.victimBody.healthComponent.alive)
            {
                _recentPlayerKills += 1f;
            }
        }

        private void OnSkillExecuted(On.RoR2.GenericSkill.orig_OnExecute orig, GenericSkill self)
        {
            orig(self);

            if (!NetworkServer.active || !_stageActive)
            {
                return;
            }

            if (self.baseRechargeInterval < 6f)
            {
                return;
            }

            var body = self.characterBody;
            if (body == null || body.teamComponent == null || body.teamComponent.teamIndex != TeamIndex.Player)
            {
                return;
            }

            NotifyNearbyEnemiesCooldownExploit(body.corePosition, 30f);
        }

        private float OnHealthComponentHeal(On.RoR2.HealthComponent.orig_Heal orig, HealthComponent self, float amount, ProcChainMask procChainMask, bool nonRegen)
        {
            if (amount > 0f && self != null && self.body != null)
            {
                var master = self.body.master;
                if (master != null)
                {
                    var rc = master.GetComponent<WarfrontRoleController>();
                    if (rc != null && rc.IsCommander)
                    {
                        return 0f;
                    }
                }
            }

            return orig(self, amount, procChainMask, nonRegen);
        }

        private void OnRecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);

            if (self == null)
            {
                return;
            }

            var master = self.master;
            if (master == null)
            {
                return;
            }

            var rc = master.GetComponent<WarfrontRoleController>();
            if (rc != null && rc.IsCommander)
            {
                self.baseRegen = 0f;
                self.levelRegen = 0f;
                self.regen = 0f;
            }
        }

        private void ServerFixedUpdate(float deltaTime)
        {
            if (!_stageActive)
            {
                return;
            }

            _stageStopwatch += deltaTime;

            TickEventBuffWindow(deltaTime);
            TickHunterSquadTarget(deltaTime);
            TickBossChallengeBuffs(deltaTime);
            TickPostBossRespite(deltaTime);

            CleanupInvalidNodeReferences();
            ResolveTeleporterReference();
            TickBossGate();
            TickZoneShrink();
            TickTeleporterFog(deltaTime);
            TickCadence(deltaTime);
            TickFairness(deltaTime);
            TickContest(deltaTime);
            TickBreach(deltaTime);
            TickReactiveEscalation(deltaTime);
            TickSappers(deltaTime);
            TickSignalJamming(deltaTime);
            TickMedicNet(deltaTime);
            TickIntensity(deltaTime);
            TickNetworkSync(deltaTime);
        }

        private void TickFairness(float deltaTime)
        {
            if (_mercyCooldownTimer > 0f)
            {
                _mercyCooldownTimer = Mathf.Max(0f, _mercyCooldownTimer - deltaTime);
            }

            if (_mercyTimer > 0f)
            {
                _mercyTimer = Mathf.Max(0f, _mercyTimer - deltaTime);
            }

            var alivePlayers = GetAlivePlayerCount();
            if (_lastAlivePlayerCount <= 0)
            {
                _lastAlivePlayerCount = alivePlayers;
            }
            else if (alivePlayers < _lastAlivePlayerCount && _mercyCooldownTimer <= 0f)
            {
                var teamHealthFraction = GetTeamAverageHealthFraction();
                var mercyDuration = Mathf.Max(2f, WarfrontDirectorPlugin.MercyWindowSeconds.Value);
                if (teamHealthFraction > 0.7f)
                {
                    mercyDuration *= 0.5f;
                }
                else if (teamHealthFraction > 0.5f)
                {
                    mercyDuration *= 0.75f;
                }

                _mercyTimer = mercyDuration;
                _mercyCooldownTimer = Mathf.Max(_mercyTimer + 2f, WarfrontDirectorPlugin.MercyCooldownSeconds.Value);
                BroadcastWarfrontMessage("Frontline destabilized. Brief recovery window.", 6f);
            }

            _lastAlivePlayerCount = alivePlayers;

            if (!_teleporterEventActive || _holdoutZone == null || _teleporter == null || !_teleporter.isCharging || _teleporter.isCharged || alivePlayers <= 0)
            {
                _loneWolfPressure = Mathf.MoveTowards(_loneWolfPressure, 0f, deltaTime * 0.8f);
                return;
            }

            var defenders = Mathf.RoundToInt(ComputePlayerPresence(_holdoutZone));
            var attendanceFraction = defenders / (float)Mathf.Max(1, alivePlayers);
            var threshold = Mathf.Clamp(WarfrontDirectorPlugin.LoneWolfPressureThreshold.Value, 0.2f, 1f);
            var targetPressure = Mathf.Clamp01((threshold - attendanceFraction) / threshold);
            if (_mercyTimer > 0f)
            {
                targetPressure *= 0.4f;
            }

            _loneWolfPressure = Mathf.MoveTowards(_loneWolfPressure, targetPressure, deltaTime * 0.7f);
            _stageLoneWolfSignal += _loneWolfPressure * deltaTime;
        }

        private void TickCadence(float deltaTime)
        {
            if (!_teleporterEventActive)
            {
                _reconPulseTimer -= deltaTime;
                if (_reconPulseTimer <= 0f)
                {
                    SpawnReconPulse();
                    _reconPulseTimer = 14f;
                }

                return;
            }

            if (_teleporter == null || !_teleporter.isCharging || _teleporter.isCharged)
            {
                return;
            }

            if (_postBossKillRespitesEnabled)
            {
                return;
            }

            if (_bossGateActive)
            {
                _assaultActive = false;
                return;
            }

            if (IsSpawnRestWindowActive())
            {
                _assaultActive = false;
                return;
            }

            if (_operationRoll.Anomaly == WarAnomaly.SilentMinute && !_silentMinuteFinished)
            {
                _silentMinuteTimer += deltaTime;
                if (_silentMinuteTimer < 35f)
                {
                    _silentMinuteBufferedPulse += deltaTime;
                    return;
                }

                _silentMinuteFinished = true;
                BeginAssault(bonusCreditPulse: true);
            }

            _windowTimer -= deltaTime;
            if (_windowTimer <= 0f)
            {
                if (_assaultActive)
                {
                    BeginBreather(initial: false);
                }
                else
                {
                    BeginAssault(bonusCreditPulse: false);
                }
            }

            if (_assaultActive)
            {
                _assaultPulseTimer -= deltaTime;
                if (_assaultPulseTimer <= 0f)
                {
                    SpawnAssaultPulse();
                    _assaultPulseTimer = GetAssaultPulseInterval();
                }
            }
        }

        private void TickContest(float deltaTime)
        {
            ReleaseTeleporterChargePause();
        }

        private void TickBossGate()
        {
            if (!_bossGateActive || _holdoutZone == null)
            {
                return;
            }

            _holdoutZone.charge = 0f;
        }

        private void TickZoneShrink()
        {
            if (!_teleporterEventActive || _bossGateActive || _holdoutZone == null || _teleporter == null)
            {
                return;
            }

            var chargeFraction = Mathf.Clamp01(_holdoutZone.charge);
            var minFraction = Mathf.Clamp(WarfrontDirectorPlugin.ZoneShrinkMinPercent.Value / 100f, 0.3f, 1f);
            var shrinkFraction = Mathf.Lerp(1f, minFraction, chargeFraction);
            _holdoutZone.baseRadius = _originalHoldoutRadius * shrinkFraction;
        }

        private void RestoreHoldoutRadius()
        {
            if (_holdoutZone != null && _originalHoldoutRadius > 0f)
            {
                _holdoutZone.baseRadius = _originalHoldoutRadius;
            }
        }

        private int GetChargeTier()
        {
            if (_holdoutZone == null)
            {
                return 0;
            }

            var charge = Mathf.Clamp01(_holdoutZone.charge);
            if (charge >= 0.75f) return 3;
            if (charge >= 0.50f) return 2;
            if (charge >= 0.25f) return 1;
            return 0;
        }

        private void TickTeleporterFog(float deltaTime)
        {
            var teleporterCharging = _teleporter != null && _teleporter.isCharging && !_teleporter.isCharged;
            if (teleporterCharging && !_teleporterFogActive)
            {
                _teleporterFogActive = true;
                _teleporterFogTickTimer = 0f;
            }

            if (!_teleporterFogActive || _holdoutZone == null || _teleporter == null || !teleporterCharging)
            {
                _teleporterFogTickTimer = 0f;
                _teleporterFogExposureByMasterId.Clear();
                _teleporterFogSeenPlayerMasterIds.Clear();
                return;
            }

            _teleporterFogTickTimer -= deltaTime;
            var applyDotThisTick = _teleporterFogTickTimer <= 0f;
            if (applyDotThisTick)
            {
                _teleporterFogTickTimer = TeleporterFogDotRefreshInterval;
            }

            _teleporterFogSeenPlayerMasterIds.Clear();

            var playerMembers = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in playerMembers)
            {
                var body = member ? member.body : null;
                if (!IsTeleporterFogPlayerTarget(body, out var masterId))
                {
                    continue;
                }

                _teleporterFogSeenPlayerMasterIds.Add(masterId);

                if (_holdoutZone.IsBodyInChargingRadius(body))
                {
                    _teleporterFogExposureByMasterId[masterId] = 0f;
                    continue;
                }

                var exposure = _teleporterFogExposureByMasterId.TryGetValue(masterId, out var existingExposure)
                    ? existingExposure + deltaTime
                    : deltaTime;
                _teleporterFogExposureByMasterId[masterId] = exposure;

                ApplyTeleporterVoidFogDamage(body, exposure, deltaTime);

                if (!applyDotThisTick)
                {
                    continue;
                }

                ApplyTeleporterVoidFogDot(body, exposure);
            }

            if (_teleporterFogExposureByMasterId.Count <= 0)
            {
                return;
            }

            _teleporterFogStaleKeys.Clear();
            foreach (var masterId in _teleporterFogExposureByMasterId.Keys)
            {
                if (!_teleporterFogSeenPlayerMasterIds.Contains(masterId))
                {
                    _teleporterFogStaleKeys.Add(masterId);
                }
            }

            for (var i = 0; i < _teleporterFogStaleKeys.Count; i++)
            {
                _teleporterFogExposureByMasterId.Remove(_teleporterFogStaleKeys[i]);
            }
        }

        private static bool IsTeleporterFogPlayerTarget(CharacterBody body, out int masterId)
        {
            masterId = -1;
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return false;
            }

            var master = body.master;
            if (master == null)
            {
                return false;
            }

            if (master.minionOwnership != null && master.minionOwnership.ownerMaster != null)
            {
                return false;
            }

            if (!body.isPlayerControlled && master.playerCharacterMasterController == null)
            {
                return false;
            }

            masterId = master.GetInstanceID();
            return true;
        }

        private void ApplyTeleporterVoidFogDamage(CharacterBody body, float exposureSeconds, float deltaTime)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive || deltaTime <= 0f)
            {
                return;
            }

            var fullCombinedHealth = Mathf.Max(1f, body.healthComponent.fullCombinedHealth);
            var rampSeconds = Mathf.Max(0f, exposureSeconds - 1f);
            var rampPerSecond = Mathf.Min(TeleporterFogMaxRampDamageFractionPerSecond, rampSeconds * TeleporterFogRampDamageFractionPerSecond);
            var damageFractionPerSecond = TeleporterFogBaseDamageFractionPerSecond + rampPerSecond;
            var damage = fullCombinedHealth * damageFractionPerSecond * deltaTime;
            if (damage <= 0f)
            {
                return;
            }

            body.healthComponent.TakeDamage(new DamageInfo
            {
                damage = damage,
                attacker = null,
                inflictor = _teleporter ? _teleporter.gameObject : null,
                position = body.corePosition,
                force = Vector3.zero,
                crit = false,
                procCoefficient = 0f,
                damageColorIndex = DamageColorIndex.Void,
                damageType = DamageType.BypassArmor
            });
        }

        private void ApplyTeleporterVoidFogDot(CharacterBody body, float exposureSeconds)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            if (!TryResolveVoidFogDotIndex(exposureSeconds >= TeleporterFogStrongThresholdSeconds, out var dotIndex))
            {
                return;
            }

            DotController.InflictDot(body.gameObject, _teleporter ? _teleporter.gameObject : null, dotIndex, TeleporterFogDotDuration, 1f);

            if (TryResolveVoidFogBuffIndex(exposureSeconds >= TeleporterFogStrongThresholdSeconds, out var buffIndex))
            {
                body.AddTimedBuff(buffIndex, TeleporterFogDotDuration);
            }
        }

        private static void CacheVoidFogEnums()
        {
            if (_voidFogEnumsCached)
            {
                return;
            }

            _voidFogEnumsCached = true;

            _hasVoidFogDotStrong = Enum.TryParse("VoidFogStrong", out _cachedVoidFogDotStrong);
            _hasVoidFogDotMild = Enum.TryParse("VoidFogMild", out _cachedVoidFogDotMild);

            if (!_hasVoidFogDotMild && !_hasVoidFogDotStrong)
            {
                foreach (var name in Enum.GetNames(typeof(DotController.DotIndex)))
                {
                    if (name.IndexOf("VoidFog", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        Enum.TryParse(name, out _cachedVoidFogDotMild))
                    {
                        _hasVoidFogDotMild = true;
                        break;
                    }
                }
            }

            _hasVoidFogBuffStrong = Enum.TryParse("VoidFogStrong", out _cachedVoidFogBuffStrong);
            _hasVoidFogBuffMild = Enum.TryParse("VoidFogMild", out _cachedVoidFogBuffMild);

            if (!_hasVoidFogBuffMild && !_hasVoidFogBuffStrong)
            {
                foreach (var name in Enum.GetNames(typeof(BuffIndex)))
                {
                    if (name.IndexOf("VoidFog", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        Enum.TryParse(name, out _cachedVoidFogBuffMild))
                    {
                        _hasVoidFogBuffMild = true;
                        break;
                    }
                }
            }
        }

        private static bool TryResolveVoidFogDotIndex(bool preferStrong, out DotController.DotIndex dotIndex)
        {
            CacheVoidFogEnums();

            if (preferStrong && _hasVoidFogDotStrong)
            {
                dotIndex = _cachedVoidFogDotStrong;
                return true;
            }

            if (_hasVoidFogDotMild)
            {
                dotIndex = _cachedVoidFogDotMild;
                return true;
            }

            dotIndex = default;
            return false;
        }

        private static bool TryResolveVoidFogBuffIndex(bool preferStrong, out BuffIndex buffIndex)
        {
            CacheVoidFogEnums();

            if (preferStrong && _hasVoidFogBuffStrong)
            {
                buffIndex = _cachedVoidFogBuffStrong;
                return true;
            }

            if (_hasVoidFogBuffMild)
            {
                buffIndex = _cachedVoidFogBuffMild;
                return true;
            }

            buffIndex = default;
            return false;
        }

        private void PauseTeleporterCharge()
        {
            if (_holdoutZone == null)
            {
                return;
            }

            if (!_teleporterChargePaused)
            {
                _pausedTeleporterCharge = _holdoutZone.charge;
                _teleporterChargePaused = true;
            }

            _holdoutZone.charge = Mathf.Min(_holdoutZone.charge, _pausedTeleporterCharge);
        }

        private void ReleaseTeleporterChargePause()
        {
            if (!_teleporterChargePaused)
            {
                return;
            }

            _teleporterChargePaused = false;
            _pausedTeleporterCharge = _holdoutZone != null ? _holdoutZone.charge : 0f;
        }

        private void TickBreach(float deltaTime)
        {
            if (!_teleporterEventActive || _bossGateActive || _postBossKillRespitesEnabled)
            {
                _breachCooldownTimer = 0f;
                _breachDurationTimer = 0f;
                _breachActive = false;
                return;
            }

            if (_breachCooldownTimer > 0f)
            {
                _breachCooldownTimer = Mathf.Max(0f, _breachCooldownTimer - deltaTime);
            }

            if (_breachActive)
            {
                _breachDurationTimer -= deltaTime;
                _breachPulseTimer -= deltaTime;
                if (_breachPulseTimer <= 0f)
                {
                    SpawnBreachPulse();
                    _breachPulseTimer = GetBreachPulseInterval();
                }

                if (_breachDurationTimer <= 0f)
                {
                    EndBreach();
                }

                return;
            }

            if (_breachCooldownTimer > 0f)
            {
                return;
            }

            var chargeTier = GetChargeTier();
            var breachChance = 0f;
            if (chargeTier >= 2)
            {
                breachChance += 0.008f * deltaTime;
            }

            if (chargeTier >= 3)
            {
                breachChance += 0.012f * deltaTime;
            }

            if (HasWarning(WarWarning.SiegeEngine))
            {
                breachChance += 0.006f * deltaTime;
            }

            if (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
            {
                breachChance += 0.005f * deltaTime;
            }

            if (_loneWolfPressure > 0.5f)
            {
                breachChance += 0.004f * deltaTime;
            }

            if (_reactiveEscalationTimer > 0f)
            {
                breachChance += 0.01f * deltaTime;
            }

            if (breachChance > 0f && UnityEngine.Random.value < breachChance)
            {
                StartBreach();
            }
        }

        private void TickReactiveEscalation(float deltaTime)
        {
            if (!_teleporterEventActive || _bossGateActive || _postBossKillRespitesEnabled)
            {
                _reactiveEscalationTimer = 0f;
                _recentPlayerKills = Mathf.Max(0f, _recentPlayerKills - deltaTime * 0.5f);
                return;
            }

            _recentPlayerKills = Mathf.Max(0f, _recentPlayerKills - deltaTime * 0.8f);

            if (_reactiveEscalationTimer > 0f)
            {
                _reactiveEscalationTimer = Mathf.Max(0f, _reactiveEscalationTimer - deltaTime);
                return;
            }

            var playersDominating = _recentPlayerDamage < 5f && _recentPlayerKills > 6f;
            var chargingFast = _holdoutZone != null && _holdoutZone.charge > 0.15f && _intensity < 30f;
            var lowPressure = !_assaultActive && !_breachActive && _intensity < 25f && _stageStopwatch > 20f;

            if (!playersDominating && !chargingFast && !lowPressure)
            {
                return;
            }

            _reactiveEscalationTimer = UnityEngine.Random.Range(25f, 40f);

            if (playersDominating)
            {
                PivotDoctrine(NodeTypeCycle[UnityEngine.Random.Range(0, NodeTypeCycle.Length)]);
                if (!_assaultActive)
                {
                    _windowTimer = Mathf.Min(_windowTimer, 2f);
                }

                BroadcastWarfrontMessage("Enemy command detects weakness \u2014 escalating tactics.", 5f);
                return;
            }

            if (chargingFast)
            {
                var spawnCount = 2 + GetDifficultyTier();
                var objective = GetObjectivePosition();
                SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, spawnCount, objective, 10f, 22f, WarfrontRole.Contester);
                SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, 1, objective, 12f, 20f, WarfrontRole.Anchor);
                TriggerEventBuffWindow(8f, 0.22f);
                BoostNearbyMonsterMovement(objective, 40f, 0.25f, 5f);
                BroadcastWarfrontMessage("Enemy reinforcements rush the objective!", 5f);
                return;
            }

            if (lowPressure && !_assaultActive)
            {
                _windowTimer = Mathf.Min(_windowTimer, 1f);
                BroadcastWarfrontMessage("Enemy forces regroup and advance.", 5f);
            }
        }

        private void TickSappers(float deltaTime)
        {
            if (!HasWarning(WarWarning.Sappers) || !_teleporterEventActive || _holdoutZone == null)
            {
                return;
            }

            _sapperPulseTimer -= deltaTime;
            if (_sapperPulseTimer > 0f)
            {
                return;
            }

            _sapperPulseTimer = _assaultActive ? 4f : 6f;

            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                if (!_holdoutZone.IsBodyInChargingRadius(body))
                {
                    continue;
                }

                body.AddTimedBuff(RoR2Content.Buffs.Slow60, 3.5f);

                if (_assaultActive && body.healthComponent.combinedHealthFraction > 0.5f)
                {
                    body.AddTimedBuff(RoR2Content.Buffs.Weak, 2.5f);
                }
            }
        }

        private void TickSignalJamming(float deltaTime)
        {
            if (!HasWarning(WarWarning.SignalJamming) || !_teleporterEventActive || _holdoutZone == null)
            {
                return;
            }

            _signalJamPulseTimer -= deltaTime;
            if (_signalJamPulseTimer > 0f)
            {
                return;
            }

            _signalJamPulseTimer = 5f;

            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                if (!_holdoutZone.IsBodyInChargingRadius(body))
                {
                    continue;
                }

                body.AddTimedBuff(RoR2Content.Buffs.HealingDisabled, 3f);

                if (_breachActive)
                {
                    body.AddTimedBuff(RoR2Content.Buffs.Cripple, 2f);
                }
            }
        }

        private void TickMedicNet(float deltaTime)
        {
            if (!HasWarning(WarWarning.MedicNet) || !_teleporterEventActive || _holdoutZone == null)
            {
                return;
            }

            _medicPulseTimer -= deltaTime;
            if (_medicPulseTimer > 0f)
            {
                return;
            }

            _medicPulseTimer = 8f;

            var teams = new[] { TeamIndex.Monster, TeamIndex.Void };
            foreach (var teamIndex in teams)
            {
                var members = TeamComponent.GetTeamMembers(teamIndex);
                foreach (var member in members)
                {
                    var body = member ? member.body : null;
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                    {
                        continue;
                    }

                    if (_holdoutZone.IsBodyInChargingRadius(body))
                    {
                        body.healthComponent.HealFraction(0.07f, default);
                    }
                }
            }
        }

        private void TickIntensity(float deltaTime)
        {
            var target = 12f;
            target += Mathf.Clamp(_recentPlayerDamage * 0.015f, 0f, 35f);
            if (_teleporterEventActive)
            {
                target += 10f;
            }

            if (_assaultActive)
            {
                target += 24f;
            }

            if (HasWarning(WarWarning.Attrition))
            {
                target += 6f;
            }

            if (HasWarning(WarWarning.ReinforcedVanguard))
            {
                target += 4f;
            }

            if (HasWarning(WarWarning.SignalJamming))
            {
                target += 3f;
            }

            if (_operationRoll.Anomaly == WarAnomaly.Blackout)
            {
                target += 5f;
            }

            target += _loneWolfPressure * 10f;

            if (_mercyTimer > 0f)
            {
                target -= 10f;
            }

            target = Mathf.Clamp(target, 0f, 100f);

            var riseRate = 16f;
            var fallRate = 6f;
            _intensity = Mathf.MoveTowards(_intensity, target, deltaTime * (target > _intensity ? riseRate : fallRate));
            _recentPlayerDamage = Mathf.Max(0f, _recentPlayerDamage - deltaTime * 7.5f);
        }

        private void BeginBreather(bool initial)
        {
            _assaultActive = false;

            var min = 10f;
            var max = 20f;
            if (HasWarning(WarWarning.Attrition))
            {
                min -= 2f;
                max -= 3f;
            }

            if (HasWarning(WarWarning.SupplyLine))
            {
                min -= 1f;
                max -= 1.5f;
            }

            if (_operationRoll.Anomaly == WarAnomaly.WarDrums)
            {
                min -= 1f;
                max -= 2f;
            }

            if (_operationRoll.Anomaly == WarAnomaly.BlitzOrder)
            {
                min -= 2f;
                max -= 3f;
            }

            if (_operationRoll.Anomaly == WarAnomaly.CounterIntel)
            {
                min += 2f;
                max += 3f;
            }

            if (_falseLullPending)
            {
                min = 30f;
                max = 36f;
                _falseLullPending = false;
            }

            if (initial)
            {
                min += 2f;
                max += 2f;
            }

            if (_mercyTimer > 0f)
            {
                min += 2f;
                max += 3f;
            }

            _windowTimer = UnityEngine.Random.Range(min, max);

            var siegeTier = GetChargeTier();
            var escalation = Mathf.Clamp(WarfrontDirectorPlugin.SiegeEscalationMultiplier.Value, 0.5f, 2f);
            var breatherScale = Mathf.Lerp(1f, SiegeBreatherScale[siegeTier], escalation);
            _windowTimer *= breatherScale;

            _dominantRole = WarfrontRole.None;

            SetDirectorCadence(assault: false, recon: false);
        }

        private void BeginAssault(bool bonusCreditPulse)
        {
            _assaultActive = true;
            _staggerPhase = 0;
            _staggerDelayTimer = 0f;

            var min = 25f;
            var max = 45f;
            if (_operationRoll.Anomaly == WarAnomaly.WarDrums)
            {
                min -= 5f;
                max -= 6f;
            }

            if (_operationRoll.Anomaly == WarAnomaly.BlitzOrder)
            {
                min -= 6f;
                max -= 8f;
            }

            if (HasWarning(WarWarning.SiegeEngine))
            {
                min += 2f;
                max += 3f;
            }

            if (_mercyTimer > 0f)
            {
                min -= 5f;
                max -= 8f;
            }

            min = Mathf.Max(12f, min);
            max = Mathf.Max(min + 4f, max);

            _windowTimer = UnityEngine.Random.Range(min, max);
            _assaultPulseTimer = 1f;
            _dominantRole = ResolveDominantRole(forceRotate: true);

            var siegeTier = GetChargeTier();
            if (siegeTier >= 3 && !_siegeTier3Announced)
            {
                _siegeTier3Announced = true;
                BroadcastWarfrontMessage("Final push \u2014 enemy forces at maximum strength.");
            }

            SetDirectorCadence(assault: true, recon: false);
            TriggerEventBuffWindow(8f, 0.18f + (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront ? 0.04f : 0f));

            if (bonusCreditPulse && _silentMinuteBufferedPulse > 0f)
            {
                var bonusSpawns = Mathf.Clamp(Mathf.FloorToInt(_silentMinuteBufferedPulse / 8f), 1, 5);
                for (var i = 0; i < bonusSpawns; i++)
                {
                    SpawnAssaultPulse();
                }
            }
        }

        private void ApplyRollback(float deltaTime, float contestDelta)
        {
        }

        private float ComputePlayerPresence(HoldoutZoneController zone)
        {
            var count = 0;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                if (zone.IsBodyInChargingRadius(body))
                {
                    count++;
                }
            }

            return count <= 0 ? 0f : Mathf.Sqrt(count);
        }

        private float ComputeEnemyContestWeight(HoldoutZoneController zone)
        {
            return 0f;
        }

        private float ComputeEnemyContestWeight(HoldoutZoneController zone, TeamIndex teamIndex)
        {
            return 0f;
        }

        private void StartBreach()
        {
            _breachActive = true;
            _breachDurationTimer = UnityEngine.Random.Range(12f, 18f);
            _breachPulseTimer = 0.5f;
            _stageBreachCount++;

            var objective = GetObjectivePosition();
            var heavyCount = 1 + Mathf.Clamp(GetDifficultyTier() - 1, 0, 2);
            if (HasWarning(WarWarning.SiegeEngine))
            {
                heavyCount += 1;
            }

            SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, heavyCount, objective, 8f, 18f, WarfrontRole.Anchor);
            SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 2 + GetDifficultyTier(), objective, 10f, 22f, WarfrontRole.Contester);

            var isolated = GetMostIsolatedPlayer();
            if (isolated != null)
            {
                SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 2, isolated.corePosition, 12f, 20f, WarfrontRole.Hunter);
            }

            BoostNearbyMonsterMovement(objective, 50f, 0.3f, 6f);
            TriggerEventBuffWindow(10f, 0.3f);
            SetDirectorCadence(assault: true, recon: false);
            BroadcastWarfrontMessage("<color=#ff3333>BREACH!</color> Enemy forces overrunning the zone!", 3f);
        }

        private void EndBreach()
        {
            _breachActive = false;
            _breachCooldownTimer = UnityEngine.Random.Range(35f, 55f);
            _breachDurationTimer = 0f;
            _breachPulseTimer = 0f;

            if (_assaultActive)
            {
                SetDirectorCadence(assault: true, recon: false);
            }
            else
            {
                BeginBreather(initial: false);
            }

            BroadcastWarfrontMessage("Breach repelled. Enemy regrouping.", 3f);
        }

        private void SpawnBreachPulse()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            var objective = GetObjectivePosition();
            var count = 1 + Mathf.Clamp(GetDifficultyTier(), 0, 2);
            if (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
            {
                count += 1;
            }

            var role = UnityEngine.Random.value < 0.4f ? WarfrontRole.Contester : WarfrontRole.Flanker;
            SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, count, objective, 8f, 20f, role);

            if (UnityEngine.Random.value < 0.3f + GetChargeTier() * 0.1f)
            {
                SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, 1, objective, 10f, 16f, WarfrontRole.Anchor);
            }
        }

        private float GetBreachPulseInterval()
        {
            var interval = UnityEngine.Random.Range(3.5f, 5.5f);
            if (HasWarning(WarWarning.SiegeEngine))
            {
                interval -= 0.6f;
            }

            if (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
            {
                interval -= 0.4f;
            }

            return Mathf.Max(2f, interval);
        }

        private void SpawnReconPulse()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            var spawnCount = 1 + GetDifficultyTier();
            spawnCount += Mathf.Min(1, GetNodeCount(WarfrontNodeType.SpawnCache));
            if (HasWarning(WarWarning.SupplyLine))
            {
                spawnCount += 1;
            }

            if (_operationRoll.Anomaly == WarAnomaly.CounterIntel)
            {
                spawnCount = Mathf.Max(1, spawnCount - 1);
            }

            switch (_currentDoctrine)
            {
                case WarfrontDoctrineProfile.SwarmFront:
                    spawnCount += 1;
                    break;
                case WarfrontDoctrineProfile.ArtilleryFront:
                    spawnCount = Mathf.Max(1, spawnCount - 1);
                    break;
                case WarfrontDoctrineProfile.HunterCell:
                    if (_loneWolfPressure > 0.2f)
                    {
                        spawnCount += 1;
                    }

                    break;
                case WarfrontDoctrineProfile.SiegeFront:
                    spawnCount += 1;
                    break;
                case WarfrontDoctrineProfile.DisruptionFront:
                    if (UnityEngine.Random.value < 0.45f)
                    {
                        spawnCount += 1;
                    }

                    break;
            }

            if (_mercyTimer > 0f)
            {
                spawnCount = Mathf.Max(1, spawnCount - 1);
            }

            spawnCount = Mathf.Clamp(spawnCount, 1, 5);
            var anchor = GetPlayerCenterPosition();
            if (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
            {
                anchor = GetObjectivePosition();
            }
            else if (_currentDoctrine == WarfrontDoctrineProfile.HunterCell)
            {
                var isolated = GetMostIsolatedPlayer();
                if (isolated != null)
                {
                    anchor = isolated.corePosition;
                }
            }

            SpawnMonsterPack(WarfrontAssetCatalog.ReconMasterPrefabs, spawnCount, anchor, 20f, 32f, WarfrontRole.Peeler);
            AccumulateRoleSignal(WarfrontRole.Peeler, spawnCount * 0.05f);
        }

        private void SpawnAssaultPulse()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (_staggerPhase == 0)
            {
                _dominantRole = WarfrontRole.Contester;
                _staggerPhase = 1;
                _staggerDelayTimer = UnityEngine.Random.Range(1.5f, 2.5f);
            }
            else if (_staggerPhase == 1)
            {
                _staggerDelayTimer -= _assaultPulseTimer;
                if (_staggerDelayTimer > 0f)
                {
                    _dominantRole = WarfrontRole.Contester;
                }
                else
                {
                    _dominantRole = ResolveDominantRole(forceRotate: true);
                    _staggerPhase = 2;
                    _staggerDelayTimer = UnityEngine.Random.Range(2f, 3.5f);
                }
            }
            else if (_staggerPhase == 2)
            {
                _staggerDelayTimer -= _assaultPulseTimer;
                if (_staggerDelayTimer > 0f)
                {
                    _dominantRole = ResolveDominantRole(forceRotate: false);
                }
                else
                {
                    var supportRoll = UnityEngine.Random.value;
                    _dominantRole = supportRoll < 0.35f ? WarfrontRole.Flanker
                        : supportRoll < 0.6f ? WarfrontRole.Hunter
                        : supportRoll < 0.8f ? WarfrontRole.Artillery
                        : WarfrontRole.Peeler;
                    _staggerPhase = 3;
                }
            }
            else
            {
                _dominantRole = ResolveDominantRole(forceRotate: false);
            }

            var roleBudget = Mathf.Clamp(WarfrontDirectorPlugin.RoleBudgetMultiplier.Value, 0.5f, 2f);
            var count = Mathf.RoundToInt((2 + GetDifficultyTier()) * roleBudget);

            if (HasWarning(WarWarning.PhalanxDoctrine))
            {
                count += 1;
            }

            if (HasWarning(WarWarning.PackTactics))
            {
                count += 1;
            }

            if (HasWarning(WarWarning.Attrition))
            {
                count = Mathf.Max(1, count - 1);
            }

            if (_operationRoll.Anomaly == WarAnomaly.CommandConfusion)
            {
                count = Mathf.Max(1, count - 1);
            }

            if (_operationRoll.Anomaly == WarAnomaly.BlitzOrder)
            {
                count += 1;
            }

            switch (_currentDoctrine)
            {
                case WarfrontDoctrineProfile.SwarmFront:
                    count += 2;
                    break;
                case WarfrontDoctrineProfile.ArtilleryFront:
                    count = Mathf.Max(1, count - 1);
                    break;
                case WarfrontDoctrineProfile.HunterCell:
                    if (_loneWolfPressure > 0.25f)
                    {
                        count += 1;
                    }

                    break;
                case WarfrontDoctrineProfile.SiegeFront:
                    count += 1;
                    break;
                case WarfrontDoctrineProfile.DisruptionFront:
                    count += UnityEngine.Random.value < 0.35f ? 1 : 0;
                    break;
            }

            if (_mercyTimer > 0f)
            {
                count = Mathf.Max(1, count - 1);
            }

            var siegeTier = GetChargeTier();
            count += SiegeSpawnCountBonus[siegeTier];

            var objective = GetObjectivePosition();
            var isolated = GetMostIsolatedPlayer();
            var anchor = objective;
            var minDistance = 10f;
            var maxDistance = 24f;

            switch (_dominantRole)
            {
                case WarfrontRole.Contester:
                    anchor = objective;
                    minDistance = 8f;
                    maxDistance = 18f;
                    break;
                case WarfrontRole.Peeler:
                    anchor = GetPlayerCenterPosition();
                    minDistance = 10f;
                    maxDistance = 22f;
                    break;
                case WarfrontRole.Flanker:
                    anchor = GetFlankPointForAI(objective);
                    minDistance = 12f;
                    maxDistance = 26f;
                    break;
                case WarfrontRole.Artillery:
                    anchor = FindGroundedPosition(objective, 24f, 42f);
                    minDistance = 16f;
                    maxDistance = 30f;
                    break;
                case WarfrontRole.Hunter:
                    anchor = isolated != null ? isolated.corePosition : objective;
                    minDistance = 12f;
                    maxDistance = 21f;
                    break;
                case WarfrontRole.Anchor:
                    anchor = objective;
                    minDistance = 7f;
                    maxDistance = 15f;
                    break;
            }

            switch (_currentDoctrine)
            {
                case WarfrontDoctrineProfile.SwarmFront:
                    maxDistance += 4f;
                    break;
                case WarfrontDoctrineProfile.ArtilleryFront:
                    minDistance += 4f;
                    maxDistance += 8f;
                    break;
                case WarfrontDoctrineProfile.HunterCell:
                    if (isolated != null)
                    {
                        anchor = isolated.corePosition;
                    }

                    break;
                case WarfrontDoctrineProfile.SiegeFront:
                    minDistance = Mathf.Max(6f, minDistance - 2f);
                    maxDistance = Mathf.Min(maxDistance, 20f);
                    break;
                case WarfrontDoctrineProfile.DisruptionFront:
                    minDistance += 1f;
                    maxDistance += 2f;
                    break;
            }

            minDistance = Mathf.Max(5f, minDistance);
            maxDistance = Mathf.Max(minDistance + 4f, maxDistance);

            SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, count, anchor, minDistance, maxDistance, _dominantRole);
            AccumulateRoleSignal(_dominantRole, Mathf.Max(0.4f, count * 0.2f));

            if (HasWarning(WarWarning.ArtilleryDoctrine) || _dominantRole == WarfrontRole.Artillery || _operationRoll.Anomaly == WarAnomaly.IronRain)
            {
                var artilleryExtras = 1 + (_operationRoll.Anomaly == WarAnomaly.IronRain ? 1 : 0);
                if (_currentDoctrine == WarfrontDoctrineProfile.ArtilleryFront)
                {
                    artilleryExtras += 1;
                }

                SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, artilleryExtras, objective, 24f, 35f, WarfrontRole.Artillery);
                AccumulateRoleSignal(WarfrontRole.Artillery, artilleryExtras * 0.25f);
            }

            if (HasWarning(WarWarning.HunterKiller) || HasWarning(WarWarning.ExecutionOrder) || _dominantRole == WarfrontRole.Hunter || _loneWolfPressure > 0.45f || _currentDoctrine == WarfrontDoctrineProfile.HunterCell)
            {
                if (isolated != null)
                {
                    var hunterCount = 2 + Mathf.Clamp(GetNodeCount(WarfrontNodeType.Siren), 0, 1);
                    if (_currentDoctrine == WarfrontDoctrineProfile.HunterCell)
                    {
                        hunterCount += 1;
                    }

                    SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, hunterCount, isolated.corePosition, 14f, 21f, WarfrontRole.Hunter);
                    AccumulateRoleSignal(WarfrontRole.Hunter, hunterCount * 0.24f);
                }
            }

            var forgeNodes = GetForgeNodeCount();
            var heavyChance = 0.4f + 0.1f * forgeNodes;
            if (_dominantRole == WarfrontRole.Anchor)
            {
                heavyChance += 0.12f;
            }

            if (HasWarning(WarWarning.SiegeEngine))
            {
                heavyChance += 0.15f;
            }

            if (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
            {
                heavyChance += 0.1f;
            }
            else if (_currentDoctrine == WarfrontDoctrineProfile.SwarmFront)
            {
                heavyChance -= 0.08f;
            }

            heavyChance += SiegeHeavyChanceBonus[siegeTier];

            if (forgeNodes > 0 && UnityEngine.Random.value < Mathf.Clamp01(heavyChance))
            {
                SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, 1, objective, 14f, 20f, WarfrontRole.Anchor);
                AccumulateRoleSignal(WarfrontRole.Anchor, 0.25f);
            }

            if (_operationRoll.Anomaly == WarAnomaly.WarDrums)
            {
                BoostNearbyMonsterMovement(objective, 32f, 0.15f, 6f);
            }

            if (_operationRoll.Anomaly == WarAnomaly.Blackout)
            {
                SpawnMonsterPack(WarfrontAssetCatalog.ReconMasterPrefabs, 1, objective, 18f, 30f, WarfrontRole.Flanker);
            }

            if (HasWarning(WarWarning.SupplyLine) && UnityEngine.Random.value < 0.35f)
            {
                SpawnMonsterPack(WarfrontAssetCatalog.ReconMasterPrefabs, 1, objective, 16f, 26f, WarfrontRole.Flanker);
            }
        }

        private void SpawnMonsterPack(IReadOnlyList<GameObject> pool, int count, Vector3 anchor, float minDistance, float maxDistance, WarfrontRole roleHint = WarfrontRole.None)
        {
            if (pool == null || pool.Count == 0)
            {
                return;
            }

            if (IsSpawnRestWindowActive())
            {
                return;
            }

            var playerClumped = IsPlayerTeamClumped();

            for (var i = 0; i < count; i++)
            {
                var prefab = PickWeightedPrefab(pool, roleHint, playerClumped);
                if (!prefab)
                {
                    continue;
                }

                var spawnPosition = FindGroundedPosition(anchor, minDistance, maxDistance);
                var summon = new MasterSummon
                {
                    masterPrefab = prefab,
                    position = spawnPosition,
                    rotation = Quaternion.identity,
                    ignoreTeamMemberLimit = true,
                    teamIndexOverride = TeamIndex.Monster,
                    useAmbientLevel = true
                };

                var spawnedMaster = summon.Perform();
                if (spawnedMaster)
                {
                    if (TryApplyBossChallengeAffix(spawnedMaster))
                    {
                        continue;
                    }

                    EnsureSpawnOutsideTeleporterRadius(spawnedMaster, spawnPosition);
                    AttachRoleController(spawnedMaster, roleHint);
                }
            }
        }

        private GameObject PickWeightedPrefab(IReadOnlyList<GameObject> pool, WarfrontRole roleHint, bool playerClumped)
        {
            if (pool.Count <= 1)
            {
                return pool.Count == 1 ? pool[0] : null;
            }

            var weights = new float[pool.Count];
            var totalWeight = 0f;

            for (var i = 0; i < pool.Count; i++)
            {
                var prefab = pool[i];
                if (!prefab)
                {
                    weights[i] = 0f;
                    continue;
                }

                var weight = 1f;
                var prefabName = prefab.name.ToUpperInvariant();
                var isRanged = prefabName.Contains("WISP") || prefabName.Contains("GOLEM");
                var isFast = prefabName.Contains("LEMURIAN") || prefabName.Contains("WISP");
                var isSwarm = prefabName.Contains("BEETLE") || prefabName.Contains("LEMURIAN");

                switch (roleHint)
                {
                    case WarfrontRole.Artillery:
                        if (isRanged) weight += 1.5f;
                        break;
                    case WarfrontRole.Hunter:
                    case WarfrontRole.Flanker:
                        if (isFast) weight += 1.2f;
                        break;
                    case WarfrontRole.Contester:
                    case WarfrontRole.Anchor:
                        if (!isFast) weight += 0.8f;
                        break;
                    case WarfrontRole.Peeler:
                        if (isSwarm) weight += 0.6f;
                        break;
                }

                if (playerClumped && isRanged)
                {
                    weight += 0.8f;
                }

                if (!playerClumped && isFast)
                {
                    weight += 0.6f;
                }

                weights[i] = Mathf.Max(0.2f, weight);
                totalWeight += weights[i];
            }

            if (totalWeight <= 0f)
            {
                return pool[UnityEngine.Random.Range(0, pool.Count)];
            }

            var roll = UnityEngine.Random.Range(0f, totalWeight);
            for (var i = 0; i < pool.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }

        private bool IsPlayerTeamClumped()
        {
            var members = TeamComponent.GetTeamMembers(TeamIndex.Player);
            var center = Vector3.zero;
            var playerCount = 0;
            foreach (var member in members)
            {
                var b = member ? member.body : null;
                if (b == null || b.healthComponent == null || !b.healthComponent.alive)
                {
                    continue;
                }

                center += b.corePosition;
                playerCount++;
            }

            if (playerCount <= 1)
            {
                return true;
            }

            center /= playerCount;

            var maxDistSqr = 0f;
            foreach (var member in members)
            {
                var b = member ? member.body : null;
                if (b == null || b.healthComponent == null || !b.healthComponent.alive)
                {
                    continue;
                }

                var distSqr = (b.corePosition - center).sqrMagnitude;
                if (distSqr > maxDistSqr)
                {
                    maxDistSqr = distSqr;
                }
            }

            return maxDistSqr < 20f * 20f;
        }

        private void BoostNearbyMonsterMovement(Vector3 origin, float radius, float boostMultiplier, float duration)
        {
            var radiusSqr = radius * radius;
            var teams = EnemyTeams;
            foreach (var teamIndex in teams)
            {
                var members = TeamComponent.GetTeamMembers(teamIndex);
                foreach (var member in members)
                {
                    var body = member ? member.body : null;
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                    {
                        continue;
                    }

                    if ((body.corePosition - origin).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }

                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, duration * (1f + boostMultiplier));
                }
            }
        }

        private void SpawnEnemyNodes()
        {
            CleanupNodes();

            if (!NetworkServer.active)
            {
                return;
            }

            var stageIndex = Run.instance != null ? Run.instance.stageClearCount : 0;
            var alivePlayers = Mathf.Max(1, GetAlivePlayerCount());

            _stageCommanderQuota = Mathf.Clamp(2 + stageIndex / 4 + Mathf.Max(0, alivePlayers - 2) / 2, 2, 4);
            var stageEntryCount = Mathf.Clamp(1 + stageIndex / 7, 1, Mathf.Max(1, _stageCommanderQuota - 1));
            _pendingTeleporterCommanderSpawns = Mathf.Max(1, _stageCommanderQuota - stageEntryCount);

            SpawnCommanderGroup(stageEntryCount, aroundObjective: false);
            BroadcastWarfrontMessage($"Enemy command deployed ({stageEntryCount}/{_stageCommanderQuota}).", 2.5f);
        }

        private void SpawnTeleporterCommanders()
        {
            if (!NetworkServer.active || _pendingTeleporterCommanderSpawns <= 0)
            {
                return;
            }

            var spawnCount = _pendingTeleporterCommanderSpawns;
            _pendingTeleporterCommanderSpawns = 0;
            SpawnCommanderGroup(spawnCount, aroundObjective: true);
            BroadcastWarfrontMessage("Command elites reinforce the objective.", 2.5f);
        }

        private void SpawnCommanderGroup(int count, bool aroundObjective)
        {
            if (count <= 0)
            {
                return;
            }

            var anchor = aroundObjective ? GetObjectivePosition() : GetPlayerCenterPosition();
            if (anchor == Vector3.zero)
            {
                anchor = GetObjectivePosition();
            }

            var minDistance = aroundObjective ? 24f : 40f;
            var maxDistance = aroundObjective ? 46f : 70f;

            for (var i = 0; i < count; i++)
            {
                var commanderType = NodeTypeCycle[_nodeTypeCursor % NodeTypeCycle.Length];
                _nodeTypeCursor++;

                var prefab = PickCommanderPrefab(commanderType);
                if (!prefab)
                {
                    continue;
                }

                var position = FindGroundedPosition(anchor, minDistance + i * 2f, maxDistance + i * 3f);
                var summon = new MasterSummon
                {
                    masterPrefab = prefab,
                    position = position,
                    rotation = Quaternion.identity,
                    ignoreTeamMemberLimit = true,
                    teamIndexOverride = TeamIndex.Monster,
                    useAmbientLevel = true
                };

                var master = summon.Perform();
                if (!master)
                {
                    continue;
                }

                var commanderRole = ResolveCommanderRole(commanderType);
                var commanderRoleController = master.GetComponent<WarfrontRoleController>();
                if (!commanderRoleController)
                {
                    commanderRoleController = master.gameObject.AddComponent<WarfrontRoleController>();
                }

                commanderRoleController.Initialize(this, master, commanderRole, _currentDoctrine, isCommander: true);

                var commandZonePosition = EnsureSpawnOutsideTeleporterRadius(master, position);
                ApplyCommanderEliteAffix(master, commanderType);

                var node = master.GetComponent<WarfrontNode>();
                if (!node)
                {
                    node = master.gameObject.AddComponent<WarfrontNode>();
                }

                node.Initialize(
                    this,
                    commanderType,
                    master,
                    commandZonePosition,
                    effectRadius: GetCommanderEffectRadius(commanderType),
                    tetherDistance: Mathf.Max(20f, WarfrontDirectorPlugin.CommanderTetherDistance.Value * 0.6f));

                _activeNodes.Add(node);
            }
        }

        internal void OnCommanderDefeated(WarfrontNode node)
        {
            if (!NetworkServer.active || node == null)
            {
                return;
            }

            if (!_activeNodes.Remove(node))
            {
                return;
            }

            var position = node.CommandZonePosition;
            var nodeType = node.NodeType;

            GrantCommanderRewardBurst(nodeType);

            var chestCount = nodeType == WarfrontNodeType.SpawnCache ? 2 : 1;
            SpawnNodeRewardChest(position, chestCount);
            TriggerCommanderResponse(position, nodeType);

            BroadcastWarfrontMessage($"{ToDisplayName(nodeType)} eliminated. Enemy command adapting.", 3f);
        }

        private void SpawnRetaliationMicroWave(Vector3 nodePosition, WarfrontNodeType nodeType)
        {
            var count = 2;
            switch (nodeType)
            {
                case WarfrontNodeType.Forge:
                    count = 3;
                    SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, 1, nodePosition, 10f, 15f, WarfrontRole.Anchor);
                    break;
                case WarfrontNodeType.Siren:
                    count = 3;
                    break;
                case WarfrontNodeType.SpawnCache:
                    count = 4;
                    break;
            }

            if (HasWarning(WarWarning.ExecutionOrder))
            {
                count += 1;
            }

            SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, count, nodePosition, 9f, 16f, WarfrontRole.Contester);

            if (nodeType == WarfrontNodeType.Siren)
            {
                var isolated = GetMostIsolatedPlayer();
                if (isolated != null)
                {
                    SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 1, isolated.corePosition, 12f, 18f, WarfrontRole.Hunter);
                }
            }

            TriggerEventBuffWindow(8f, 0.22f);
        }

        private void TriggerCommanderResponse(Vector3 position, WarfrontNodeType nodeType)
        {
            var responseRoll = UnityEngine.Random.Range(0, 4);
            switch (responseRoll)
            {
                case 0:
                    SpawnRetaliationMicroWave(position, nodeType);
                    break;
                case 1:
                    PivotDoctrine(nodeType);
                    break;
                case 2:
                    SpawnBreachDeployment(position, nodeType);
                    break;
                default:
                    DoCreditDump(position, nodeType);
                    break;
            }
        }

        private void PivotDoctrine(WarfrontNodeType nodeType)
        {
            var pivotDoctrine = nodeType switch
            {
                WarfrontNodeType.Relay => WarfrontDoctrineProfile.SiegeFront,
                WarfrontNodeType.Forge => WarfrontDoctrineProfile.ArtilleryFront,
                WarfrontNodeType.Siren => WarfrontDoctrineProfile.HunterCell,
                WarfrontNodeType.SpawnCache => WarfrontDoctrineProfile.SwarmFront,
                _ => _currentDoctrine
            };

            if (pivotDoctrine == _currentDoctrine)
            {
                pivotDoctrine = DoctrinePool[UnityEngine.Random.Range(0, DoctrinePool.Length)];
            }

            _currentDoctrine = pivotDoctrine;
            _operationRoll = RollOperations(_currentDoctrine);
            _activeWarnings.Clear();
            _activeWarnings.Add(_operationRoll.WarningOne);
            _activeWarnings.Add(_operationRoll.WarningTwo);
            _operationSummary = $"{ToDisplayName(_currentDoctrine)} | {ToDisplayName(_operationRoll.WarningOne)}, {ToDisplayName(_operationRoll.WarningTwo)} - {ToDisplayName(_operationRoll.Anomaly)}";

            SetDirectorCadence(_assaultActive, recon: !_teleporterEventActive);
            TriggerEventBuffWindow(8f, 0.18f);
            BroadcastWarfrontMessage($"Doctrine pivot: {ToDisplayName(_currentDoctrine)}.", 2.5f);
        }

        private void SpawnBreachDeployment(Vector3 position, WarfrontNodeType nodeType)
        {
            var heavyCount = nodeType == WarfrontNodeType.Forge ? 2 : 1;
            SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, heavyCount, position, 8f, 16f, WarfrontRole.Anchor);
            TriggerEventBuffWindow(10f, 0.24f);
            BroadcastWarfrontMessage("Enemy breach deployment incoming!", 3f);
        }

        private void DoCreditDump(Vector3 position, WarfrontNodeType nodeType)
        {
            var dumpCount = 1 + Mathf.Clamp(GetDifficultyTier(), 0, 2);
            if (nodeType == WarfrontNodeType.SpawnCache)
            {
                dumpCount += 1;
            }

            var role = nodeType == WarfrontNodeType.Siren ? WarfrontRole.Hunter : WarfrontRole.Contester;
            SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, dumpCount, position, 10f, 20f, role);
            TriggerEventBuffWindow(7f, 0.18f);
            BroadcastWarfrontMessage("Enemy reserves committed immediately.", 3f);
        }

        private void GrantCommanderRewardBurst(WarfrontNodeType nodeType)
        {
            var baseReward = nodeType switch
            {
                WarfrontNodeType.Relay => WarfrontDirectorPlugin.CommanderRewardRelayBase.Value,
                WarfrontNodeType.Forge => WarfrontDirectorPlugin.CommanderRewardForgeBase.Value,
                WarfrontNodeType.Siren => WarfrontDirectorPlugin.CommanderRewardSirenBase.Value,
                WarfrontNodeType.SpawnCache => WarfrontDirectorPlugin.CommanderRewardCacheBase.Value,
                _ => WarfrontDirectorPlugin.NodeRewardGold.Value
            };

            var stageCount = Run.instance != null ? Run.instance.stageClearCount : 0;
            var difficultyTier = GetDifficultyTier();
            var alivePlayers = Mathf.Max(1, GetAlivePlayerCount());

            var scale = 1f;
            scale += stageCount * Mathf.Max(0f, WarfrontDirectorPlugin.CommanderRewardStageScale.Value);
            scale += difficultyTier * Mathf.Max(0f, WarfrontDirectorPlugin.CommanderRewardDifficultyScale.Value);
            scale += Mathf.Max(0, alivePlayers - 1) * Mathf.Max(0f, WarfrontDirectorPlugin.CommanderRewardPlayerScale.Value);

            var payout = Mathf.Max(0f, baseReward * scale);
            var payoutInt = (uint)Mathf.Max(1f, Mathf.Round(payout));

            foreach (var member in TeamComponent.GetTeamMembers(TeamIndex.Player))
            {
                var master = member ? member.body?.master : null;
                if (master != null)
                {
                    master.GiveMoney(payoutInt);
                }
            }
        }

        private static float GetCommanderEffectRadius(WarfrontNodeType nodeType)
        {
            return nodeType switch
            {
                WarfrontNodeType.Relay => 42f,
                WarfrontNodeType.Forge => 38f,
                WarfrontNodeType.Siren => 36f,
                WarfrontNodeType.SpawnCache => 40f,
                _ => 36f
            };
        }

        private WarfrontRole ResolveCommanderRole(WarfrontNodeType nodeType)
        {
            return nodeType switch
            {
                WarfrontNodeType.Relay => WarfrontRole.Contester,
                WarfrontNodeType.Forge => WarfrontRole.Anchor,
                WarfrontNodeType.Siren => WarfrontRole.Hunter,
                WarfrontNodeType.SpawnCache => WarfrontRole.Flanker,
                _ => WarfrontRole.Contester
            };
        }

        private GameObject PickCommanderPrefab(WarfrontNodeType nodeType)
        {
            var pool = WarfrontAssetCatalog.CommanderMasterPrefabs;
            if (pool.Count > 0)
            {
                return pool[UnityEngine.Random.Range(0, pool.Count)];
            }

            if (WarfrontAssetCatalog.HeavyMasterPrefabs.Count > 0)
            {
                return WarfrontAssetCatalog.HeavyMasterPrefabs[UnityEngine.Random.Range(0, WarfrontAssetCatalog.HeavyMasterPrefabs.Count)];
            }

            if (WarfrontAssetCatalog.AssaultMasterPrefabs.Count > 0)
            {
                return WarfrontAssetCatalog.AssaultMasterPrefabs[UnityEngine.Random.Range(0, WarfrontAssetCatalog.AssaultMasterPrefabs.Count)];
            }

            return null;
        }

        private void SpawnNodeRewardChest(Vector3 nodePosition, int chestCount)
        {
            if (!WarfrontAssetCatalog.ChestSmallPrefab)
            {
                return;
            }

            chestCount = Mathf.Clamp(chestCount, 1, 3);
            for (var i = 0; i < chestCount; i++)
            {
                var spawnPosition = FindGroundedPosition(nodePosition, 3f + i * 1.5f, 6f + i * 2f);
                var chest = Instantiate(WarfrontAssetCatalog.ChestSmallPrefab, spawnPosition, Quaternion.identity);
                var identity = chest.GetComponent<NetworkIdentity>();
                if (identity)
                {
                    NetworkServer.Spawn(chest);
                }
            }
        }

        private void CacheDirectors()
        {
            _directorDefaults.Clear();

            var directors = FindObjectsOfType<CombatDirector>();
            foreach (var director in directors)
            {
                if (!director || _directorDefaults.ContainsKey(director))
                {
                    continue;
                }

                _directorDefaults.Add(director, new DirectorDefaults
                {
                    CreditMultiplier = director.creditMultiplier,
                    MinSeriesSpawnInterval = director.minSeriesSpawnInterval,
                    MaxSeriesSpawnInterval = director.maxSeriesSpawnInterval,
                    EliteBias = director.eliteBias
                });
            }
        }

        private void SetDirectorCadence(bool assault, bool recon)
        {
            _staleDirectorKeys.Clear();
            foreach (var pair in _directorDefaults)
            {
                var director = pair.Key;
                if (!director)
                {
                    _staleDirectorKeys.Add(pair.Key);
                    continue;
                }

                var defaults = pair.Value;
                if (assault)
                {
                    var assaultMultiplier = _operationRoll.Anomaly == WarAnomaly.WarDrums ? 1.75f : 1.45f;
                    if (_operationRoll.Anomaly == WarAnomaly.IronRain)
                    {
                        assaultMultiplier += 0.15f;
                    }

                    if (_operationRoll.Anomaly == WarAnomaly.BlitzOrder)
                    {
                        assaultMultiplier += 0.12f;
                    }

                    if (HasWarning(WarWarning.SupplyLine))
                    {
                        assaultMultiplier += 0.08f;
                    }

                    if (_mercyTimer > 0f)
                    {
                        assaultMultiplier *= 0.85f;
                    }

                    switch (_currentDoctrine)
                    {
                        case WarfrontDoctrineProfile.SwarmFront:
                            assaultMultiplier += 0.12f;
                            break;
                        case WarfrontDoctrineProfile.ArtilleryFront:
                            assaultMultiplier += 0.07f;
                            break;
                        case WarfrontDoctrineProfile.HunterCell:
                            assaultMultiplier += 0.09f;
                            break;
                        case WarfrontDoctrineProfile.SiegeFront:
                            assaultMultiplier += 0.1f;
                            break;
                        case WarfrontDoctrineProfile.DisruptionFront:
                            assaultMultiplier += 0.05f;
                            break;
                    }

                    director.creditMultiplier = defaults.CreditMultiplier * assaultMultiplier;

                    var intervalScale = _dominantRole switch
                    {
                        WarfrontRole.Artillery => 0.78f,
                        WarfrontRole.Hunter => 0.68f,
                        _ => 0.7f
                    };

                    if (_operationRoll.Anomaly == WarAnomaly.CounterIntel)
                    {
                        intervalScale += 0.08f;
                    }

                    switch (_currentDoctrine)
                    {
                        case WarfrontDoctrineProfile.SwarmFront:
                            intervalScale -= 0.06f;
                            break;
                        case WarfrontDoctrineProfile.ArtilleryFront:
                            intervalScale += 0.08f;
                            break;
                        case WarfrontDoctrineProfile.HunterCell:
                            intervalScale -= 0.03f;
                            break;
                        case WarfrontDoctrineProfile.SiegeFront:
                            intervalScale += 0.03f;
                            break;
                        case WarfrontDoctrineProfile.DisruptionFront:
                            intervalScale -= 0.02f;
                            break;
                    }

                    intervalScale = Mathf.Clamp(intervalScale, 0.52f, 0.95f);

                    director.minSeriesSpawnInterval = defaults.MinSeriesSpawnInterval * intervalScale;
                    director.maxSeriesSpawnInterval = defaults.MaxSeriesSpawnInterval * (intervalScale + 0.02f);

                    var eliteBias = defaults.EliteBias + 0.1f * GetForgeNodeCount();
                    if (HasWarning(WarWarning.ReinforcedVanguard))
                    {
                        eliteBias += 0.08f;
                    }

                    if (_operationRoll.Anomaly == WarAnomaly.Blackout)
                    {
                        eliteBias += 0.05f;
                    }

                    switch (_currentDoctrine)
                    {
                        case WarfrontDoctrineProfile.SwarmFront:
                            eliteBias -= 0.04f;
                            break;
                        case WarfrontDoctrineProfile.ArtilleryFront:
                            eliteBias += 0.05f;
                            break;
                        case WarfrontDoctrineProfile.SiegeFront:
                            eliteBias += 0.1f;
                            break;
                        case WarfrontDoctrineProfile.DisruptionFront:
                            eliteBias += 0.02f;
                            break;
                    }

                    eliteBias = Mathf.Max(0f, eliteBias);

                    director.eliteBias = eliteBias;
                }
                else if (recon)
                {
                    var reconMultiplier = 0.8f;
                    if (HasWarning(WarWarning.SupplyLine))
                    {
                        reconMultiplier += 0.05f;
                    }

                    if (_operationRoll.Anomaly == WarAnomaly.CounterIntel)
                    {
                        reconMultiplier -= 0.08f;
                    }

                    switch (_currentDoctrine)
                    {
                        case WarfrontDoctrineProfile.SwarmFront:
                            reconMultiplier += 0.06f;
                            break;
                        case WarfrontDoctrineProfile.ArtilleryFront:
                            reconMultiplier -= 0.05f;
                            break;
                        case WarfrontDoctrineProfile.HunterCell:
                            reconMultiplier += 0.04f;
                            break;
                        case WarfrontDoctrineProfile.DisruptionFront:
                            reconMultiplier += 0.03f;
                            break;
                    }

                    reconMultiplier = Mathf.Clamp(reconMultiplier, 0.55f, 1.05f);

                    director.creditMultiplier = defaults.CreditMultiplier * reconMultiplier;
                    director.minSeriesSpawnInterval = defaults.MinSeriesSpawnInterval * 1.2f;
                    director.maxSeriesSpawnInterval = defaults.MaxSeriesSpawnInterval * 1.25f;
                    director.eliteBias = defaults.EliteBias;
                }
                else
                {
                    var breatherMultiplier = _mercyTimer > 0f ? 0.45f : 0.55f;
                    if (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
                    {
                        breatherMultiplier += 0.04f;
                    }
                    else if (_currentDoctrine == WarfrontDoctrineProfile.SwarmFront)
                    {
                        breatherMultiplier -= 0.03f;
                    }

                    director.creditMultiplier = defaults.CreditMultiplier * breatherMultiplier;
                    director.minSeriesSpawnInterval = defaults.MinSeriesSpawnInterval * 1.35f;
                    director.maxSeriesSpawnInterval = defaults.MaxSeriesSpawnInterval * 1.4f;
                    director.eliteBias = defaults.EliteBias;
                }
            }

            for (var i = 0; i < _staleDirectorKeys.Count; i++)
            {
                _directorDefaults.Remove(_staleDirectorKeys[i]);
            }
        }

        private void SetDirectorCadenceWavePause()
        {
            _staleDirectorKeys.Clear();
            foreach (var pair in _directorDefaults)
            {
                var director = pair.Key;
                if (!director)
                {
                    _staleDirectorKeys.Add(pair.Key);
                    continue;
                }

                var defaults = pair.Value;
                director.creditMultiplier = defaults.CreditMultiplier * 0.03f;
                director.minSeriesSpawnInterval = Mathf.Max(defaults.MinSeriesSpawnInterval * 2.8f, 14f);
                director.maxSeriesSpawnInterval = Mathf.Max(defaults.MaxSeriesSpawnInterval * 3.2f, 18f);
                director.eliteBias = defaults.EliteBias;
            }

            for (var i = 0; i < _staleDirectorKeys.Count; i++)
            {
                _directorDefaults.Remove(_staleDirectorKeys[i]);
            }
        }

        private void RestoreDirectors()
        {
            foreach (var pair in _directorDefaults)
            {
                if (!pair.Key)
                {
                    continue;
                }

                pair.Key.creditMultiplier = pair.Value.CreditMultiplier;
                pair.Key.minSeriesSpawnInterval = pair.Value.MinSeriesSpawnInterval;
                pair.Key.maxSeriesSpawnInterval = pair.Value.MaxSeriesSpawnInterval;
                pair.Key.eliteBias = pair.Value.EliteBias;
            }

            _directorDefaults.Clear();
        }

        private void CleanupNodes()
        {
            foreach (var node in _activeNodes)
            {
                if (!node)
                {
                    continue;
                }

                var identity = node.GetComponent<NetworkIdentity>();
                if (identity && NetworkServer.active)
                {
                    NetworkServer.Destroy(node.gameObject);
                }
                else
                {
                    Destroy(node.gameObject);
                }
            }

            _activeNodes.Clear();
        }

        private void CleanupInvalidNodeReferences()
        {
            _activeNodes.RemoveAll(node => !node || !node.IsActive);
        }

        private void ResolveTeleporterReference()
        {
            if (_teleporter == null)
            {
                _teleporter = TeleporterInteraction.instance;
            }

            if (_holdoutZone == null && _teleporter)
            {
                _holdoutZone = _teleporter.holdoutZoneController;
                if (_holdoutZone == null)
                {
                    _holdoutZone = _teleporter.GetComponent<HoldoutZoneController>();
                }
            }
        }

        private void BuildHudSnapshot()
        {
            if (!NetworkServer.active && _hasRemoteSnapshot)
            {
                _latestHudSnapshot = _remoteHudSnapshot;
                return;
            }

            var siegeTier = GetChargeTier();
            var snapshot = new WarfrontHudSnapshot
            {
                Active = WarfrontDirectorPlugin.Enabled.Value && _stageActive,
                DominantRole = _dominantRole,
                Doctrine = _currentDoctrine,
                Intensity = _intensity,
                ContestDelta = siegeTier,
                ChargeFraction = _teleporter ? _teleporter.chargeFraction : 0f,
                AssaultActive = _assaultActive,
                BreachActive = _breachActive,
                MercyActive = _mercyTimer > 0f,
                LoneWolfPressure = Mathf.Clamp01(_loneWolfPressure),
                WindowTimeRemaining = Mathf.Max(0f, _windowTimer),
                OperationSummary = _operationSummary,
                ActiveCommanders = GetActiveCommanderCount(),
                CommanderTypeMask = BuildCommanderTypeMask(),
                ContestColor = GetSiegeTierColor(siegeTier)
            };

            snapshot.Phase = ResolvePhase(snapshot.Intensity);
            _latestHudSnapshot = snapshot;
        }

        private void TickNetworkSync(float deltaTime)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            _networkSyncTimer -= deltaTime;
            if (_networkSyncTimer > 0f)
            {
                return;
            }

            _networkSyncTimer = 1.0f;

            var message = new WarfrontStateMessage
            {
                Active = WarfrontDirectorPlugin.Enabled.Value && _stageActive,
                Phase = (byte)ResolvePhase(_intensity),
                DominantRole = (byte)_dominantRole,
                Doctrine = (byte)_currentDoctrine,
                Intensity = _intensity,
                ContestDelta = GetChargeTier(),
                ChargeFraction = _teleporter ? _teleporter.chargeFraction : 0f,
                AssaultActive = _assaultActive,
                BreachActive = _breachActive,
                MercyActive = _mercyTimer > 0f,
                LoneWolfPressure = (byte)Mathf.Clamp(Mathf.RoundToInt(_loneWolfPressure * byte.MaxValue), 0, byte.MaxValue),
                WindowTimeRemaining = Mathf.Max(0f, _windowTimer),
                ActiveCommanderCount = (byte)Mathf.Clamp(GetActiveCommanderCount(), 0, byte.MaxValue),
                CommanderTypeMask = BuildCommanderTypeMask(),
                WarningOne = (byte)_operationRoll.WarningOne,
                WarningTwo = (byte)_operationRoll.WarningTwo,
                Anomaly = (byte)_operationRoll.Anomaly
            };

            NetworkServer.SendToAll(WarfrontNetworkSync.MessageType, message);
        }

        internal static void ApplyRemoteSnapshot(WarfrontStateMessage message)
        {
            if (message == null)
            {
                return;
            }

            _hasRemoteSnapshot = true;
            _remoteHudSnapshot = new WarfrontHudSnapshot
            {
                Active = message.Active,
                Phase = (WarfrontPhase)Mathf.Clamp(message.Phase, 0, (byte)WarfrontPhase.Breach),
                DominantRole = (WarfrontRole)Mathf.Clamp(message.DominantRole, 0, (byte)WarfrontRole.Anchor),
                Doctrine = (WarfrontDoctrineProfile)Mathf.Clamp(message.Doctrine, 0, (byte)WarfrontDoctrineProfile.DisruptionFront),
                Intensity = message.Intensity,
                ContestDelta = message.ContestDelta,
                ChargeFraction = message.ChargeFraction,
                AssaultActive = message.AssaultActive,
                BreachActive = message.BreachActive,
                MercyActive = message.MercyActive,
                LoneWolfPressure = Mathf.Clamp01(message.LoneWolfPressure / (float)byte.MaxValue),
                WindowTimeRemaining = message.WindowTimeRemaining,
                ActiveCommanders = message.ActiveCommanderCount,
                CommanderTypeMask = message.CommanderTypeMask,
                OperationSummary = $"{ToDisplayName((WarfrontDoctrineProfile)Mathf.Clamp(message.Doctrine, 0, (byte)WarfrontDoctrineProfile.DisruptionFront))} | {ToDisplayName((WarWarning)message.WarningOne)}, {ToDisplayName((WarWarning)message.WarningTwo)} - {ToDisplayName((WarAnomaly)message.Anomaly)}",
                ContestColor = GetSiegeTierColor(Mathf.RoundToInt(message.ContestDelta))
            };
        }

        private static Color GetSiegeTierColor(int tier)
        {
            return tier switch
            {
                0 => new Color(0.36f, 0.85f, 0.46f),
                1 => new Color(0.95f, 0.88f, 0.30f),
                2 => new Color(0.95f, 0.55f, 0.20f),
                _ => new Color(0.95f, 0.25f, 0.20f)
            };
        }

        private WarfrontPhase ResolvePhase(float intensity)
        {
            if (_breachActive)
            {
                return WarfrontPhase.Breach;
            }

            if (_assaultActive)
            {
                if (intensity >= 80f)
                {
                    return WarfrontPhase.Overwhelm;
                }

                return WarfrontPhase.Assault;
            }

            if (_teleporterEventActive)
            {
                return WarfrontPhase.Cooldown;
            }

            if (intensity >= 30f)
            {
                return WarfrontPhase.Skirmish;
            }

            return WarfrontPhase.Recon;
        }

        private void ResetRunState(bool clearNodes, bool restoreDirectors)
        {
            _stageActive = false;
            _teleporterEventActive = false;
            _assaultActive = false;
            _breachActive = false;
            _falseLullPending = false;
            _silentMinuteFinished = false;
            _bossGateActive = false;
            _siegeTier3Announced = false;
            _originalHoldoutRadius = 0f;

            _windowTimer = 0f;
            _assaultPulseTimer = 0f;
            _reconPulseTimer = 0f;
            _intensity = 0f;
            _recentPlayerDamage = 0f;
            _medicPulseTimer = 4f;
            _silentMinuteTimer = 0f;
            _silentMinuteBufferedPulse = 0f;
            _networkSyncTimer = 0f;
            _loneWolfPressure = 0f;
            _mercyTimer = 0f;
            _mercyCooldownTimer = 0f;
            _dominantRole = WarfrontRole.None;
            _roleRotationCursor = -1;
            _nodeTypeCursor = 0;
            _stageCommanderQuota = 0;
            _pendingTeleporterCommanderSpawns = 0;
            _eventBuffId = 0;
            _lastAlivePlayerCount = 0;
            _stageDamageSignal = 0f;
            _stageContestSignal = 0f;
            _stageLoneWolfSignal = 0f;
            _stageBreachCount = 0;
            _stageSignalsCommitted = false;
            _eventBuffTimer = 0f;
            _eventBuffDuration = 0f;
            _eventBuffMagnitude = 0f;
            _hunterSquadTargetTimer = 0f;
            _bossChallengeScanTimer = 0f;
            _postBossKillRespiteTimer = 0f;
            _postBossKillRespiteCycleTimer = 0f;
            _pausedTeleporterCharge = 0f;
            _teleporterFogTickTimer = 0f;
            _hunterSquadTarget = null;
            _teleporterChargePaused = false;
            _teleporterFogActive = false;
            _teleporterBossObservedAlive = false;
            _postBossKillRespitesEnabled = false;
            _postBossWaveActive = false;
            _teleporterFogExposureByMasterId.Clear();
            _teleporterFogSeenPlayerMasterIds.Clear();

            _teleporter = null;
            _holdoutZone = null;
            _currentDoctrine = WarfrontDoctrineProfile.Balanced;
            _activeWarnings.Clear();
            _operationSummary = string.Empty;
            _hasRemoteSnapshot = false;
            _remoteHudSnapshot = default;
            _lastBroadcastMessage = string.Empty;
            _lastBroadcastTime = -100f;
            _curatedCommanderElites.Clear();
            _bossChallengeBuffedMasters.Clear();
            _bossRoleControllerAttached.Clear();
            _bossPhaseReached.Clear();
            _bossEnraged.Clear();
            _stageRoleSignals.Clear();
            foreach (var role in RoleRotation)
            {
                _stageRoleSignals[role] = 0f;
            }

            if (clearNodes)
            {
                CleanupNodes();
            }

            if (restoreDirectors)
            {
                RestoreDirectors();
            }
        }

        private WarfrontOperationRoll RollOperations(WarfrontDoctrineProfile doctrine)
        {
            var warningOne = RollWarning(doctrine, null);
            var warningTwo = RollWarning(doctrine, warningOne);
            var anomaly = RollAnomaly(doctrine);
            return new WarfrontOperationRoll(warningOne, warningTwo, anomaly);
        }

        private WarWarning RollWarning(WarfrontDoctrineProfile doctrine, WarWarning? exclude)
        {
            var weights = new float[WarningPool.Length];
            var totalWeight = 0f;

            for (var i = 0; i < WarningPool.Length; i++)
            {
                var warning = WarningPool[i];
                if (exclude.HasValue && warning == exclude.Value)
                {
                    weights[i] = 0f;
                    continue;
                }

                var weight = 1f + GetDoctrineWarningWeight(doctrine, warning) + GetAdaptedWarningWeight(warning);
                weights[i] = Mathf.Clamp(weight, 0.35f, 3f);
                totalWeight += weights[i];
            }

            if (totalWeight <= 0f)
            {
                return WarningPool[UnityEngine.Random.Range(0, WarningPool.Length)];
            }

            var roll = UnityEngine.Random.Range(0f, totalWeight);
            for (var i = 0; i < WarningPool.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                {
                    return WarningPool[i];
                }
            }

            for (var j = 0; j < WarningPool.Length; j++)
            {
                if (!exclude.HasValue || WarningPool[j] != exclude.Value)
                {
                    return WarningPool[j];
                }
            }

            return WarningPool[0];
        }

        private WarAnomaly RollAnomaly(WarfrontDoctrineProfile doctrine)
        {
            var weights = new float[AnomalyPool.Length];
            var totalWeight = 0f;

            for (var i = 0; i < AnomalyPool.Length; i++)
            {
                var anomaly = AnomalyPool[i];
                var weight = 1f + GetDoctrineAnomalyWeight(doctrine, anomaly) + GetAdaptedAnomalyWeight(anomaly);
                weights[i] = Mathf.Clamp(weight, 0.35f, 2.8f);
                totalWeight += weights[i];
            }

            if (totalWeight <= 0f)
            {
                return AnomalyPool[UnityEngine.Random.Range(0, AnomalyPool.Length)];
            }

            var roll = UnityEngine.Random.Range(0f, totalWeight);
            for (var i = 0; i < AnomalyPool.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                {
                    return AnomalyPool[i];
                }
            }

            return AnomalyPool[0];
        }

        private WarfrontDoctrineProfile SelectDoctrineProfile(int stageIndex)
        {
            if (stageIndex <= 0)
            {
                _lastDoctrine = WarfrontDoctrineProfile.Balanced;
                _doctrineStreakCount = 1;
                return WarfrontDoctrineProfile.Balanced;
            }

            var weights = new float[DoctrinePool.Length];
            var totalWeight = 0f;

            for (var i = 0; i < DoctrinePool.Length; i++)
            {
                var doctrine = DoctrinePool[i];
                var weight = GetDoctrineSelectionWeight(doctrine);

                if (doctrine == _lastDoctrine && _doctrineStreakCount > 0)
                {
                    weight *= _doctrineStreakCount >= 2 ? 0.4f : 0.62f;
                }

                weights[i] = Mathf.Clamp(weight, 0.25f, 3.2f);
                totalWeight += weights[i];
            }

            if (totalWeight <= 0f)
            {
                return WarfrontDoctrineProfile.Balanced;
            }

            var roll = UnityEngine.Random.Range(0f, totalWeight);
            var selected = WarfrontDoctrineProfile.Balanced;
            for (var i = 0; i < DoctrinePool.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                {
                    selected = DoctrinePool[i];
                    break;
                }
            }

            if (selected == _lastDoctrine)
            {
                _doctrineStreakCount = Mathf.Min(3, _doctrineStreakCount + 1);
            }
            else
            {
                _lastDoctrine = selected;
                _doctrineStreakCount = 1;
            }

            return selected;
        }

        private float GetDoctrineSelectionWeight(WarfrontDoctrineProfile doctrine)
        {
            return doctrine switch
            {
                WarfrontDoctrineProfile.Balanced => 1.45f,
                WarfrontDoctrineProfile.SwarmFront => 0.75f + _runContestSignal * 0.95f + GetRoleThreatSignal(WarfrontRole.Contester) * 0.5f + GetRoleThreatSignal(WarfrontRole.Peeler) * 0.28f,
                WarfrontDoctrineProfile.ArtilleryFront => 0.72f + _runDamageSignal * 0.82f + GetRoleThreatSignal(WarfrontRole.Artillery) * 0.7f,
                WarfrontDoctrineProfile.HunterCell => 0.68f + _runLoneWolfSignal * 1.05f + GetRoleThreatSignal(WarfrontRole.Hunter) * 0.7f,
                WarfrontDoctrineProfile.SiegeFront => 0.65f + _runBreachSignal * 1.1f + GetRoleThreatSignal(WarfrontRole.Anchor) * 0.55f + _runContestSignal * 0.2f,
                WarfrontDoctrineProfile.DisruptionFront => 0.63f + _runDamageSignal * 0.3f + _runLoneWolfSignal * 0.3f + GetRoleThreatSignal(WarfrontRole.Peeler) * 0.6f,
                _ => 1f
            };
        }

        private float GetDoctrineWarningWeight(WarfrontDoctrineProfile doctrine, WarWarning warning)
        {
            return doctrine switch
            {
                WarfrontDoctrineProfile.SwarmFront when warning == WarWarning.PackTactics => 0.85f,
                WarfrontDoctrineProfile.SwarmFront when warning == WarWarning.PhalanxDoctrine => 0.55f,
                WarfrontDoctrineProfile.SwarmFront when warning == WarWarning.SupplyLine => 0.35f,
                WarfrontDoctrineProfile.ArtilleryFront when warning == WarWarning.ArtilleryDoctrine => 0.95f,
                WarfrontDoctrineProfile.ArtilleryFront when warning == WarWarning.SiegeEngine => 0.55f,
                WarfrontDoctrineProfile.ArtilleryFront when warning == WarWarning.ReinforcedVanguard => 0.3f,
                WarfrontDoctrineProfile.HunterCell when warning == WarWarning.HunterKiller => 0.95f,
                WarfrontDoctrineProfile.HunterCell when warning == WarWarning.ExecutionOrder => 0.75f,
                WarfrontDoctrineProfile.HunterCell when warning == WarWarning.SignalJamming => 0.35f,
                WarfrontDoctrineProfile.SiegeFront when warning == WarWarning.SiegeEngine => 1f,
                WarfrontDoctrineProfile.SiegeFront when warning == WarWarning.ReinforcedVanguard => 0.75f,
                WarfrontDoctrineProfile.SiegeFront when warning == WarWarning.Attrition => 0.3f,
                WarfrontDoctrineProfile.DisruptionFront when warning == WarWarning.SignalJamming => 0.95f,
                WarfrontDoctrineProfile.DisruptionFront when warning == WarWarning.Sappers => 0.55f,
                WarfrontDoctrineProfile.DisruptionFront when warning == WarWarning.MedicNet => 0.25f,
                _ => 0f
            };
        }

        private float GetDoctrineAnomalyWeight(WarfrontDoctrineProfile doctrine, WarAnomaly anomaly)
        {
            return doctrine switch
            {
                WarfrontDoctrineProfile.SwarmFront when anomaly == WarAnomaly.WarDrums => 0.65f,
                WarfrontDoctrineProfile.SwarmFront when anomaly == WarAnomaly.BlitzOrder => 0.5f,
                WarfrontDoctrineProfile.ArtilleryFront when anomaly == WarAnomaly.IronRain => 0.8f,
                WarfrontDoctrineProfile.ArtilleryFront when anomaly == WarAnomaly.Blackout => 0.35f,
                WarfrontDoctrineProfile.HunterCell when anomaly == WarAnomaly.CounterIntel => 0.75f,
                WarfrontDoctrineProfile.HunterCell when anomaly == WarAnomaly.CommandConfusion => 0.35f,
                WarfrontDoctrineProfile.SiegeFront when anomaly == WarAnomaly.IronRain => 0.65f,
                WarfrontDoctrineProfile.SiegeFront when anomaly == WarAnomaly.FalseLull => 0.3f,
                WarfrontDoctrineProfile.DisruptionFront when anomaly == WarAnomaly.CommandConfusion => 0.7f,
                WarfrontDoctrineProfile.DisruptionFront when anomaly == WarAnomaly.Blackout => 0.45f,
                _ => 0f
            };
        }

        private float GetAdaptedWarningWeight(WarWarning warning)
        {
            return warning switch
            {
                WarWarning.Sappers => _runContestSignal * 0.12f + _runLoneWolfSignal * 0.18f,
                WarWarning.PhalanxDoctrine => _runContestSignal * 0.35f + GetRoleThreatSignal(WarfrontRole.Contester) * 0.28f,
                WarWarning.ArtilleryDoctrine => _runDamageSignal * 0.18f + GetRoleThreatSignal(WarfrontRole.Artillery) * 0.45f,
                WarWarning.HunterKiller => _runLoneWolfSignal * 0.42f + GetRoleThreatSignal(WarfrontRole.Hunter) * 0.32f,
                WarWarning.MedicNet => _runDamageSignal * 0.15f,
                WarWarning.Attrition => _runDamageSignal * 0.3f + _runContestSignal * 0.15f,
                WarWarning.SiegeEngine => _runBreachSignal * 0.5f + GetRoleThreatSignal(WarfrontRole.Anchor) * 0.28f,
                WarWarning.PackTactics => _runContestSignal * 0.25f,
                WarWarning.SignalJamming => _runLoneWolfSignal * 0.22f + GetRoleThreatSignal(WarfrontRole.Peeler) * 0.34f,
                WarWarning.ReinforcedVanguard => _runBreachSignal * 0.35f,
                WarWarning.ExecutionOrder => _runLoneWolfSignal * 0.33f + GetRoleThreatSignal(WarfrontRole.Hunter) * 0.25f,
                WarWarning.SupplyLine => _runContestSignal * 0.18f + _runDamageSignal * 0.08f,
                _ => 0f
            };
        }

        private float GetAdaptedAnomalyWeight(WarAnomaly anomaly)
        {
            return anomaly switch
            {
                WarAnomaly.SilentMinute => _runDamageSignal * 0.18f,
                WarAnomaly.WarDrums => _runContestSignal * 0.24f + GetRoleThreatSignal(WarfrontRole.Contester) * 0.18f,
                WarAnomaly.FalseLull => _runBreachSignal * 0.16f + _runDamageSignal * 0.12f,
                WarAnomaly.CommandConfusion => GetRoleThreatSignal(WarfrontRole.Peeler) * 0.26f,
                WarAnomaly.Blackout => _runDamageSignal * 0.22f,
                WarAnomaly.CounterIntel => _runLoneWolfSignal * 0.28f,
                WarAnomaly.BlitzOrder => _runContestSignal * 0.3f + GetRoleThreatSignal(WarfrontRole.Hunter) * 0.15f,
                WarAnomaly.IronRain => _runBreachSignal * 0.36f + GetRoleThreatSignal(WarfrontRole.Artillery) * 0.2f,
                _ => 0f
            };
        }

        private static float DampAndBlendSignal(float current, float incoming, float blend, float decay)
        {
            var damped = Mathf.Max(0f, current - decay);
            return Mathf.Clamp01(Mathf.Lerp(damped, Mathf.Clamp01(incoming), Mathf.Clamp01(blend)));
        }

        private void CommitStageAdaptationSignals()
        {
            if (!_stageActive || _stageSignalsCommitted)
            {
                return;
            }

            _stageSignalsCommitted = true;

            var difficultyTier = Mathf.Max(1, GetDifficultyTier() + 1);
            var stageMinutes = Mathf.Max(0.75f, _stageStopwatch / 60f);

            var damageNormalized = Mathf.Clamp01(_stageDamageSignal / (220f * difficultyTier * stageMinutes));
            var contestNormalized = Mathf.Clamp01(_stageContestSignal / (10f * stageMinutes));
            var loneWolfNormalized = Mathf.Clamp01(_stageLoneWolfSignal / (12f * stageMinutes));
            var breachNormalized = Mathf.Clamp01(_stageBreachCount / 3f);

            _runDamageSignal = DampAndBlendSignal(_runDamageSignal, damageNormalized, 0.38f, 0.08f);
            _runContestSignal = DampAndBlendSignal(_runContestSignal, contestNormalized, 0.4f, 0.07f);
            _runLoneWolfSignal = DampAndBlendSignal(_runLoneWolfSignal, loneWolfNormalized, 0.35f, 0.09f);
            _runBreachSignal = DampAndBlendSignal(_runBreachSignal, breachNormalized, 0.42f, 0.06f);

            var roleDenominator = Mathf.Max(1.4f, stageMinutes * 2.4f);
            foreach (var role in RoleRotation)
            {
                var stageRoleSignal = _stageRoleSignals.TryGetValue(role, out var signal) ? signal : 0f;
                var normalized = Mathf.Clamp01(stageRoleSignal / roleDenominator);
                _runRoleThreatSignals[role] = DampAndBlendSignal(GetRoleThreatSignal(role), normalized, 0.34f, 0.08f);
            }

            Log.Info($"Warfront adaptation updated: dmg={_runDamageSignal:0.00}, contest={_runContestSignal:0.00}, lone={_runLoneWolfSignal:0.00}, breach={_runBreachSignal:0.00}");
        }

        private void ResetDoctrineAdaptation()
        {
            _runDamageSignal = 0f;
            _runContestSignal = 0f;
            _runLoneWolfSignal = 0f;
            _runBreachSignal = 0f;

            _stageDamageSignal = 0f;
            _stageContestSignal = 0f;
            _stageLoneWolfSignal = 0f;
            _stageBreachCount = 0;
            _stageSignalsCommitted = false;

            _currentDoctrine = WarfrontDoctrineProfile.Balanced;
            _lastDoctrine = WarfrontDoctrineProfile.Balanced;
            _doctrineStreakCount = 0;

            _stageRoleSignals.Clear();
            _runRoleThreatSignals.Clear();
            foreach (var role in RoleRotation)
            {
                _stageRoleSignals[role] = 0f;
                _runRoleThreatSignals[role] = 0f;
            }
        }

        private void CacheCuratedCommanderElites()
        {
            _curatedCommanderElites.Clear();

            foreach (var eliteDef in EliteCatalog.eliteDefs)
            {
                if (eliteDef == null || eliteDef.eliteEquipmentDef == null)
                {
                    continue;
                }

                var isCurated = CuratedCommanderEliteTokens.Any(t => EliteMatchesToken(eliteDef, t));
                if (!isCurated)
                {
                    continue;
                }

                if (!_curatedCommanderElites.Contains(eliteDef.eliteIndex))
                {
                    _curatedCommanderElites.Add(eliteDef.eliteIndex);
                }
            }

            if (_curatedCommanderElites.Count == 0)
            {
                foreach (var eliteDef in EliteCatalog.eliteDefs)
                {
                    if (eliteDef == null || eliteDef.eliteEquipmentDef == null || eliteDef.eliteIndex == EliteIndex.None)
                    {
                        continue;
                    }

                    if (!_curatedCommanderElites.Contains(eliteDef.eliteIndex))
                    {
                        _curatedCommanderElites.Add(eliteDef.eliteIndex);
                    }
                }
            }

            if (_curatedCommanderElites.Count == 0)
            {
                _curatedCommanderElites.Add(EliteIndex.None);
            }
        }

        private EliteIndex PickCuratedCommanderElite(WarfrontNodeType nodeType)
        {
            if (_curatedCommanderElites.Count == 0)
            {
                CacheCuratedCommanderElites();
            }

            if (_curatedCommanderElites.Count == 0)
            {
                return EliteIndex.None;
            }

            var index = Mathf.Abs(_nodeTypeCursor + (int)nodeType) % _curatedCommanderElites.Count;
            return _curatedCommanderElites[index];
        }

        private void ApplyCommanderEliteAffix(CharacterMaster master, WarfrontNodeType nodeType)
        {
            if (!master)
            {
                return;
            }

            var body = master.GetBody();
            ApplyDoctrineBuffPackage(body, _currentDoctrine);
            ApplyRoleBuffPackage(body, ResolveCommanderRole(nodeType));
            if (body != null)
            {
                body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 14f + GetDifficultyTier() * 2f);
            }

            if (master.inventory == null)
            {
                return;
            }

            var diffTier = GetDifficultyTier();
            var stageCount = Run.instance != null ? Run.instance.stageClearCount : 0;
            var hpStacks = 30 + diffTier * 8 + stageCount * 4;
            var dmgStacks = 10 + diffTier * 3 + stageCount * 2;
            master.inventory.GiveItem(RoR2Content.Items.BoostHp, hpStacks);
            master.inventory.GiveItem(RoR2Content.Items.BoostDamage, dmgStacks);
            master.inventory.GiveItem(RoR2Content.Items.AdaptiveArmor, 1);

            var eliteIndex = PickCuratedCommanderElite(nodeType);
            if (eliteIndex == EliteIndex.None)
            {
                return;
            }

            var eliteDef = EliteCatalog.GetEliteDef(eliteIndex);
            if (eliteDef == null || eliteDef.eliteEquipmentDef == null)
            {
                return;
            }

            master.inventory.SetEquipmentIndex(eliteDef.eliteEquipmentDef.equipmentIndex);
        }

        private bool IsSpawnRestWindowActive()
        {
            return _teleporterEventActive && _postBossKillRespitesEnabled && !_postBossWaveActive && _postBossKillRespiteTimer > 0f;
        }

        private void TickPostBossRespite(float deltaTime)
        {
            if (!_postBossKillRespitesEnabled || !_teleporterEventActive || _teleporter == null || !_teleporter.isCharging || _teleporter.isCharged)
            {
                _postBossKillRespiteTimer = 0f;
                _postBossKillRespiteCycleTimer = 0f;
                _postBossWaveActive = false;
                return;
            }

            if (_postBossWaveActive)
            {
                _postBossKillRespiteCycleTimer = Mathf.Max(0f, _postBossKillRespiteCycleTimer - deltaTime);
                _assaultPulseTimer -= deltaTime;
                if (_assaultPulseTimer <= 0f)
                {
                    SpawnAssaultPulse();
                    _assaultPulseTimer = Mathf.Max(1f, GetAssaultPulseInterval() * 0.72f);
                }

                if (_postBossKillRespiteCycleTimer <= 0f)
                {
                    BeginPostBossKillRespite(initial: false);
                }

                return;
            }

            _postBossKillRespiteTimer = Mathf.Max(0f, _postBossKillRespiteTimer - deltaTime);
            if (_postBossKillRespiteTimer <= 0f)
            {
                BeginPostBossKillWave(initial: false);
            }
        }

        private void BeginPostBossKillRespite(bool initial)
        {
            var restMin = initial ? 4.5f : 3.5f;
            var restMax = initial ? 7f : 6f;

            var teamHealth = GetTeamAverageHealthFraction();
            if (teamHealth > 0.75f)
            {
                restMin -= 1f;
                restMax -= 1.5f;
            }

            var restDuration = UnityEngine.Random.Range(Mathf.Max(2f, restMin), Mathf.Max(3f, restMax));

            _postBossWaveActive = false;
            _postBossKillRespiteTimer = restDuration;
            _postBossKillRespiteCycleTimer = 0f;
            _assaultActive = false;
            _breachActive = false;
            _windowTimer = Mathf.Max(_windowTimer, restDuration);
            SetDirectorCadenceWavePause();

            if (initial)
            {
                BroadcastWarfrontMessage("Boss down. Enemy waves incoming with brief lulls between pushes.", 2.5f);
            }
        }

        private void BeginPostBossKillWave(bool initial)
        {
            var waveMin = initial ? 11f : 8f;
            var waveMax = initial ? 16f : 13f;

            if (HasWarning(WarWarning.Attrition))
            {
                waveMin += 2f;
                waveMax += 2f;
            }

            if (_currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
            {
                waveMin += 1f;
                waveMax += 1.5f;
            }

            var waveDuration = UnityEngine.Random.Range(waveMin, waveMax);
            var openingPulseCount = initial ? 2 : 1;
            openingPulseCount += Mathf.Clamp(GetDifficultyTier() - 1, 0, 1);

            if (UnityEngine.Random.value < 0.25f)
            {
                openingPulseCount += 1;
            }

            _postBossWaveActive = true;
            _postBossKillRespiteTimer = 0f;
            _postBossKillRespiteCycleTimer = waveDuration;
            _assaultActive = true;
            _breachActive = false;
            _windowTimer = waveDuration;
            _assaultPulseTimer = Mathf.Max(0.75f, GetAssaultPulseInterval() * 0.6f);
            _staggerPhase = 0;
            _staggerDelayTimer = 0f;
            _dominantRole = ResolveDominantRole(forceRotate: true);

            SetDirectorCadence(assault: true, recon: false);
            TriggerEventBuffWindow(initial ? 8f : 6f, 0.16f + UnityEngine.Random.Range(0f, 0.08f));

            for (var i = 0; i < openingPulseCount; i++)
            {
                SpawnAssaultPulse();
            }

            if (initial)
            {
                BroadcastWarfrontMessage("Boss down. Enemy retaliates in waves with short pauses between pushes.", 2.5f);
            }
        }

        private void TickBossChallengeBuffs(float deltaTime)
        {
            if (!_teleporterEventActive)
            {
                _bossChallengeScanTimer = 0f;
                return;
            }

            _bossChallengeScanTimer -= deltaTime;
            if (_bossChallengeScanTimer > 0f)
            {
                return;
            }

            _bossChallengeScanTimer = 0.35f;
            var aliveBossCount = 0;
            aliveBossCount += TryApplyBossChallengeAffixForTeam(TeamIndex.Monster);
            aliveBossCount += TryApplyBossChallengeAffixForTeam(TeamIndex.Void);
            aliveBossCount += TryApplyBossChallengeAffixForTeam(TeamIndex.Lunar);

            if (aliveBossCount > 0)
            {
                _teleporterBossObservedAlive = true;
                TickBossPhases();
                return;
            }

            if (_teleporterBossObservedAlive && !_postBossKillRespitesEnabled)
            {
                _postBossKillRespitesEnabled = true;

                if (_bossGateActive)
                {
                    _bossGateActive = false;
                    ReleaseTeleporterChargePause();
                    BroadcastWarfrontMessage("Boss eliminated. Charge unlocked.");
                }

                BeginPostBossKillWave(initial: true);
            }
        }

        private void TickBossPhases()
        {
            var teams = EnemyTeams;
            foreach (var teamIndex in teams)
            {
                var members = TeamComponent.GetTeamMembers(teamIndex);
                foreach (var member in members)
                {
                    var body = member ? member.body : null;
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive || !body.isBoss)
                    {
                        continue;
                    }

                    var master = body.master;
                    if (master == null)
                    {
                        continue;
                    }

                    var masterId = master.GetInstanceID();
                    var healthFraction = body.healthComponent.combinedHealthFraction;

                    if (!_bossPhaseReached.TryGetValue(masterId, out var phase))
                    {
                        phase = 0;
                        _bossPhaseReached[masterId] = 0;
                    }

                    if (phase < 1 && healthFraction <= 0.75f)
                    {
                        _bossPhaseReached[masterId] = 1;
                        OnBossPhaseReached(body, 1);
                    }

                    if (phase < 2 && healthFraction <= 0.50f)
                    {
                        _bossPhaseReached[masterId] = 2;
                        OnBossPhaseReached(body, 2);
                    }

                    if (phase < 3 && healthFraction <= 0.25f)
                    {
                        _bossPhaseReached[masterId] = 3;
                        OnBossPhaseReached(body, 3);
                    }

                    if (!_bossEnraged.Contains(masterId) && healthFraction <= 0.40f)
                    {
                        _bossEnraged.Add(masterId);
                        OnBossEnrage(body, master);
                    }

                    RefreshBossPeriodicBuffs(body);
                }
            }
        }

        private void OnBossPhaseReached(CharacterBody bossBody, int phase)
        {
            var objective = GetObjectivePosition();
            var diffTier = GetDifficultyTier();
            var bossPos = bossBody.corePosition;

            switch (phase)
            {
                case 1:
                    SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 2 + diffTier, bossPos, 8f, 16f, WarfrontRole.Contester);
                    BoostNearbyMonsterMovement(objective, 40f, 0.2f, 5f);
                    BroadcastWarfrontMessage("<color=#ffaa00>Boss calls reinforcements!</color>", 3f);
                    break;
                case 2:
                    SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, 1 + Mathf.Clamp(diffTier - 1, 0, 2), bossPos, 6f, 14f, WarfrontRole.Anchor);
                    SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 3 + diffTier, bossPos, 8f, 18f, WarfrontRole.Contester);
                    var isolated = GetMostIsolatedPlayer();
                    if (isolated != null)
                    {
                        SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 2, isolated.corePosition, 10f, 18f, WarfrontRole.Hunter);
                    }
                    TriggerEventBuffWindow(10f, 0.3f);
                    BoostNearbyMonsterMovement(objective, 50f, 0.3f, 6f);
                    BroadcastWarfrontMessage("<color=#ff6600>Boss at half health \u2014 enemy forces surge!</color>", 3f);
                    break;
                case 3:
                    SpawnMonsterPack(WarfrontAssetCatalog.HeavyMasterPrefabs, 2 + Mathf.Clamp(diffTier, 0, 2), bossPos, 6f, 14f, WarfrontRole.Anchor);
                    SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 4 + diffTier, objective, 6f, 16f, WarfrontRole.Contester);
                    SpawnMonsterPack(WarfrontAssetCatalog.AssaultMasterPrefabs, 2, objective, 12f, 24f, WarfrontRole.Flanker);
                    TriggerEventBuffWindow(12f, 0.35f);
                    BoostNearbyMonsterMovement(objective, 60f, 0.4f, 8f);
                    BroadcastWarfrontMessage("<color=#ff3333>Boss desperate \u2014 all enemy reserves committed!</color>", 3f);
                    break;
            }
        }

        private void OnBossEnrage(CharacterBody body, CharacterMaster master)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, 45f);
            body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, 45f);
            body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 45f);
            body.AddTimedBuff(RoR2Content.Buffs.CloakSpeed, 30f);
            body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 45f);

            if (master != null && master.inventory != null)
            {
                var diffTier = GetDifficultyTier();
                master.inventory.GiveItem(RoR2Content.Items.BoostDamage, 5 + diffTier * 2);
                master.inventory.GiveItem(RoR2Content.Items.BoostHp, 10 + diffTier * 3);
            }

            body.healthComponent.HealFraction(0.10f, default);
            BroadcastWarfrontMessage("<color=#ff0000>BOSS ENRAGED!</color> Increased damage and speed.", 3f);
        }

        private static void RefreshBossPeriodicBuffs(CharacterBody body)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            if (!body.HasBuff(RoR2Content.Buffs.Warbanner))
            {
                body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 8f);
            }

            if (!body.HasBuff(RoR2Content.Buffs.ArmorBoost))
            {
                body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 6f);
            }
        }

        private int TryApplyBossChallengeAffixForTeam(TeamIndex teamIndex)
        {
            var aliveBossCount = 0;
            var members = TeamComponent.GetTeamMembers(teamIndex);
            foreach (var member in members)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive || !body.isBoss)
                {
                    continue;
                }

                aliveBossCount++;
                TryApplyBossChallengeAffix(body.master);
            }

            return aliveBossCount;
        }

        private bool TryApplyBossChallengeAffix(CharacterMaster master)
        {
            if (!IsBossMaster(master))
            {
                return false;
            }

            var masterId = master.GetInstanceID();
            if (_bossChallengeBuffedMasters.Contains(masterId))
            {
                EnsureBossRoleController(master);
                return true;
            }

            if (master.inventory == null)
            {
                EnsureBossRoleController(master);
                return true;
            }

            var diffTier = GetDifficultyTier();
            var stageCount = Run.instance != null ? Run.instance.stageClearCount : 0;
            var alivePlayers = Mathf.Max(1, GetAlivePlayerCount());

            var eliteIndex = PickBossChallengeElite(out var challengeLabel);
            var eliteDef = eliteIndex != EliteIndex.None ? EliteCatalog.GetEliteDef(eliteIndex) : null;
            if (eliteDef?.eliteEquipmentDef != null)
            {
                master.inventory.SetEquipmentIndex(eliteDef.eliteEquipmentDef.equipmentIndex);
            }

            var hpStacks = 40 + diffTier * 12 + stageCount * 6 + alivePlayers * 5;
            var dmgStacks = 8 + diffTier * 4 + stageCount * 2 + alivePlayers * 2;
            master.inventory.GiveItem(RoR2Content.Items.BoostHp, hpStacks);
            master.inventory.GiveItem(RoR2Content.Items.BoostDamage, dmgStacks);
            master.inventory.GiveItem(RoR2Content.Items.AdaptiveArmor, 1);
            master.inventory.GiveItem(RoR2Content.Items.Knurl, 3 + diffTier);

            var body = master.GetBody();
            if (body != null && body.healthComponent != null && body.healthComponent.alive)
            {
                body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 20f + diffTier * 3f);
                body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 15f + diffTier * 2f);
                body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, 12f + diffTier * 2f);
                body.healthComponent.HealFraction(0.15f, default);
            }

            _bossChallengeBuffedMasters.Add(masterId);
            _bossPhaseReached[masterId] = 0;
            EnsureBossRoleController(master);
            BroadcastWarfrontMessage($"Enemy command primes boss with {challengeLabel} doctrine.", 2.5f);
            return true;
        }

        private void EnsureBossRoleController(CharacterMaster master)
        {
            if (master == null)
            {
                return;
            }

            var masterId = master.GetInstanceID();
            if (_bossRoleControllerAttached.Contains(masterId))
            {
                return;
            }

            _bossRoleControllerAttached.Add(masterId);

            var controller = master.GetComponent<WarfrontRoleController>();
            if (!controller)
            {
                controller = master.gameObject.AddComponent<WarfrontRoleController>();
            }

            controller.Initialize(this, master, WarfrontRole.Anchor, _currentDoctrine, isCommander: false, isBoss: true);
        }

        private EliteIndex PickBossChallengeElite(out string challengeLabel)
        {
            if (HasWarning(WarWarning.HunterKiller) || _currentDoctrine == WarfrontDoctrineProfile.HunterCell || _loneWolfPressure > 0.35f)
            {
                challengeLabel = "Overloading";
                return FindCuratedEliteByToken("OVERLOADING");
            }

            if (HasWarning(WarWarning.PhalanxDoctrine) || _currentDoctrine == WarfrontDoctrineProfile.SiegeFront)
            {
                challengeLabel = "Glacial";
                return FindCuratedEliteByToken("GLACIAL");
            }

            if (HasWarning(WarWarning.MedicNet) || _intensity > 62f || _recentPlayerDamage > 70f)
            {
                challengeLabel = "Mending";
                return FindCuratedEliteByToken("MENDING");
            }

            challengeLabel = "Blazing";
            return FindCuratedEliteByToken("BLAZING");
        }

        private EliteIndex FindCuratedEliteByToken(string token)
        {
            if (_curatedCommanderElites.Count == 0)
            {
                CacheCuratedCommanderElites();
            }

            foreach (var eliteIndex in _curatedCommanderElites)
            {
                var eliteDef = EliteCatalog.GetEliteDef(eliteIndex);
                if (EliteMatchesToken(eliteDef, token))
                {
                    return eliteIndex;
                }
            }

            return _curatedCommanderElites.Count > 0 ? _curatedCommanderElites[0] : EliteIndex.None;
        }

        private static bool EliteMatchesToken(EliteDef eliteDef, string token)
        {
            if (eliteDef == null)
            {
                return false;
            }

            var descriptor = $"{eliteDef.name} {eliteDef.eliteEquipmentDef?.name}";
            foreach (var alias in GetEliteTokenAliases(token))
            {
                if (descriptor.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetEliteTokenAliases(string token)
        {
            var normalized = (token ?? string.Empty).ToUpperInvariant();
            switch (normalized)
            {
                case "BLAZING":
                case "AFFIXRED":
                case "EDFIRE":
                    return new[] { "BLAZING", "AFFIXRED", "EDFIRE", "FIRE" };
                case "GLACIAL":
                case "AFFIXBLUE":
                case "EDICE":
                    return new[] { "GLACIAL", "AFFIXBLUE", "EDICE", "ICE" };
                case "OVERLOADING":
                case "AFFIXWHITE":
                case "EDLIGHTNING":
                    return new[] { "OVERLOADING", "AFFIXWHITE", "EDLIGHTNING", "LIGHTNING" };
                case "MENDING":
                case "AFFIXPOISON":
                case "EDPOISON":
                    return new[] { "MENDING", "AFFIXPOISON", "EDPOISON", "POISON" };
                default:
                    return new[] { normalized };
            }
        }

        private static bool IsBossMaster(CharacterMaster master)
        {
            if (master == null)
            {
                return false;
            }

            var body = master.GetBody();
            if (body != null)
            {
                return body.isBoss;
            }

            var bodyPrefab = master.bodyPrefab;
            if (!bodyPrefab)
            {
                return false;
            }

            var prefabBody = bodyPrefab.GetComponent<CharacterBody>();
            return prefabBody != null && prefabBody.isBoss;
        }

        private Vector3 EnsureSpawnOutsideTeleporterRadius(CharacterMaster master, Vector3 fallbackAnchor)
        {
            if (master == null || !master.hasBody)
            {
                return fallbackAnchor;
            }

            var body = master.GetBody();
            if (body == null)
            {
                return fallbackAnchor;
            }

            if (body.isBoss)
            {
                return body.corePosition;
            }

            if (!_teleporterEventActive)
            {
                return body.corePosition;
            }

            var objective = GetObjectivePosition();
            var minEventDistance = GetTeleporterEventSpawnMinDistance();
            var minEventDistanceSqr = minEventDistance * minEventDistance;
            var insideChargingRadius = _holdoutZone != null && _holdoutZone.IsBodyInChargingRadius(body);
            var tooCloseToObjective = GetFlatDistanceSqr(body.corePosition, objective) < minEventDistanceSqr;
            if (!insideChargingRadius && !tooCloseToObjective)
            {
                return body.corePosition;
            }

            var outward = body.corePosition - objective;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.05f)
            {
                outward = fallbackAnchor - objective;
                outward.y = 0f;
            }

            if (outward.sqrMagnitude < 0.05f)
            {
                outward = UnityEngine.Random.insideUnitSphere;
                outward.y = 0f;
            }

            if (outward.sqrMagnitude < 0.05f)
            {
                outward = Vector3.forward;
            }

            outward.Normalize();

            for (var attempt = 0; attempt < 12; attempt++)
            {
                var distance = minEventDistance + attempt * 5f;
                var spin = Quaternion.AngleAxis(UnityEngine.Random.Range(-50f, 50f), Vector3.up);
                var candidateDirection = (spin * outward).normalized;
                var candidate = FindGroundedPosition(objective + candidateDirection * distance, 2f, 10f);
                MoveBodyToSpawnPosition(body, candidate);
                var candidateInsideCharge = _holdoutZone != null && _holdoutZone.IsBodyInChargingRadius(body);
                if (!candidateInsideCharge && GetFlatDistanceSqr(body.corePosition, objective) >= minEventDistanceSqr)
                {
                    return body.corePosition;
                }
            }

            var fallbackDistance = minEventDistance + 36f;
            var fallback = FindGroundedPosition(objective + outward * fallbackDistance, 0f, 12f);
            MoveBodyToSpawnPosition(body, fallback);
            return body.corePosition;
        }

        private float GetTeleporterEventSpawnMinDistance()
        {
            return 30f + GetDifficultyTier() * 2f;
        }

        private static float GetFlatDistanceSqr(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return (a - b).sqrMagnitude;
        }

        private static void MoveBodyToSpawnPosition(CharacterBody body, Vector3 position)
        {
            if (body == null)
            {
                return;
            }

            body.transform.position = position + Vector3.up;
            if (body.characterMotor != null)
            {
                body.characterMotor.velocity = Vector3.zero;
            }

            if (body.rigidbody != null)
            {
                body.rigidbody.velocity = Vector3.zero;
            }
        }

        private void TickEventBuffWindow(float deltaTime)
        {
            if (_eventBuffTimer <= 0f)
            {
                return;
            }

            _eventBuffTimer = Mathf.Max(0f, _eventBuffTimer - deltaTime);
        }

        private void TriggerEventBuffWindow(float duration, float magnitude)
        {
            _eventBuffId++;
            _eventBuffDuration = Mathf.Clamp(duration, 4f, 14f);
            _eventBuffMagnitude = Mathf.Clamp(magnitude, 0.05f, 0.65f);
            _eventBuffTimer = _eventBuffDuration;
        }

        private void TickHunterSquadTarget(float deltaTime)
        {
            if (!_stageActive)
            {
                _hunterSquadTarget = null;
                return;
            }

            _hunterSquadTargetTimer -= deltaTime;
            if (_hunterSquadTargetTimer > 0f && IsValidSquadTarget(_hunterSquadTarget))
            {
                return;
            }

            _hunterSquadTarget = SelectHunterSquadTarget();
            _hunterSquadTargetTimer = UnityEngine.Random.Range(6f, 10f);
        }

        private CharacterBody SelectHunterSquadTarget()
        {
            var isolated = GetMostIsolatedPlayer();
            if (isolated != null)
            {
                return isolated;
            }

            var members = TeamComponent.GetTeamMembers(TeamIndex.Player);
            CharacterBody weakest = null;
            var weakestFraction = float.MaxValue;
            var playerCount = 0;
            CharacterBody randomFallback = null;

            foreach (var member in members)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                playerCount++;
                if (body.healthComponent.combinedHealthFraction < weakestFraction)
                {
                    weakestFraction = body.healthComponent.combinedHealthFraction;
                    weakest = body;
                }

                if (UnityEngine.Random.Range(0, playerCount) == 0)
                {
                    randomFallback = body;
                }
            }

            if (playerCount == 0)
            {
                return null;
            }

            if (weakest != null && weakestFraction < 0.55f)
            {
                return weakest;
            }

            return randomFallback ?? weakest;
        }

        private static bool IsValidSquadTarget(CharacterBody body)
        {
            return body != null && body.healthComponent != null && body.healthComponent.alive;
        }

        internal bool TryGetActiveEventBuff(out int eventId, out float duration, out float magnitude)
        {
            if (_eventBuffTimer <= 0f)
            {
                eventId = 0;
                duration = 0f;
                magnitude = 0f;
                return false;
            }

            eventId = _eventBuffId;
            duration = _eventBuffDuration;
            magnitude = _eventBuffMagnitude;
            return true;
        }

        internal void ApplyDoctrineBuffPackage(CharacterBody body, WarfrontDoctrineProfile doctrine)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            switch (doctrine)
            {
                case WarfrontDoctrineProfile.SiegeFront:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 7.5f);
                    body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 7.5f);
                    body.healthComponent.HealFraction(0.025f, default);
                    break;
                case WarfrontDoctrineProfile.ArtilleryFront:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.8f);
                    body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, 6.8f);
                    break;
                case WarfrontDoctrineProfile.HunterCell:
                    body.AddTimedBuff(RoR2Content.Buffs.CloakSpeed, 6.4f);
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.4f);
                    break;
                case WarfrontDoctrineProfile.SwarmFront:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.2f);
                    body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, 5f);
                    break;
                case WarfrontDoctrineProfile.DisruptionFront:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.6f);
                    body.AddTimedBuff(RoR2Content.Buffs.Energized, 6.6f);
                    body.healthComponent.HealFraction(0.015f, default);
                    break;
                default:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6f);
                    break;
            }
        }

        internal void ApplyRoleBuffPackage(CharacterBody body, WarfrontRole role)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            switch (role)
            {
                case WarfrontRole.Contester:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.8f);
                    body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 6.8f);
                    body.healthComponent.HealFraction(0.015f, default);
                    break;
                case WarfrontRole.Artillery:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.5f);
                    body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, 6.5f);
                    break;
                case WarfrontRole.Flanker:
                    body.AddTimedBuff(RoR2Content.Buffs.CloakSpeed, 6.4f);
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.4f);
                    break;
                case WarfrontRole.Peeler:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.2f);
                    body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, 5.5f);
                    break;
                case WarfrontRole.Hunter:
                    body.AddTimedBuff(RoR2Content.Buffs.CloakSpeed, 6.1f);
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 6.1f);
                    body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, 5f);
                    break;
                case WarfrontRole.Anchor:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 7.2f);
                    body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 7.2f);
                    body.healthComponent.HealFraction(0.02f, default);
                    break;
                default:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 5.8f);
                    break;
            }
        }

        internal void ApplyEventBuffPackage(CharacterBody body, float duration, float magnitude)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            var finalDuration = Mathf.Clamp(duration * (1f + Mathf.Clamp(magnitude, 0f, 0.65f)), 3f, 16f);
            body.AddTimedBuff(RoR2Content.Buffs.Warbanner, finalDuration);

            if (magnitude >= 0.2f)
            {
                body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, finalDuration * 0.7f);
            }

            if (magnitude >= 0.25f)
            {
                body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, finalDuration * 0.5f);
            }
        }

        private void AttachRoleController(CharacterMaster master, WarfrontRole role)
        {
            if (!master || role == WarfrontRole.None || TryApplyBossChallengeAffix(master))
            {
                return;
            }

            var controller = master.GetComponent<WarfrontRoleController>();
            if (!controller)
            {
                controller = master.gameObject.AddComponent<WarfrontRoleController>();
            }

            controller.Initialize(this, master, role, _currentDoctrine);
        }

        private void AttachCommanderRoleControllers()
        {
            foreach (var node in _activeNodes)
            {
                if (node == null || !node.IsActive || node.Master == null)
                {
                    continue;
                }

                AttachRoleController(node.Master, ResolveCommanderRole(node.NodeType));
            }
        }

        private void DetachCommanderRoleControllers()
        {
            foreach (var node in _activeNodes)
            {
                if (node == null || node.Master == null)
                {
                    continue;
                }

                var controller = node.Master.GetComponent<WarfrontRoleController>();
                if (controller)
                {
                    Destroy(controller);
                }
            }
        }

        internal Vector3 GetObjectivePositionForAI()
        {
            return GetObjectivePosition();
        }

        internal float GetObjectiveRadiusForAI()
        {
            return 16f;
        }

        internal CharacterBody GetHunterSquadTargetForAI()
        {
            return IsValidSquadTarget(_hunterSquadTarget) ? _hunterSquadTarget : SelectHunterSquadTarget();
        }

        internal CharacterBody GetPeelerPriorityTargetForAI()
        {
            var members = TeamComponent.GetTeamMembers(TeamIndex.Player);
            CharacterBody weakest = null;
            var weakestFraction = float.MaxValue;

            foreach (var member in members)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                if (body.healthComponent.combinedHealthFraction < weakestFraction)
                {
                    weakestFraction = body.healthComponent.combinedHealthFraction;
                    weakest = body;
                }
            }

            if (weakest == null)
            {
                return null;
            }

            if (weakestFraction < 0.65f)
            {
                return weakest;
            }

            var isolated = GetMostIsolatedPlayer();
            return isolated ?? weakest;
        }

        internal Vector3 GetNearestCommanderPositionForAI(Vector3 origin)
        {
            Vector3 nearest = Vector3.zero;
            var nearestDistSqr = float.MaxValue;
            foreach (var node in _activeNodes)
            {
                if (node == null || !node.IsActive)
                {
                    continue;
                }

                var pos = node.CommandZonePosition;
                var distSqr = (pos - origin).sqrMagnitude;
                if (distSqr < nearestDistSqr)
                {
                    nearestDistSqr = distSqr;
                    nearest = pos;
                }
            }

            return nearest;
        }

        private void NotifyNearbyEnemiesCooldownExploit(Vector3 origin, float radius)
        {
            var radiusSqr = radius * radius;
            var teams = EnemyTeams;
            foreach (var teamIndex in teams)
            {
                var members = TeamComponent.GetTeamMembers(teamIndex);
                foreach (var member in members)
                {
                    var body = member ? member.body : null;
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                    {
                        continue;
                    }

                    if ((body.corePosition - origin).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }

                    var master = body.master;
                    if (master == null)
                    {
                        continue;
                    }

                    var rc = master.GetComponent<WarfrontRoleController>();
                    if (rc != null)
                    {
                        rc.OnNearbyPlayerBigHit(body);
                    }
                }
            }
        }

        internal int CountRoleEnemiesNearPosition(Vector3 position, float radius, WarfrontRole role)
        {
            var radiusSqr = radius * radius;
            var count = 0;
            var teams = EnemyTeams;
            foreach (var teamIndex in teams)
            {
                var members = TeamComponent.GetTeamMembers(teamIndex);
                foreach (var member in members)
                {
                    if (!member || member.body == null)
                    {
                        continue;
                    }

                    if ((member.body.corePosition - position).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }

                    var master = member.body.master;
                    if (master == null)
                    {
                        continue;
                    }

                    var rc = master.GetComponent<WarfrontRoleController>();
                    if (rc != null && rc.AssignedRole == role)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        internal Vector3 GetFlankPointForAI(Vector3 seekerPosition)
        {
            var objective = GetObjectivePosition();
            var teamCenter = GetPlayerCenterPosition();
            var baseline = teamCenter - objective;
            baseline.y = 0f;
            if (baseline.sqrMagnitude < 0.1f)
            {
                baseline = seekerPosition - objective;
                baseline.y = 0f;
            }

            if (baseline.sqrMagnitude < 0.1f)
            {
                baseline = Vector3.forward;
            }

            var enemyCenter = GetEnemyCenterNearObjective(objective, 50f);
            var enemyBias = enemyCenter - objective;
            enemyBias.y = 0f;

            Vector3 flankDirection;
            if (enemyBias.sqrMagnitude > 4f)
            {
                flankDirection = -enemyBias.normalized;
                var jitter = Quaternion.AngleAxis(UnityEngine.Random.Range(-40f, 40f), Vector3.up);
                flankDirection = (jitter * flankDirection).normalized;
            }
            else
            {
                var lateral = Vector3.Cross(Vector3.up, baseline.normalized);
                var side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                flankDirection = lateral * side;
            }

            var raw = objective + flankDirection * UnityEngine.Random.Range(16f, 28f);
            return FindGroundedPosition(raw, 2f, 7f);
        }

        private Vector3 GetEnemyCenterNearObjective(Vector3 objective, float radius)
        {
            var radiusSqr = radius * radius;
            var total = Vector3.zero;
            var count = 0;
            var teams = EnemyTeams;
            foreach (var teamIndex in teams)
            {
                var members = TeamComponent.GetTeamMembers(teamIndex);
                foreach (var member in members)
                {
                    var body = member ? member.body : null;
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                    {
                        continue;
                    }

                    if ((body.corePosition - objective).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }

                    total += body.corePosition;
                    count++;
                }
            }

            return count > 0 ? total / count : objective;
        }

        private int GetActiveCommanderCount()
        {
            var count = 0;
            foreach (var commander in _activeNodes)
            {
                if (commander && commander.IsActive)
                {
                    count++;
                }
            }

            return count;
        }

        private byte BuildCommanderTypeMask()
        {
            var mask = 0;
            foreach (var commander in _activeNodes)
            {
                if (!commander || !commander.IsActive)
                {
                    continue;
                }

                var bit = 1 << Mathf.Clamp((int)commander.NodeType, 0, 7);
                mask |= bit;
            }

            return (byte)mask;
        }

        private void AccumulateRoleSignal(WarfrontRole role, float amount)
        {
            if (role == WarfrontRole.None || amount <= 0f)
            {
                return;
            }

            if (!_stageRoleSignals.TryGetValue(role, out var signal))
            {
                signal = 0f;
            }

            _stageRoleSignals[role] = signal + amount;
        }

        private float GetRoleThreatSignal(WarfrontRole role)
        {
            return _runRoleThreatSignals.TryGetValue(role, out var signal) ? signal : 0f;
        }

        private bool HasWarning(WarWarning warning)
        {
            return _activeWarnings.Contains(warning);
        }

        private int GetDifficultyTier()
        {
            var difficulty = Run.instance ? Run.instance.difficultyCoefficient : 1f;
            return Mathf.Clamp(Mathf.FloorToInt(difficulty / 2.2f), 0, 5);
        }

        private int GetForgeNodeCount()
        {
            return GetNodeCount(WarfrontNodeType.Forge);
        }

        private bool AnyRelayNodeAffects(Vector3 position)
        {
            return AnyNodeAffects(WarfrontNodeType.Relay, position);
        }

        private int GetNodeCount(WarfrontNodeType nodeType)
        {
            var count = 0;
            foreach (var node in _activeNodes)
            {
                if (node && node.IsActive && node.NodeType == nodeType)
                {
                    count++;
                }
            }

            return count;
        }

        private bool AnyNodeAffects(WarfrontNodeType nodeType, Vector3 position)
        {
            foreach (var node in _activeNodes)
            {
                if (node && node.IsActive && node.NodeType == nodeType && node.AffectsPosition(position))
                {
                    return true;
                }
            }

            return false;
        }

        private int GetAlivePlayerCount()
        {
            var alive = 0;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body != null && body.healthComponent != null && body.healthComponent.alive)
                {
                    alive++;
                }
            }

            return alive;
        }

        private float GetTeamAverageHealthFraction()
        {
            var total = 0f;
            var count = 0;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                total += body.healthComponent.combinedHealthFraction;
                count++;
            }

            return count > 0 ? total / count : 0f;
        }

        private float GetAssaultPulseInterval()
        {
            var min = 5.1f;
            var max = 7.4f;
            if (_operationRoll.Anomaly == WarAnomaly.WarDrums)
            {
                min -= 1f;
                max -= 1.2f;
            }

            if (_operationRoll.Anomaly == WarAnomaly.BlitzOrder)
            {
                min -= 0.8f;
                max -= 1f;
            }

            if (_operationRoll.Anomaly == WarAnomaly.CounterIntel)
            {
                min += 0.7f;
                max += 0.9f;
            }

            if (HasWarning(WarWarning.PackTactics))
            {
                min -= 0.3f;
                max -= 0.4f;
            }

            if (_dominantRole == WarfrontRole.Hunter)
            {
                min -= 0.45f;
                max -= 0.55f;
            }

            switch (_currentDoctrine)
            {
                case WarfrontDoctrineProfile.SwarmFront:
                    min -= 0.5f;
                    max -= 0.7f;
                    break;
                case WarfrontDoctrineProfile.ArtilleryFront:
                    min += 0.45f;
                    max += 0.8f;
                    break;
                case WarfrontDoctrineProfile.HunterCell:
                    min -= 0.35f;
                    max -= 0.45f;
                    break;
                case WarfrontDoctrineProfile.SiegeFront:
                    min += 0.2f;
                    max += 0.4f;
                    break;
                case WarfrontDoctrineProfile.DisruptionFront:
                    min -= 0.15f;
                    max -= 0.2f;
                    break;
            }

            if (_mercyTimer > 0f)
            {
                min += 0.9f;
                max += 1.2f;
            }

            min = Mathf.Max(2.8f, min);
            max = Mathf.Max(min + 0.8f, max);

            var interval = UnityEngine.Random.Range(min, max);
            var siegeTier = GetChargeTier();
            var escalation = Mathf.Clamp(WarfrontDirectorPlugin.SiegeEscalationMultiplier.Value, 0.5f, 2f);
            interval *= Mathf.Lerp(1f, SiegePulseIntervalScale[siegeTier], escalation);
            return interval;
        }

        private WarfrontRole ResolveDominantRole(bool forceRotate)
        {
            if (!forceRotate && !_assaultActive)
            {
                return WarfrontRole.None;
            }

            if (forceRotate || _dominantRole == WarfrontRole.None || UnityEngine.Random.value < 0.35f)
            {
                _roleRotationCursor = (_roleRotationCursor + 1) % RoleRotation.Length;
            }

            var role = RoleRotation[Mathf.Clamp(_roleRotationCursor, 0, RoleRotation.Length - 1)];

            if (_operationRoll.Anomaly == WarAnomaly.CommandConfusion && !forceRotate && UnityEngine.Random.value < 0.25f)
            {
                role = RoleRotation[UnityEngine.Random.Range(0, RoleRotation.Length)];
            }

            if (HasWarning(WarWarning.ArtilleryDoctrine) && UnityEngine.Random.value < 0.45f)
            {
                role = WarfrontRole.Artillery;
            }

            if (HasWarning(WarWarning.HunterKiller) && (_loneWolfPressure > 0.4f || UnityEngine.Random.value < 0.4f))
            {
                role = WarfrontRole.Hunter;
            }

            if (HasWarning(WarWarning.PhalanxDoctrine) && UnityEngine.Random.value < 0.35f)
            {
                role = WarfrontRole.Contester;
            }

            if (HasWarning(WarWarning.SiegeEngine) && UnityEngine.Random.value < 0.3f)
            {
                role = WarfrontRole.Anchor;
            }

            if (_operationRoll.Anomaly == WarAnomaly.BlitzOrder && UnityEngine.Random.value < 0.3f)
            {
                role = WarfrontRole.Contester;
            }

            switch (_currentDoctrine)
            {
                case WarfrontDoctrineProfile.SwarmFront:
                    if (UnityEngine.Random.value < 0.4f)
                    {
                        var swarmRoll = UnityEngine.Random.value;
                        role = swarmRoll < 0.45f ? WarfrontRole.Contester : swarmRoll < 0.8f ? WarfrontRole.Peeler : WarfrontRole.Flanker;
                    }

                    break;
                case WarfrontDoctrineProfile.ArtilleryFront:
                    if (UnityEngine.Random.value < 0.5f)
                    {
                        role = WarfrontRole.Artillery;
                    }

                    break;
                case WarfrontDoctrineProfile.HunterCell:
                    if (_loneWolfPressure > 0.2f || UnityEngine.Random.value < 0.45f)
                    {
                        role = WarfrontRole.Hunter;
                    }

                    break;
                case WarfrontDoctrineProfile.SiegeFront:
                    if (UnityEngine.Random.value < 0.45f)
                    {
                        role = WarfrontRole.Anchor;
                    }

                    break;
                case WarfrontDoctrineProfile.DisruptionFront:
                    if (UnityEngine.Random.value < 0.45f)
                    {
                        role = UnityEngine.Random.value < 0.5f ? WarfrontRole.Peeler : WarfrontRole.Flanker;
                    }

                    break;
            }

            if (_operationRoll.Anomaly == WarAnomaly.CounterIntel && role == WarfrontRole.Hunter)
            {
                role = WarfrontRole.Peeler;
            }

            return role;
        }

        private Vector3 GetObjectivePosition()
        {
            if (_teleporter)
            {
                return _teleporter.transform.position;
            }

            var playersCenter = GetPlayerCenterPosition();
            return playersCenter == Vector3.zero && Stage.instance ? Stage.instance.transform.position : playersCenter;
        }

        private Vector3 GetPlayerCenterPosition()
        {
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            var total = Vector3.zero;
            var count = 0;
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                total += body.corePosition;
                count++;
            }

            return count > 0 ? total / count : Vector3.zero;
        }

        private CharacterBody GetMostIsolatedPlayer()
        {
            var members = TeamComponent.GetTeamMembers(TeamIndex.Player);

            CharacterBody isolated = null;
            var isolatedDistance = -1f;

            foreach (var member in members)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                var nearest = float.MaxValue;
                foreach (var otherMember in members)
                {
                    var other = otherMember ? otherMember.body : null;
                    if (other == null || other == body || other.healthComponent == null || !other.healthComponent.alive)
                    {
                        continue;
                    }

                    nearest = Mathf.Min(nearest, Vector3.Distance(body.corePosition, other.corePosition));
                }

                if (nearest == float.MaxValue)
                {
                    nearest = 0f;
                }

                if (nearest > isolatedDistance)
                {
                    isolatedDistance = nearest;
                    isolated = body;
                }
            }

            return isolatedDistance > 20f ? isolated : null;
        }

        private static Vector3 FindGroundedPosition(Vector3 center, float minRadius, float maxRadius)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var offset = UnityEngine.Random.insideUnitSphere;
                offset.y = 0f;
                offset.Normalize();
                offset *= UnityEngine.Random.Range(minRadius, maxRadius);

                var candidate = center + offset + Vector3.up * 60f;
                if (Physics.Raycast(candidate, Vector3.down, out var hitInfo, 200f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    return hitInfo.point;
                }
            }

            return center;
        }

        private static string ToDisplayName(WarWarning warning)
        {
            return warning switch
            {
                WarWarning.Sappers => "Sappers",
                WarWarning.PhalanxDoctrine => "Phalanx Doctrine",
                WarWarning.ArtilleryDoctrine => "Artillery Doctrine",
                WarWarning.HunterKiller => "Hunter-Killer",
                WarWarning.MedicNet => "Medic Net",
                WarWarning.Attrition => "Attrition",
                WarWarning.SiegeEngine => "Siege Engine",
                WarWarning.PackTactics => "Pack Tactics",
                WarWarning.SignalJamming => "Signal Jamming",
                WarWarning.ReinforcedVanguard => "Reinforced Vanguard",
                WarWarning.ExecutionOrder => "Execution Order",
                WarWarning.SupplyLine => "Supply Line",
                _ => warning.ToString()
            };
        }

        private static string ToDisplayName(WarAnomaly anomaly)
        {
            return anomaly switch
            {
                WarAnomaly.SilentMinute => "Silent Minute",
                WarAnomaly.WarDrums => "War Drums",
                WarAnomaly.FalseLull => "False Lull",
                WarAnomaly.CommandConfusion => "Command Confusion",
                WarAnomaly.Blackout => "Blackout",
                WarAnomaly.CounterIntel => "Counter Intel",
                WarAnomaly.BlitzOrder => "Blitz Order",
                WarAnomaly.IronRain => "Iron Rain",
                _ => anomaly.ToString()
            };
        }

        private static string ToDisplayName(WarfrontNodeType nodeType)
        {
            return nodeType switch
            {
                WarfrontNodeType.Relay => "Relay Commander",
                WarfrontNodeType.Forge => "Forge Commander",
                WarfrontNodeType.Siren => "Siren Commander",
                WarfrontNodeType.SpawnCache => "Cache Commander",
                _ => nodeType.ToString()
            };
        }

        private static string ToDisplayName(WarfrontDoctrineProfile doctrine)
        {
            return doctrine switch
            {
                WarfrontDoctrineProfile.Balanced => "Balanced Front",
                WarfrontDoctrineProfile.SwarmFront => "Swarm Front",
                WarfrontDoctrineProfile.ArtilleryFront => "Artillery Front",
                WarfrontDoctrineProfile.HunterCell => "Hunter Cell",
                WarfrontDoctrineProfile.SiegeFront => "Siege Front",
                WarfrontDoctrineProfile.DisruptionFront => "Disruption Front",
                _ => doctrine.ToString()
            };
        }

        private static void BroadcastWarfrontMessage(string message)
        {
            BroadcastWarfrontMessage(message, 0f);
        }

        private static void BroadcastWarfrontMessage(string message, float minRepeatIntervalSeconds)
        {
            if (!NetworkServer.active || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var now = Time.unscaledTime;
            if (minRepeatIntervalSeconds > 0f && string.Equals(_lastBroadcastMessage, message, StringComparison.Ordinal) && now - _lastBroadcastTime < minRepeatIntervalSeconds)
            {
                return;
            }

            _lastBroadcastMessage = message;
            _lastBroadcastTime = now;

            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = $"<color=#f15c2f>[Warfront]</color> {message}"
            });
        }

        private struct DirectorDefaults
        {
            internal float CreditMultiplier;
            internal float MinSeriesSpawnInterval;
            internal float MaxSeriesSpawnInterval;
            internal float EliteBias;
        }
    }
}

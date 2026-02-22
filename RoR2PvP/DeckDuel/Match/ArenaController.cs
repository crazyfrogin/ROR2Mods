using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace DeckDuel.Match
{
    public class ArenaController : IDisposable
    {
        private Vector3 _arenaCenter;
        private float _currentRadius;
        private float _initialRadius;
        private bool _isActive;
        private float _graceTimer;

        // Ring phase state (driven by RoundTimer)
        private RingPhase _ringPhase = RingPhase.A;

        // Per-player continuous time spent outside the ring (resets on re-entry)
        private readonly Dictionary<CharacterMaster, float> _timeOutside = new Dictionary<CharacterMaster, float>();

        // Visual boundary sphere (teleporter-style ward)
        private GameObject _sphereObj;
        private Renderer _sphereRenderer;
        private Material _sphereMat;
        private bool _clonedFromTeleporter;
        private float _pulseTimer;
        private float _lastBuiltRadius = -1f;
        private static readonly int TintColorID = Shader.PropertyToID("_TintColor");
        private static readonly int MainColorID = Shader.PropertyToID("_Color");

        public Vector3 ArenaCenter => _arenaCenter;
        public float CurrentRadius => _currentRadius;
        public bool IsActive => _isActive;
        public RingPhase CurrentRingPhase => _ringPhase;

        /// <summary>
        /// Horizontal (XZ-plane) distance, ignoring height differences.
        /// </summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public void StartArena(Vector3 center)
        {
            _arenaCenter = center;
            _initialRadius = ComputeMapRadius(center);
            _currentRadius = _initialRadius;
            _isActive = true;
            _ringPhase = RingPhase.A;
            _graceTimer = DeckDuelPlugin.Cfg.WarmupDuration.Value;
            _timeOutside.Clear();

            Log.Info($"Arena started at {center} with radius {_initialRadius}");
        }

        public void SetRingPhase(RingPhase phase)
        {
            if (_ringPhase == phase) return;
            _ringPhase = phase;
            Log.Info($"Ring phase → {phase}");
        }

        public void StopArena()
        {
            _isActive = false;
            _timeOutside.Clear();
        }

        public void ResetArena()
        {
            _currentRadius = _initialRadius;
            _ringPhase = RingPhase.A;
            _timeOutside.Clear();
        }

        public void Tick(float deltaTime)
        {
            // No boundary enforcement — entire map is playable
        }

        private float GetShrinkRate()
        {
            var cfg = DeckDuelPlugin.Cfg;
            switch (_ringPhase)
            {
                case RingPhase.A: return 0f; // No shrink during phase A
                case RingPhase.B: return cfg.PhaseBShrinkRate.Value;
                case RingPhase.C: return cfg.PhaseCShrinkRate.Value;
                default: return 0f;
            }
        }

        private void ApplyOutOfBoundsDamage(float deltaTime)
        {
            // Phase A: no outside damage at all
            if (_ringPhase == RingPhase.A) return;

            var cfg = DeckDuelPlugin.Cfg;
            float baseDPS = _ringPhase == RingPhase.C ? cfg.PhaseCBaseDPS.Value : cfg.PhaseBBaseDPS.Value;
            float rampPerSec = cfg.OutsideRampPerSecond.Value;
            float regenPauseDuration = cfg.RegenPauseDuration.Value;
            float hardKillRadius = _initialRadius * 3f;

            // Collect all duelist bodies (human + AI)
            var bodies = new List<(CharacterMaster master, CharacterBody body)>();
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (pc.master == null) continue;
                var body = pc.master.GetBody();
                if (body != null && body.healthComponent.alive)
                    bodies.Add((pc.master, body));
            }

            // Also check AI opponent
            var aiMaster = DeckDuelPlugin.Instance.AIOpponent?.AIMaster;
            if (aiMaster != null)
            {
                var aiBody = aiMaster.GetBody();
                if (aiBody != null && aiBody.healthComponent.alive)
                    bodies.Add((aiMaster, aiBody));
            }

            foreach (var (master, body) in bodies)
            {
                float dist = HorizontalDistance(body.transform.position, _arenaCenter);

                if (dist > _currentRadius)
                {
                    // Track continuous time outside
                    if (!_timeOutside.ContainsKey(master))
                        _timeOutside[master] = 0f;
                    _timeOutside[master] += deltaTime;

                    float timeOut = _timeOutside[master];

                    if (dist > hardKillRadius)
                    {
                        // Massive damage — way outside bounds
                        var hardDmg = new DamageInfo
                        {
                            damage = body.healthComponent.fullCombinedHealth * 0.5f,
                            position = body.transform.position,
                            damageType = DamageType.BypassArmor | DamageType.BypassBlock,
                            procCoefficient = 0f
                        };
                        body.healthComponent.TakeDamage(hardDmg);
                    }
                    else
                    {
                        // Ramping DoT: baseDPS + (rampPerSec * timeOutside)
                        float effectiveDPS = baseDPS + (rampPerSec * timeOut);
                        var damageInfo = new DamageInfo
                        {
                            damage = effectiveDPS * deltaTime,
                            position = body.transform.position,
                            damageType = DamageType.BypassArmor | DamageType.BypassBlock,
                            procCoefficient = 0f
                        };
                        body.healthComponent.TakeDamage(damageInfo);
                    }

                    // Pause regen: reset the out-of-danger stopwatch so natural regen doesn't kick in
                    body.outOfDangerStopwatch = 0f;

                    // Also apply HealingDisabled buff to suppress item-based regen
                    if (!body.HasBuff(RoR2Content.Buffs.HealingDisabled))
                        body.AddTimedBuff(RoR2Content.Buffs.HealingDisabled, regenPauseDuration);
                }
                else
                {
                    // Player is inside the ring — reset their outside timer
                    if (_timeOutside.ContainsKey(master))
                        _timeOutside[master] = 0f;
                }
            }
        }

        public Vector3 GetSpawnPosition(int playerIndex, int totalPlayers = 2)
        {
            if (totalPlayers <= 1)
                return SnapToGround(_arenaCenter);

            // Use a fixed, reasonable spread distance (not proportional to map radius)
            float offset = 20f;
            float angle = (2f * Mathf.PI * playerIndex) / totalPlayers;
            float x = Mathf.Sin(angle) * offset;
            float z = Mathf.Cos(angle) * offset;
            Vector3 pos = _arenaCenter + new Vector3(x, 0f, z);
            return SnapToGround(pos);
        }

        /// <summary>
        /// Raycast downward from well above the target position to find actual ground.
        /// Falls back to the nearest SpawnPoint if the raycast misses.
        /// </summary>
        private Vector3 SnapToGround(Vector3 pos)
        {
            // Cast from high above to find the ground
            Vector3 rayStart = new Vector3(pos.x, pos.y + 200f, pos.z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 400f, LayerIndex.world.mask))
            {
                return hit.point + Vector3.up * 1.5f; // slight offset above ground
            }

            // Raycast missed — try to use a nearby SpawnPoint
            var spawnPoints = SpawnPoint.readOnlyInstancesList;
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                float bestDist = float.MaxValue;
                Vector3 bestPos = pos;
                foreach (var sp in spawnPoints)
                {
                    float dist = Vector3.Distance(sp.transform.position, pos);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestPos = sp.transform.position;
                    }
                }
                Log.Warning($"SnapToGround: raycast missed at {pos}, using nearest SpawnPoint at {bestPos}");
                return bestPos;
            }

            Log.Warning($"SnapToGround: no ground found at {pos}, returning original position");
            return pos;
        }

        public Vector3 FindArenaCenter()
        {
            // Try to find the stage center from spawn points
            var spawnPoints = SpawnPoint.readOnlyInstancesList;
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (var sp in spawnPoints)
                {
                    sum += sp.transform.position;
                }
                return sum / spawnPoints.Count;
            }

            // Fallback: use the first player's position if available
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                var body = pc.master?.GetBody();
                if (body != null)
                    return body.transform.position;
            }

            return Vector3.zero;
        }

        public void TeleportPlayerToArena(CharacterBody body, int playerIndex, int totalPlayers = 2)
        {
            if (body == null) return;

            Vector3 spawnPos = GetSpawnPosition(playerIndex, totalPlayers);
#pragma warning disable CS0618
            TeleportHelper.TeleportBody(body, spawnPos);
#pragma warning restore CS0618

            // Zero out velocity to prevent momentum from a previous fall
            var motor = body.characterMotor;
            if (motor != null)
            {
                motor.velocity = Vector3.zero;
                motor.rootMotion = Vector3.zero;
            }

            Log.Info($"Teleported {body.GetUserName()} to arena position {spawnPos}");
        }

        // === Teleporter-Style Sphere Visual ===

        private void CreateBoundarySphere()
        {
            DestroyBoundarySphere();

            try
            {
                // Create a primitive sphere (diameter = 1, we scale to radius*2)
                _sphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphereObj.name = "DeckDuelArenaSphere";

                var col = _sphereObj.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);

                _sphereRenderer = _sphereObj.GetComponent<Renderer>();
                _sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _sphereRenderer.receiveShadows = false;

                // --- Material loading chain ---
                Material sourceMat = null;

                // 1) Try loading the teleporter range indicator material directly
                try
                {
                    sourceMat = UnityEngine.AddressableAssets.Addressables
                        .LoadAssetAsync<Material>("RoR2/Base/Teleporters/matTeleporterRangeIndicator.mat")
                        .WaitForCompletion();
                    if (sourceMat != null)
                        Log.Info($"Loaded matTeleporterRangeIndicator directly.");
                }
                catch (Exception ex)
                {
                    Log.Info($"Direct material load failed: {ex.Message}");
                }

                // 2) Try extracting from teleporter prefab's renderer hierarchy
                if (sourceMat == null)
                {
                    try
                    {
                        var teleporterPrefab = UnityEngine.AddressableAssets.Addressables
                            .LoadAssetAsync<GameObject>("RoR2/Base/Teleporters/Teleporter1.prefab")
                            .WaitForCompletion();

                        if (teleporterPrefab != null)
                        {
                            // First try via HoldoutZoneController.radiusIndicator
                            var hzc = teleporterPrefab.GetComponentInChildren<HoldoutZoneController>();
                            if (hzc != null && hzc.radiusIndicator != null)
                            {
                                var r = hzc.radiusIndicator.GetComponentInChildren<Renderer>();
                                if (r?.sharedMaterial != null)
                                {
                                    sourceMat = r.sharedMaterial;
                                    Log.Info($"Extracted material from radiusIndicator: {sourceMat.name}");
                                }
                            }

                            // Fallback: scan all renderers for an indicator-like material
                            if (sourceMat == null)
                            {
                                foreach (var r in teleporterPrefab.GetComponentsInChildren<Renderer>(true))
                                {
                                    if (r.sharedMaterial != null &&
                                        (r.sharedMaterial.name.Contains("Indicator") ||
                                         r.sharedMaterial.name.Contains("Range")))
                                    {
                                        sourceMat = r.sharedMaterial;
                                        Log.Info($"Found indicator material by name scan: {sourceMat.name}");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"Teleporter prefab material extraction failed: {ex.Message}");
                    }
                }

                // Apply material
                if (sourceMat != null)
                {
                    _sphereMat = new Material(sourceMat);
                    _clonedFromTeleporter = true;
                }
                else
                {
                    // 3) Final fallback: Sprites/Default — guaranteed visible, unlit, supports alpha
                    var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                    _sphereMat = new Material(shader);
                    _clonedFromTeleporter = false;
                    Log.Warning($"Arena sphere using fallback shader: {shader?.name ?? "NONE"}");
                }

                _sphereRenderer.material = _sphereMat;
                _sphereObj.transform.position = _arenaCenter;
                _pulseTimer = 0f;
                _lastBuiltRadius = -1f;
                UpdateBoundarySphere();
                Log.Info($"Arena sphere created at {_arenaCenter}. clonedMat={_clonedFromTeleporter}");
            }
            catch (Exception ex)
            {
                Log.Warning($"CreateBoundarySphere failed (non-fatal): {ex}");
                DestroyBoundarySphere();
            }
        }

        private void UpdateBoundarySphere()
        {
            if (_sphereObj == null || _sphereRenderer == null) return;

            // Pulse animation
            _pulseTimer += Time.deltaTime;
            float pulse = 0.85f + 0.15f * Mathf.Sin(_pulseTimer * 2.5f);

            // Phase-based color
            Color baseColor;
            switch (_ringPhase)
            {
                case RingPhase.A:
                    baseColor = _graceTimer > 0f
                        ? new Color(0.15f, 0.6f, 1f, 0.35f)
                        : new Color(0.2f, 0.85f, 1f, 0.35f);
                    break;
                case RingPhase.B:
                    baseColor = new Color(1f, 0.6f, 0.1f, 0.45f);
                    break;
                case RingPhase.C:
                    baseColor = new Color(1f, 0.15f, 0.15f, 0.55f);
                    break;
                default:
                    baseColor = new Color(1f, 1f, 1f, 0.35f);
                    break;
            }

            baseColor.a *= pulse;

            // Apply color via whichever property the material supports
            if (_sphereMat != null)
            {
                if (_sphereMat.HasProperty(TintColorID))
                    _sphereMat.SetColor(TintColorID, baseColor);
                if (_sphereMat.HasProperty(MainColorID))
                    _sphereMat.SetColor(MainColorID, baseColor);
            }

            // Scale sphere to match current radius
            if (Mathf.Abs(_currentRadius - _lastBuiltRadius) > 0.1f || _lastBuiltRadius < 0f)
            {
                // Unity primitive sphere has diameter 1, so scale = radius * 2
                float scale = _currentRadius * 2f;
                _sphereObj.transform.localScale = new Vector3(scale, scale, scale);
                _sphereObj.transform.position = _arenaCenter;
                _lastBuiltRadius = _currentRadius;
            }
        }

        private void DestroyBoundarySphere()
        {
            if (_sphereObj != null)
            {
                UnityEngine.Object.Destroy(_sphereObj);
                _sphereObj = null;
                _sphereRenderer = null;
            }
            if (_sphereMat != null)
            {
                UnityEngine.Object.Destroy(_sphereMat);
                _sphereMat = null;
            }
            _clonedFromTeleporter = false;
        }

        /// <summary>
        /// Auto-compute arena radius from the map's ground NodeGraph so the ring
        /// covers the entire playable area. Falls back to the config value.
        /// </summary>
        private float ComputeMapRadius(Vector3 center)
        {
            float configRadius = DeckDuelPlugin.Cfg.ArenaRadius.Value;

            try
            {
                var sceneInfo = RoR2.SceneInfo.instance;
                var groundNodes = sceneInfo?.groundNodes;
                if (groundNodes == null || groundNodes.nodes == null || groundNodes.nodes.Length == 0)
                {
                    Log.Info($"No ground NodeGraph found — using config radius {configRadius}");
                    return configRadius;
                }

                float maxDist = 0f;
                foreach (var node in groundNodes.nodes)
                {
                    float dist = HorizontalDistance(node.position, center);
                    if (dist > maxDist) maxDist = dist;
                }

                // Add a small margin so the ring edge sits just outside the farthest walkable node
                float mapRadius = maxDist + 10f;

                // Use whichever is larger: the map-derived radius or the config value
                float finalRadius = Mathf.Max(mapRadius, configRadius);
                Log.Info($"ComputeMapRadius: groundNodes={groundNodes.nodes.Length}, maxDist={maxDist:F1}, mapRadius={mapRadius:F1}, config={configRadius}, final={finalRadius:F1}");
                return finalRadius;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"ComputeMapRadius failed, using config: {ex.Message}");
                return configRadius;
            }
        }

        public void Dispose()
        {
            StopArena();
        }
    }
}

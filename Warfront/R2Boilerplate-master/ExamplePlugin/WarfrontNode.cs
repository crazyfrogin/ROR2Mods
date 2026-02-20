using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace WarfrontDirector
{
    internal sealed class WarfrontNode : MonoBehaviour
    {
        private const float DefaultEffectRadius = 30f;
        private const float DefaultTetherDistance = 54f;
        private const float HardSnapMultiplier = 1.35f;
        private const float AuraPulseInterval = 0.75f;
        private const float AntiKiteSeconds = 7f;
        private const float ReengageBurstSeconds = 3f;
        private const float EnrageThreshold = 0.5f;
        private const float FrenzyThreshold = 0.25f;
        private const float EnragePulseInterval = 1.2f;
        private const float RegenPulseInterval = 2f;
        private const float RegenFraction = 0.008f;

        private WarfrontDirectorController _owner;
        private CharacterMaster _master;
        private bool _consumed;
        private float _auraTimer;
        private float _idleTimer;
        private float _enrageTimer;
        private float _regenTimer;
        private float _lastTrackedHealth;
        private bool _enraged;
        private bool _frenzied;
        private float _tetherDistance = DefaultTetherDistance;
        private Vector3 _commandZonePosition;

        internal WarfrontNodeType NodeType { get; private set; }
        internal float EffectRadius { get; private set; } = DefaultEffectRadius;
        internal bool IsActive => !_consumed && _master != null && _master.hasBody;
        internal Vector3 CommandZonePosition => _commandZonePosition;
        internal CharacterMaster Master => _master;

        internal void Initialize(WarfrontDirectorController owner, WarfrontNodeType nodeType, CharacterMaster master, Vector3 commandZonePosition, float effectRadius, float tetherDistance)
        {
            _owner = owner;
            NodeType = nodeType;
            _master = master;
            _consumed = false;
            _commandZonePosition = commandZonePosition;
            EffectRadius = Mathf.Max(16f, effectRadius);
            _tetherDistance = Mathf.Max(24f, tetherDistance);
            _auraTimer = AuraPulseInterval;
            _idleTimer = 0f;
            _enrageTimer = 0f;
            _regenTimer = 0f;
            _enraged = false;
            _frenzied = false;
            _lastTrackedHealth = -1f;
        }

        internal void RelocateCommandZone(Vector3 newPosition)
        {
            _commandZonePosition = newPosition;
        }

        internal bool AffectsPosition(Vector3 position)
        {
            if (_consumed)
            {
                return false;
            }

            var sqrRadius = EffectRadius * EffectRadius;
            return (position - GetCenterPosition()).sqrMagnitude <= sqrRadius;
        }

        internal void ForceRetire(bool triggerDefeat)
        {
            if (_consumed)
            {
                return;
            }

            _consumed = true;
            if (triggerDefeat)
            {
                _owner?.OnCommanderDefeated(this);
            }
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _consumed)
            {
                return;
            }

            if (_master == null || !_master.hasBody)
            {
                ForceRetire(triggerDefeat: true);
                return;
            }

            var body = _master.GetBody();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                ForceRetire(triggerDefeat: true);
                return;
            }

            var deltaTime = Time.fixedDeltaTime;
            ClampCommanderHealth(body);
            TickTether(body, deltaTime);
            TickAura(body, deltaTime);
            TickAntiKite(body, deltaTime);
            TickEnrage(body, deltaTime);
        }

        private void TickTether(CharacterBody body, float deltaTime)
        {
            DriftCommandZone(deltaTime);

            if (IsPlayerNearby(body.corePosition, _tetherDistance))
            {
                return;
            }

            var toZone = _commandZonePosition - body.corePosition;
            toZone.y = 0f;
            var distance = toZone.magnitude;
            if (distance <= _tetherDistance)
            {
                return;
            }

            if (distance >= _tetherDistance * HardSnapMultiplier)
            {
                body.transform.position = _commandZonePosition + Vector3.up * 1.5f;
                if (body.characterMotor != null)
                {
                    body.characterMotor.velocity = Vector3.zero;
                }

                return;
            }

            var pullDirection = toZone / Mathf.Max(0.01f, distance);
            if (body.characterMotor != null)
            {
                body.characterMotor.velocity += pullDirection * (24f * deltaTime);
            }
            else if (body.rigidbody != null)
            {
                body.rigidbody.velocity += pullDirection * (18f * deltaTime);
            }
        }

        private void DriftCommandZone(float deltaTime)
        {
            if (_owner == null)
            {
                return;
            }

            var objective = _owner.GetObjectivePositionForAI();
            if (objective == Vector3.zero)
            {
                return;
            }

            var toObjective = objective - _commandZonePosition;
            toObjective.y = 0f;
            if (toObjective.sqrMagnitude < 4f)
            {
                return;
            }

            _commandZonePosition += toObjective.normalized * Mathf.Min(6f * deltaTime, toObjective.magnitude);
        }

        private void TickAura(CharacterBody commanderBody, float deltaTime)
        {
            _auraTimer -= deltaTime;
            if (_auraTimer > 0f)
            {
                return;
            }

            _auraTimer = AuraPulseInterval;

            var center = commanderBody.corePosition;
            var radiusSqr = EffectRadius * EffectRadius;
            var teams = new[] { TeamIndex.Monster, TeamIndex.Void, TeamIndex.Lunar };
            foreach (var team in teams)
            {
                var members = TeamComponent.GetTeamMembers(team);
                foreach (var member in members)
                {
                    var body = member ? member.body : null;
                    if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                    {
                        continue;
                    }

                    if (body == commanderBody)
                    {
                        continue;
                    }

                    if ((body.corePosition - center).sqrMagnitude > radiusSqr)
                    {
                        continue;
                    }

                    ApplyAuraEffect(body);
                }
            }
        }

        private void ApplyAuraEffect(CharacterBody body)
        {
            switch (NodeType)
            {
                case WarfrontNodeType.Relay:
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 2.4f);
                    break;
                case WarfrontNodeType.Forge:
                    body.healthComponent.HealFraction(0.05f, default);
                    break;
                case WarfrontNodeType.Siren:
                    if (body.healthComponent.combinedHealthFraction < 0.98f)
                    {
                        body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 1.8f);
                    }

                    break;
                case WarfrontNodeType.SpawnCache:
                    body.healthComponent.HealFraction(0.032f, default);
                    break;
            }
        }

        private void TickAntiKite(CharacterBody commanderBody, float deltaTime)
        {
            if (IsPlayerNearby(commanderBody.corePosition, 38f))
            {
                _idleTimer = 0f;
                return;
            }

            _idleTimer += deltaTime;
            if (_idleTimer < AntiKiteSeconds)
            {
                return;
            }

            _idleTimer = 0f;
            commanderBody.AddTimedBuff(RoR2Content.Buffs.Warbanner, ReengageBurstSeconds);
        }

        private static bool IsPlayerNearby(Vector3 origin, float radius)
        {
            var radiusSqr = radius * radius;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var player in players)
            {
                var body = player ? player.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                if ((body.corePosition - origin).sqrMagnitude <= radiusSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private void TickEnrage(CharacterBody body, float deltaTime)
        {
            var hpFraction = body.healthComponent.combinedHealthFraction;

            if (!_frenzied && hpFraction <= FrenzyThreshold)
            {
                _frenzied = true;
                body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, 20f);
                body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, 20f);
                body.AddTimedBuff(RoR2Content.Buffs.CloakSpeed, 12f);
                body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 20f);
                body.AddTimedBuff(RoR2Content.Buffs.Energized, 20f);
            }
            else if (!_enraged && hpFraction <= EnrageThreshold)
            {
                _enraged = true;
                body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, 14f);
                body.AddTimedBuff(RoR2Content.Buffs.PowerBuff, 14f);
                body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, 14f);
            }

            if (!_enraged)
            {
                return;
            }

            _enrageTimer -= deltaTime;
            if (_enrageTimer > 0f)
            {
                return;
            }

            _enrageTimer = EnragePulseInterval;

            if (_frenzied)
            {
                body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, EnragePulseInterval + 0.3f);
                body.AddTimedBuff(RoR2Content.Buffs.ArmorBoost, EnragePulseInterval + 0.3f);
            }
            else
            {
                body.AddTimedBuff(RoR2Content.Buffs.Warbanner, EnragePulseInterval + 0.3f);
            }
        }

        private void ClampCommanderHealth(CharacterBody body)
        {
            var hc = body.healthComponent;
            if (hc == null)
            {
                return;
            }

            var currentHealth = hc.health;
            if (_lastTrackedHealth < 0f)
            {
                _lastTrackedHealth = currentHealth;
            }
            else if (currentHealth > _lastTrackedHealth)
            {
                hc.health = _lastTrackedHealth;
            }
            else
            {
                _lastTrackedHealth = currentHealth;
            }
        }

        private void TickPassiveRegen(CharacterBody body, float deltaTime)
        {
            _regenTimer -= deltaTime;
            if (_regenTimer > 0f)
            {
                return;
            }

            _regenTimer = RegenPulseInterval;

            var healAmount = RegenFraction;
            if (_enraged)
            {
                healAmount *= 0.5f;
            }

            body.healthComponent.HealFraction(healAmount, default);
        }

        private Vector3 GetCenterPosition()
        {
            var body = _master ? _master.GetBody() : null;
            return body != null ? body.corePosition : _commandZonePosition;
        }
    }
}

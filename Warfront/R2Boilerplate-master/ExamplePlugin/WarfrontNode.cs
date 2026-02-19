using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace WarfrontDirector
{
    internal sealed class WarfrontNode : MonoBehaviour
    {
        private const float DefaultEffectRadius = 30f;
        private const float DefaultTetherDistance = 54f;
        private const float HardSnapMultiplier = 1.45f;
        private const float AuraPulseInterval = 0.9f;
        private const float AntiKiteSeconds = 7f;
        private const float ReengageBurstSeconds = 3f;

        private WarfrontDirectorController _owner;
        private CharacterMaster _master;
        private bool _consumed;
        private float _auraTimer;
        private float _idleTimer;
        private float _tetherDistance = DefaultTetherDistance;
        private Vector3 _commandZonePosition;

        internal WarfrontNodeType NodeType { get; private set; }
        internal float EffectRadius { get; private set; } = DefaultEffectRadius;
        internal bool IsActive => !_consumed && _master != null && _master.hasBody;
        internal Vector3 CommandZonePosition => _commandZonePosition;

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
            TickTether(body, deltaTime);
            TickAura(body, deltaTime);
            TickAntiKite(body, deltaTime);
        }

        private void TickTether(CharacterBody body, float deltaTime)
        {
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
                body.characterMotor.velocity += pullDirection * (18f * deltaTime);
            }
            else if (body.rigidbody != null)
            {
                body.rigidbody.velocity += pullDirection * (14f * deltaTime);
            }
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
                    body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 1.5f);
                    break;
                case WarfrontNodeType.Forge:
                    body.healthComponent.HealFraction(0.03f, default);
                    break;
                case WarfrontNodeType.Siren:
                    if (body.healthComponent.combinedHealthFraction < 0.98f)
                    {
                        body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 1.1f);
                    }

                    break;
                case WarfrontNodeType.SpawnCache:
                    body.healthComponent.HealFraction(0.018f, default);
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

        private Vector3 GetCenterPosition()
        {
            var body = _master ? _master.GetBody() : null;
            return body != null ? body.corePosition : _commandZonePosition;
        }
    }
}

using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace WarfrontDirector
{
    internal sealed class WarfrontRoleController : MonoBehaviour
    {
        private WarfrontDirectorController _owner;
        private CharacterMaster _master;
        private WarfrontRole _assignedRole;
        private WarfrontDoctrineProfile _assignedDoctrine;

        private bool _doctrineBuffApplied;
        private bool _roleBuffApplied;
        private int _lastEventBuffId = -1;
        private float _thinkTimer;
        private Vector3 _flankPoint;

        internal void Initialize(WarfrontDirectorController owner, CharacterMaster master, WarfrontRole role, WarfrontDoctrineProfile doctrine)
        {
            _owner = owner;
            _master = master;
            _assignedRole = role;
            _assignedDoctrine = doctrine;
            _doctrineBuffApplied = false;
            _roleBuffApplied = false;
            _lastEventBuffId = -1;
            _thinkTimer = 0f;
            _flankPoint = Vector3.zero;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active || _owner == null || _master == null)
            {
                return;
            }

            if (!_master.hasBody)
            {
                return;
            }

            var body = _master.GetBody();
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            EnsureBuffPackages(body);

            _thinkTimer -= Time.fixedDeltaTime;
            if (_thinkTimer > 0f)
            {
                return;
            }

            _thinkTimer = 0.2f;
            TickRoleSteering(body, Time.fixedDeltaTime);
        }

        private void EnsureBuffPackages(CharacterBody body)
        {
            if (!_doctrineBuffApplied)
            {
                _owner.ApplyDoctrineBuffPackage(body, _assignedDoctrine);
                _doctrineBuffApplied = true;
            }

            if (!_roleBuffApplied)
            {
                _owner.ApplyRoleBuffPackage(body, _assignedRole);
                _roleBuffApplied = true;
            }

            if (_owner.TryGetActiveEventBuff(out var eventId, out var duration, out var magnitude) && eventId != _lastEventBuffId)
            {
                _owner.ApplyEventBuffPackage(body, duration, magnitude);
                _lastEventBuffId = eventId;
            }
        }

        private void TickRoleSteering(CharacterBody body, float deltaTime)
        {
            switch (_assignedRole)
            {
                case WarfrontRole.Contester:
                    TickContester(body, deltaTime);
                    break;
                case WarfrontRole.Artillery:
                    TickArtillery(body, deltaTime);
                    break;
                case WarfrontRole.Flanker:
                    TickFlanker(body, deltaTime);
                    break;
                case WarfrontRole.Peeler:
                    TickPeeler(body, deltaTime);
                    break;
                case WarfrontRole.Hunter:
                    TickHunter(body, deltaTime);
                    break;
                case WarfrontRole.Anchor:
                    TickAnchor(body, deltaTime);
                    break;
            }
        }

        private void TickContester(CharacterBody body, float deltaTime)
        {
            var objective = _owner.GetObjectivePositionForAI();
            var toObjective = objective - body.corePosition;
            toObjective.y = 0f;
            var objectiveRadius = _owner.GetObjectiveRadiusForAI();
            var distance = toObjective.magnitude;

            if (distance > objectiveRadius * 0.8f)
            {
                ApplySteering(body, toObjective, 34f, 1.45f, deltaTime);
                return;
            }

            if (body.healthComponent.combinedHealthFraction < 0.35f)
            {
                var edgeDirection = body.corePosition - objective;
                edgeDirection.y = 0f;
                if (edgeDirection.sqrMagnitude < 0.05f)
                {
                    edgeDirection = body.characterDirection ? body.characterDirection.forward : Vector3.forward;
                }

                var edgeTarget = objective + edgeDirection.normalized * Mathf.Max(6f, objectiveRadius);
                ApplySteering(body, edgeTarget - body.corePosition, 26f, 1.25f, deltaTime);
                return;
            }

            ApplySteering(body, toObjective, 18f, 1.15f, deltaTime);
        }

        private void TickArtillery(CharacterBody body, float deltaTime)
        {
            var objective = _owner.GetObjectivePositionForAI();
            var toObjective = objective - body.corePosition;
            toObjective.y = 0f;
            var distance = toObjective.magnitude;

            const float minRange = 18f;
            const float maxRange = 32f;

            if (distance < minRange)
            {
                ApplySteering(body, -toObjective, 34f, 1.55f, deltaTime);
                return;
            }

            if (distance > maxRange)
            {
                ApplySteering(body, toObjective, 26f, 1.25f, deltaTime);
                return;
            }

            var lateral = Vector3.Cross(Vector3.up, toObjective.normalized);
            var strafeSign = Mathf.Sign(Mathf.Sin(Time.time * 1.6f + GetInstanceID()));
            ApplySteering(body, lateral * strafeSign, 22f, 1.1f, deltaTime);
        }

        private void TickFlanker(CharacterBody body, float deltaTime)
        {
            if (_flankPoint == Vector3.zero || (body.corePosition - _flankPoint).sqrMagnitude < 16f)
            {
                _flankPoint = _owner.GetFlankPointForAI(body.corePosition);
            }

            var toFlank = _flankPoint - body.corePosition;
            toFlank.y = 0f;
            ApplySteering(body, toFlank, 30f, 1.35f, deltaTime);

            if (toFlank.sqrMagnitude <= 64f)
            {
                var objective = _owner.GetObjectivePositionForAI();
                ApplySteering(body, objective - body.corePosition, 20f, 1.2f, deltaTime);
            }
        }

        private void TickPeeler(CharacterBody body, float deltaTime)
        {
            var target = _owner.GetPeelerPriorityTargetForAI();
            if (target != null)
            {
                var toTarget = target.corePosition - body.corePosition;
                toTarget.y = 0f;
                var distance = toTarget.magnitude;
                if (distance > 9f)
                {
                    ApplySteering(body, toTarget, 32f, 1.45f, deltaTime);
                }
                else
                {
                    var objective = _owner.GetObjectivePositionForAI();
                    ApplySteering(body, body.corePosition - objective, 18f, 1.1f, deltaTime);
                }

                return;
            }

            TickContester(body, deltaTime);
        }

        private void TickHunter(CharacterBody body, float deltaTime)
        {
            var target = _owner.GetHunterSquadTargetForAI();
            if (target == null)
            {
                TickFlanker(body, deltaTime);
                return;
            }

            var toTarget = target.corePosition - body.corePosition;
            toTarget.y = 0f;
            ApplySteering(body, toTarget, 36f, 1.55f, deltaTime);
        }

        private void TickAnchor(CharacterBody body, float deltaTime)
        {
            var objective = _owner.GetObjectivePositionForAI();
            var objectiveRadius = _owner.GetObjectiveRadiusForAI();
            var toBody = body.corePosition - objective;
            toBody.y = 0f;

            if (toBody.sqrMagnitude < 1f)
            {
                toBody = body.characterDirection ? body.characterDirection.forward : Vector3.forward;
            }

            var holdPoint = objective + toBody.normalized * Mathf.Max(5f, objectiveRadius * 0.55f);
            var toHold = holdPoint - body.corePosition;
            toHold.y = 0f;

            if (toHold.magnitude > 3.5f)
            {
                ApplySteering(body, toHold, 24f, 1.2f, deltaTime);
                return;
            }

            var strafe = Vector3.Cross(Vector3.up, toBody.normalized);
            ApplySteering(body, strafe, 14f, 1.05f, deltaTime);
        }

        private static void ApplySteering(CharacterBody body, Vector3 direction, float acceleration, float speedMultiplier, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
            {
                return;
            }

            var unit = direction.normalized;
            var maxSpeed = Mathf.Max(8f, body.moveSpeed * speedMultiplier);

            if (body.characterMotor != null)
            {
                var velocity = body.characterMotor.velocity;
                var planar = new Vector3(velocity.x, 0f, velocity.z);
                planar += unit * (acceleration * deltaTime);
                planar = Vector3.ClampMagnitude(planar, maxSpeed);
                body.characterMotor.velocity = new Vector3(planar.x, velocity.y, planar.z);
                return;
            }

            if (body.rigidbody != null)
            {
                var velocity = body.rigidbody.velocity;
                var planar = new Vector3(velocity.x, 0f, velocity.z);
                planar += unit * (acceleration * deltaTime);
                planar = Vector3.ClampMagnitude(planar, maxSpeed);
                body.rigidbody.velocity = new Vector3(planar.x, velocity.y, planar.z);
            }
        }
    }
}

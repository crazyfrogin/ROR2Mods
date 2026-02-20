using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace WarfrontDirector
{
    internal sealed class WarfrontRoleController : MonoBehaviour
    {
        private const float ThinkInterval = 0.2f;
        private const float TargetOverrideInterval = 0.6f;
        private const float DodgeCheckInterval = 0.35f;
        private const float GroupChargeRadius = 30f;
        private const int GroupChargeThreshold = 3;
        private const float CooldownExploitWindow = 3f;
        private const float CooldownExploitDamageThreshold = 200f;
        private const float RoleSwitchCheckInterval = 5f;

        private const float CommanderThinkInterval = 0.1f;
        private const float CommanderTargetOverrideInterval = 0.3f;
        private const float CommanderDodgeCheckInterval = 0.15f;
        private const float CommanderRoleSwitchCheckInterval = 3f;
        private const float CommanderDodgeDotThreshold = 0.65f;
        private const float CommanderDodgeRange = 40f;
        private const float CommanderSteeringAccelMult = 1.5f;
        private const float CommanderSteeringSpeedMult = 1.35f;

        private const float BossThinkInterval = 0.08f;
        private const float BossTargetOverrideInterval = 0.25f;
        private const float BossDodgeCheckInterval = 0.12f;
        private const float BossDodgeDotThreshold = 0.55f;
        private const float BossDodgeRange = 50f;
        private const float BossSteeringAccelMult = 1.8f;
        private const float BossSteeringSpeedMult = 1.5f;
        private const float BossEnrageHealthThreshold = 0.4f;
        private const float BossAggroSwitchInterval = 2.5f;
        private const float BossLeapCheckInterval = 1.2f;
        private const float BossLeapMinDistance = 18f;
        private const float BossLeapForce = 65f;

        private WarfrontDirectorController _owner;
        private CharacterMaster _master;
        private WarfrontRole _assignedRole;
        private WarfrontDoctrineProfile _assignedDoctrine;

        private bool _doctrineBuffApplied;
        private bool _roleBuffApplied;
        private bool _skillDriversTuned;
        private int _lastEventBuffId = -1;
        private float _thinkTimer;
        private float _targetOverrideTimer;
        private float _dodgeTimer;
        private float _roleSwitchTimer;
        private float _retreatTimer;
        private float _groupChargeTimer;
        private Vector3 _flankPoint;
        private bool _isRetreating;
        private bool _groupChargeActive;
        private bool _isCommander;
        private bool _isBoss;
        private float _bossAggroSwitchTimer;
        private float _bossLeapTimer;
        private CharacterBody _bossCurrentTarget;
        private bool _bossIsEnraged;

        internal WarfrontRole AssignedRole => _assignedRole;
        internal bool IsCommander => _isCommander;

        internal void Initialize(WarfrontDirectorController owner, CharacterMaster master, WarfrontRole role, WarfrontDoctrineProfile doctrine, bool isCommander = false, bool isBoss = false)
        {
            _owner = owner;
            _master = master;
            _assignedRole = role;
            _assignedDoctrine = doctrine;
            _isCommander = isCommander;
            _isBoss = isBoss;
            _doctrineBuffApplied = false;
            _roleBuffApplied = false;
            _skillDriversTuned = false;
            _lastEventBuffId = -1;
            _thinkTimer = 0f;
            _targetOverrideTimer = 0f;
            _dodgeTimer = 0f;
            _roleSwitchTimer = _isCommander ? CommanderRoleSwitchCheckInterval : RoleSwitchCheckInterval;
            _retreatTimer = 0f;
            _groupChargeTimer = 0f;
            _flankPoint = Vector3.zero;
            _isRetreating = false;
            _groupChargeActive = false;
            _bossAggroSwitchTimer = 0f;
            _bossLeapTimer = 0f;
            _bossCurrentTarget = null;
            _bossIsEnraged = false;
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

            var deltaTime = Time.fixedDeltaTime;

            EnsureBuffPackages(body);
            EnsureSkillDriversTuned();

            if (_isBoss)
            {
                TickBoss(body, deltaTime);
                return;
            }

            _targetOverrideTimer -= deltaTime;
            if (_targetOverrideTimer <= 0f)
            {
                _targetOverrideTimer = _isCommander ? CommanderTargetOverrideInterval : TargetOverrideInterval;
                TickTargetOverride(body);
            }

            _dodgeTimer -= deltaTime;
            if (_dodgeTimer <= 0f)
            {
                _dodgeTimer = _isCommander ? CommanderDodgeCheckInterval : DodgeCheckInterval;
                TickDodge(body, deltaTime);
            }

            _groupChargeTimer -= deltaTime;
            if (_groupChargeTimer <= 0f)
            {
                _groupChargeTimer = 1f;
                TickGroupCharge(body);
            }

            _roleSwitchTimer -= deltaTime;
            if (_roleSwitchTimer <= 0f)
            {
                _roleSwitchTimer = _isCommander ? CommanderRoleSwitchCheckInterval : RoleSwitchCheckInterval;
                TickRoleSwitch(body);
            }

            _thinkTimer -= deltaTime;
            if (_thinkTimer > 0f)
            {
                return;
            }

            _thinkTimer = _isCommander ? CommanderThinkInterval : ThinkInterval;

            if (!_isCommander && TickRetreat(body, deltaTime))
            {
                return;
            }

            if (_isCommander)
            {
                TickCommanderSteering(body, deltaTime);
                return;
            }

            TickRoleSteering(body, deltaTime);
        }

        #region Boss AI

        private void TickBoss(CharacterBody body, float deltaTime)
        {
            if (!_bossIsEnraged && body.healthComponent.combinedHealthFraction <= BossEnrageHealthThreshold)
            {
                _bossIsEnraged = true;
            }

            ForceBossAITarget(body);

            _targetOverrideTimer -= deltaTime;
            if (_targetOverrideTimer <= 0f)
            {
                _targetOverrideTimer = BossTargetOverrideInterval;
                TickBossTargetOverride(body);
            }

            _dodgeTimer -= deltaTime;
            if (_dodgeTimer <= 0f)
            {
                _dodgeTimer = BossDodgeCheckInterval;
                TickBossDodge(body, deltaTime);
            }

            _bossLeapTimer -= deltaTime;
            if (_bossLeapTimer <= 0f)
            {
                _bossLeapTimer = BossLeapCheckInterval;
                TickBossLeap(body);
            }

            _thinkTimer -= deltaTime;
            if (_thinkTimer > 0f)
            {
                return;
            }

            _thinkTimer = BossThinkInterval;
            TickBossSteering(body, deltaTime);
        }

        private void ForceBossAITarget(CharacterBody body)
        {
            var ai = _master.GetComponent<BaseAI>();
            if (ai == null)
            {
                return;
            }

            if (_bossCurrentTarget != null && _bossCurrentTarget.healthComponent != null && _bossCurrentTarget.healthComponent.alive)
            {
                ai.currentEnemy.gameObject = _bossCurrentTarget.gameObject;
                ai.currentEnemy.bestHurtBox = _bossCurrentTarget.mainHurtBox;
                ai.enemyAttention = BossAggroSwitchInterval + 1f;
                return;
            }

            var currentAITarget = ai.currentEnemy.gameObject != null ? ai.currentEnemy.gameObject.GetComponent<CharacterBody>() : null;
            if (currentAITarget != null && !IsPlayerBody(currentAITarget))
            {
                _bossCurrentTarget = SelectBossTarget(body);
                if (_bossCurrentTarget != null)
                {
                    ai.currentEnemy.gameObject = _bossCurrentTarget.gameObject;
                    ai.currentEnemy.bestHurtBox = _bossCurrentTarget.mainHurtBox;
                    ai.enemyAttention = BossAggroSwitchInterval + 1f;
                }
            }
        }

        private void TickBossTargetOverride(CharacterBody body)
        {
            var ai = _master.GetComponent<BaseAI>();
            if (ai == null)
            {
                return;
            }

            _bossAggroSwitchTimer -= BossTargetOverrideInterval;
            if (_bossAggroSwitchTimer > 0f && _bossCurrentTarget != null &&
                _bossCurrentTarget.healthComponent != null && _bossCurrentTarget.healthComponent.alive)
            {
                ai.currentEnemy.gameObject = _bossCurrentTarget.gameObject;
                ai.currentEnemy.bestHurtBox = _bossCurrentTarget.mainHurtBox;
                ai.enemyAttention = BossAggroSwitchInterval + 1f;
                return;
            }

            _bossAggroSwitchTimer = BossAggroSwitchInterval;
            _bossCurrentTarget = SelectBossTarget(body);
            if (_bossCurrentTarget == null)
            {
                return;
            }

            ai.currentEnemy.gameObject = _bossCurrentTarget.gameObject;
            ai.currentEnemy.bestHurtBox = _bossCurrentTarget.mainHurtBox;
            ai.enemyAttention = BossAggroSwitchInterval + 1f;
        }

        private CharacterBody SelectBossTarget(CharacterBody self)
        {
            CharacterBody best = null;
            var bestScore = float.MinValue;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var playerBody = member ? member.body : null;
                if (!IsPlayerBody(playerBody))
                {
                    continue;
                }

                var distSqr = (playerBody.corePosition - self.corePosition).sqrMagnitude;
                var distance = Mathf.Sqrt(distSqr);

                var proximityScore = Mathf.Max(0f, 1f - distance / 100f) * 1.2f;
                var vulnerabilityScore = (1f - playerBody.healthComponent.combinedHealthFraction) * 2.0f;
                var damageScore = playerBody.damage / Mathf.Max(1f, self.damage) * 1.2f;

                var toEnemy = self.corePosition - playerBody.corePosition;
                toEnemy.y = 0f;
                var playerForward = playerBody.characterDirection ? playerBody.characterDirection.forward : Vector3.forward;
                playerForward.y = 0f;
                var facingAway = Vector3.Dot(playerForward.normalized, toEnemy.normalized);
                var exposedScore = facingAway * 0.8f;

                var healthPenaltyScore = 0f;
                if (_bossIsEnraged && playerBody.healthComponent.combinedHealthFraction < 0.5f)
                {
                    healthPenaltyScore = 1.5f;
                }

                var score = proximityScore + vulnerabilityScore + damageScore + exposedScore + healthPenaltyScore;

                if (_bossCurrentTarget != null && playerBody == _bossCurrentTarget)
                {
                    score -= 0.8f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = playerBody;
                }
            }

            return best;
        }

        private static bool IsPlayerBody(CharacterBody body)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return false;
            }

            if (body.isPlayerControlled)
            {
                return true;
            }

            var master = body.master;
            if (master != null && master.playerCharacterMasterController != null)
            {
                return true;
            }

            return false;
        }

        private void TickBossDodge(CharacterBody body, float deltaTime)
        {
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var playerBody = member ? member.body : null;
                if (playerBody == null || playerBody.healthComponent == null || !playerBody.healthComponent.alive)
                {
                    continue;
                }

                var toEnemy = body.corePosition - playerBody.corePosition;
                toEnemy.y = 0f;
                var distance = toEnemy.magnitude;
                if (distance > BossDodgeRange || distance < 1f)
                {
                    continue;
                }

                var playerForward = playerBody.characterDirection ? playerBody.characterDirection.forward : Vector3.forward;
                playerForward.y = 0f;
                var aimDot = Vector3.Dot(playerForward.normalized, toEnemy.normalized);

                if (aimDot > BossDodgeDotThreshold)
                {
                    var lateral = Vector3.Cross(Vector3.up, toEnemy.normalized);
                    var dodgeSign = Mathf.Sign(Mathf.Sin(Time.time * 4.5f + GetInstanceID()));
                    if (Random.value < 0.4f)
                    {
                        dodgeSign = -dodgeSign;
                    }

                    var dodgeAccel = _bossIsEnraged ? 80f : 65f;
                    var dodgeSpeed = _bossIsEnraged ? 2.4f : 2.0f;
                    ApplySteering(body, lateral * dodgeSign, dodgeAccel, dodgeSpeed, deltaTime);
                    return;
                }
            }
        }

        private void TickBossLeap(CharacterBody body)
        {
            if (_bossCurrentTarget == null || _bossCurrentTarget.healthComponent == null || !_bossCurrentTarget.healthComponent.alive)
            {
                return;
            }

            if (body.characterMotor != null && !body.characterMotor.isGrounded)
            {
                return;
            }

            var toTarget = _bossCurrentTarget.corePosition - body.corePosition;
            toTarget.y = 0f;
            var distance = toTarget.magnitude;

            if (distance < BossLeapMinDistance)
            {
                return;
            }

            var leapDirection = toTarget.normalized;
            var upForce = _bossIsEnraged ? 12f : 8f;
            var forwardForce = _bossIsEnraged ? 28f : 22f;

            if (body.characterMotor != null)
            {
                body.characterMotor.velocity = new Vector3(
                    leapDirection.x * forwardForce,
                    upForce,
                    leapDirection.z * forwardForce);
            }
            else if (body.rigidbody != null)
            {
                body.rigidbody.velocity = new Vector3(
                    leapDirection.x * forwardForce,
                    upForce,
                    leapDirection.z * forwardForce);
            }
        }

        private void TickBossSteering(CharacterBody body, float deltaTime)
        {
            var target = _bossCurrentTarget;
            if (target == null || target.healthComponent == null || !target.healthComponent.alive)
            {
                target = SelectBossTarget(body);
                _bossCurrentTarget = target;
            }

            if (target == null)
            {
                var objective = _owner.GetObjectivePositionForAI();
                var toObj = objective - body.corePosition;
                toObj.y = 0f;
                if (toObj.sqrMagnitude > 25f)
                {
                    ApplySteering(body, toObj, 30f * BossSteeringAccelMult, 1.3f * BossSteeringSpeedMult, deltaTime);
                }
                return;
            }

            var toTarget = target.corePosition - body.corePosition;
            toTarget.y = 0f;
            var distance = toTarget.magnitude;

            var accelMult = _bossIsEnraged ? BossSteeringAccelMult * 1.3f : BossSteeringAccelMult;
            var speedMult = _bossIsEnraged ? BossSteeringSpeedMult * 1.2f : BossSteeringSpeedMult;

            if (distance > 14f)
            {
                ApplySteering(body, toTarget, 42f * accelMult, 1.6f * speedMult, deltaTime);
                return;
            }

            if (distance > 6f)
            {
                var lateral = Vector3.Cross(Vector3.up, toTarget.normalized);
                var strafeSign = Mathf.Sign(Mathf.Sin(Time.time * 3.2f + GetInstanceID()));
                var orbitalDir = lateral * strafeSign + toTarget.normalized * 0.6f;
                ApplySteering(body, orbitalDir, 36f * accelMult, 1.4f * speedMult, deltaTime);
                return;
            }

            var closeLateral = Vector3.Cross(Vector3.up, toTarget.normalized);
            var closeSign = Mathf.Sign(Mathf.Sin(Time.time * 4.0f + GetInstanceID()));
            var closeDir = closeLateral * closeSign + toTarget.normalized * 0.3f;
            ApplySteering(body, closeDir, 32f * accelMult, 1.3f * speedMult, deltaTime);
        }

        #endregion

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

        #region 1 - BaseAI Target Override

        private void TickTargetOverride(CharacterBody body)
        {
            var ai = _master.GetComponent<BaseAI>();
            if (ai == null)
            {
                return;
            }

            var desiredTarget = SelectRoleTarget(body);
            if (desiredTarget == null)
            {
                return;
            }

            ai.currentEnemy.gameObject = desiredTarget.gameObject;
            ai.currentEnemy.bestHurtBox = desiredTarget.mainHurtBox;
            ai.enemyAttention = TargetOverrideInterval + 0.2f;
        }

        private CharacterBody SelectRoleTarget(CharacterBody self)
        {
            if (_isCommander)
            {
                return SelectCommanderTarget(self);
            }

            switch (_assignedRole)
            {
                case WarfrontRole.Hunter:
                    return _owner.GetHunterSquadTargetForAI();

                case WarfrontRole.Peeler:
                    return _owner.GetPeelerPriorityTargetForAI();

                case WarfrontRole.Flanker:
                    return SelectFlankerTarget(self);

                case WarfrontRole.Artillery:
                    return SelectArtilleryTarget(self);

                case WarfrontRole.Contester:
                    return SelectNearestPlayerToObjective();

                case WarfrontRole.Anchor:
                    return SelectNearestPlayerToObjective();

                default:
                    return null;
            }
        }

        private CharacterBody SelectCommanderTarget(CharacterBody self)
        {
            CharacterBody best = null;
            var bestScore = float.MinValue;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                var distSqr = (body.corePosition - self.corePosition).sqrMagnitude;
                var distance = Mathf.Sqrt(distSqr);
                var proximityScore = Mathf.Max(0f, 1f - distance / 80f);
                var vulnerabilityScore = (1f - body.healthComponent.combinedHealthFraction) * 1.5f;
                var damageScore = body.damage / Mathf.Max(1f, self.damage) * 0.8f;

                var toEnemy = self.corePosition - body.corePosition;
                toEnemy.y = 0f;
                var playerForward = body.characterDirection ? body.characterDirection.forward : Vector3.forward;
                playerForward.y = 0f;
                var facingAway = Vector3.Dot(playerForward.normalized, toEnemy.normalized);
                var exposedScore = facingAway * 0.6f;

                var score = proximityScore + vulnerabilityScore + damageScore + exposedScore;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = body;
                }
            }

            return best;
        }

        private CharacterBody SelectFlankerTarget(CharacterBody self)
        {
            CharacterBody best = null;
            var bestScore = float.MinValue;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                var toEnemy = self.corePosition - body.corePosition;
                toEnemy.y = 0f;
                var playerForward = body.characterDirection ? body.characterDirection.forward : Vector3.forward;
                playerForward.y = 0f;
                var facingAway = Vector3.Dot(playerForward.normalized, toEnemy.normalized);
                var score = facingAway + (1f - body.healthComponent.combinedHealthFraction) * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = body;
                }
            }

            return best;
        }

        private CharacterBody SelectArtilleryTarget(CharacterBody self)
        {
            var members = TeamComponent.GetTeamMembers(TeamIndex.Player);
            var playerCount = 0;
            CharacterBody singlePlayer = null;

            foreach (var member in members)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                playerCount++;
                singlePlayer = body;
            }

            if (playerCount == 0)
            {
                return null;
            }

            if (playerCount == 1)
            {
                return singlePlayer;
            }

            CharacterBody best = null;
            var bestNeighborCount = -1;
            foreach (var member in members)
            {
                var player = member ? member.body : null;
                if (player == null || player.healthComponent == null || !player.healthComponent.alive)
                {
                    continue;
                }

                var neighborCount = 0;
                foreach (var otherMember in members)
                {
                    var other = otherMember ? otherMember.body : null;
                    if (other == null || other == player || other.healthComponent == null || !other.healthComponent.alive)
                    {
                        continue;
                    }

                    if ((other.corePosition - player.corePosition).sqrMagnitude < 15f * 15f)
                    {
                        neighborCount++;
                    }
                }

                if (neighborCount > bestNeighborCount)
                {
                    bestNeighborCount = neighborCount;
                    best = player;
                }
            }

            return best;
        }

        private CharacterBody SelectNearestPlayerToObjective()
        {
            var objective = _owner.GetObjectivePositionForAI();
            CharacterBody nearest = null;
            var nearestDistSqr = float.MaxValue;
            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var body = member ? member.body : null;
                if (body == null || body.healthComponent == null || !body.healthComponent.alive)
                {
                    continue;
                }

                var distSqr = (body.corePosition - objective).sqrMagnitude;
                if (distSqr < nearestDistSqr)
                {
                    nearestDistSqr = distSqr;
                    nearest = body;
                }
            }

            return nearest;
        }

        #endregion

        #region 2 - AISkillDriver Tuning

        private void EnsureSkillDriversTuned()
        {
            if (_skillDriversTuned)
            {
                return;
            }

            _skillDriversTuned = true;
            var drivers = _master.GetComponents<AISkillDriver>();
            if (drivers == null || drivers.Length == 0)
            {
                return;
            }

            foreach (var driver in drivers)
            {
                if (driver == null)
                {
                    continue;
                }

                TuneSkillDriver(driver);
            }
        }

        private void TuneSkillDriver(AISkillDriver driver)
        {
            if (_isBoss)
            {
                driver.shouldSprint = true;
                if (driver.maxDistance > 0f && driver.maxDistance < 120f)
                {
                    driver.maxDistance = 120f;
                }

                if (driver.movementType == AISkillDriver.MovementType.ChaseMoveTarget)
                {
                    driver.movementType = AISkillDriver.MovementType.StrafeMovetarget;
                }

                driver.minDistance = 0f;
                return;
            }

            if (_isCommander)
            {
                driver.shouldSprint = true;
                if (driver.maxDistance > 0f && driver.maxDistance < 100f)
                {
                    driver.maxDistance = 100f;
                }

                if (driver.movementType == AISkillDriver.MovementType.ChaseMoveTarget)
                {
                    driver.movementType = AISkillDriver.MovementType.StrafeMovetarget;
                }

                return;
            }

            switch (_assignedRole)
            {
                case WarfrontRole.Artillery:
                    if (driver.minDistance < 12f)
                    {
                        driver.minDistance = 12f;
                    }

                    if (driver.maxDistance < 50f && driver.maxDistance > 0f)
                    {
                        driver.maxDistance = Mathf.Max(driver.maxDistance, 50f);
                    }

                    if (driver.movementType == AISkillDriver.MovementType.ChaseMoveTarget)
                    {
                        driver.movementType = AISkillDriver.MovementType.StrafeMovetarget;
                    }

                    break;

                case WarfrontRole.Hunter:
                    driver.minDistance = 0f;
                    if (driver.maxDistance > 0f && driver.maxDistance < 80f)
                    {
                        driver.maxDistance = 80f;
                    }

                    driver.shouldSprint = true;
                    break;

                case WarfrontRole.Flanker:
                    driver.shouldSprint = true;
                    if (driver.movementType == AISkillDriver.MovementType.ChaseMoveTarget)
                    {
                        driver.movementType = AISkillDriver.MovementType.StrafeMovetarget;
                    }

                    break;

                case WarfrontRole.Anchor:
                    driver.shouldSprint = false;
                    break;

                case WarfrontRole.Contester:
                    driver.shouldSprint = true;
                    break;

                case WarfrontRole.Peeler:
                    driver.shouldSprint = true;
                    break;
            }
        }

        #endregion

        #region 3 - Health-Aware Retreat

        private bool TickRetreat(CharacterBody body, float deltaTime)
        {
            if (_assignedRole == WarfrontRole.Anchor)
            {
                _isRetreating = false;
                return false;
            }

            var retreatThreshold = _assignedRole == WarfrontRole.Artillery ? 0.35f : 0.2f;

            if (!_isRetreating && body.healthComponent.combinedHealthFraction < retreatThreshold)
            {
                _isRetreating = true;
                _retreatTimer = 4f;
            }

            if (!_isRetreating)
            {
                return false;
            }

            _retreatTimer -= deltaTime;
            if (_retreatTimer <= 0f || body.healthComponent.combinedHealthFraction > retreatThreshold + 0.15f)
            {
                _isRetreating = false;
                return false;
            }

            var retreatTarget = _owner.GetNearestCommanderPositionForAI(body.corePosition);
            if (retreatTarget == Vector3.zero)
            {
                retreatTarget = _owner.GetObjectivePositionForAI() + (body.corePosition - _owner.GetObjectivePositionForAI()).normalized * 30f;
            }

            var toRetreat = retreatTarget - body.corePosition;
            toRetreat.y = 0f;
            ApplySteering(body, toRetreat, 38f, 1.6f, deltaTime);
            return true;
        }

        #endregion

        #region 4 - Dodge/Evasion

        private void TickDodge(CharacterBody body, float deltaTime)
        {
            if (!_isCommander && _assignedRole == WarfrontRole.Anchor)
            {
                return;
            }

            var dodgeDot = _isCommander ? CommanderDodgeDotThreshold : 0.85f;
            var dodgeRange = _isCommander ? CommanderDodgeRange : 25f;
            var dodgeAccel = _isCommander ? 60f : 45f;
            var dodgeSpeedMult = _isCommander ? 2.0f : 1.6f;

            var players = TeamComponent.GetTeamMembers(TeamIndex.Player);
            foreach (var member in players)
            {
                var playerBody = member ? member.body : null;
                if (playerBody == null || playerBody.healthComponent == null || !playerBody.healthComponent.alive)
                {
                    continue;
                }

                var toEnemy = body.corePosition - playerBody.corePosition;
                toEnemy.y = 0f;
                var distance = toEnemy.magnitude;
                if (distance > dodgeRange || distance < 1f)
                {
                    continue;
                }

                var playerForward = playerBody.characterDirection ? playerBody.characterDirection.forward : Vector3.forward;
                playerForward.y = 0f;
                var aimDot = Vector3.Dot(playerForward.normalized, toEnemy.normalized);

                if (aimDot > dodgeDot)
                {
                    var lateral = Vector3.Cross(Vector3.up, toEnemy.normalized);
                    var dodgeSign = Mathf.Sign(Mathf.Sin(Time.time * 3.7f + GetInstanceID()));
                    if (_isCommander && Random.value < 0.3f)
                    {
                        dodgeSign = -dodgeSign;
                    }

                    ApplySteering(body, lateral * dodgeSign, dodgeAccel, dodgeSpeedMult, deltaTime);
                    return;
                }
            }
        }

        #endregion

        #region 5 - Group Charge Coordination

        private void TickGroupCharge(CharacterBody body)
        {
            if (_assignedRole != WarfrontRole.Contester && _assignedRole != WarfrontRole.Anchor)
            {
                _groupChargeActive = false;
                return;
            }

            var objective = _owner.GetObjectivePositionForAI();
            var nearbyCount = _owner.CountRoleEnemiesNearPosition(objective, GroupChargeRadius, WarfrontRole.Contester)
                            + _owner.CountRoleEnemiesNearPosition(objective, GroupChargeRadius, WarfrontRole.Anchor);

            if (nearbyCount >= GroupChargeThreshold && !_groupChargeActive)
            {
                _groupChargeActive = true;
                body.AddTimedBuff(RoR2Content.Buffs.Warbanner, 4f);
                body.AddTimedBuff(RoR2Content.Buffs.CloakSpeed, 3f);
            }
            else if (nearbyCount < GroupChargeThreshold)
            {
                _groupChargeActive = false;
            }
        }

        #endregion

        #region 6 - Cooldown Exploitation

        internal void OnNearbyPlayerBigHit(CharacterBody body)
        {
            if (body == null || body.healthComponent == null || !body.healthComponent.alive)
            {
                return;
            }

            body.AddTimedBuff(RoR2Content.Buffs.Warbanner, CooldownExploitWindow);
            body.AddTimedBuff(RoR2Content.Buffs.WarCryBuff, CooldownExploitWindow * 0.8f);

            var ai = _master != null ? _master.GetComponent<BaseAI>() : null;
            if (ai != null)
            {
                ai.enemyAttention = 0f;
            }
        }

        #endregion

        #region 7 - Dynamic Role Switching

        private void TickRoleSwitch(CharacterBody body)
        {
            if (_owner == null)
            {
                return;
            }

            var objective = _owner.GetObjectivePositionForAI();
            var contesterCount = _owner.CountRoleEnemiesNearPosition(objective, 40f, WarfrontRole.Contester);
            var anchorCount = _owner.CountRoleEnemiesNearPosition(objective, 40f, WarfrontRole.Anchor);

            if (_assignedRole == WarfrontRole.Flanker && contesterCount < 1)
            {
                SwitchRole(WarfrontRole.Contester);
                return;
            }

            if (_assignedRole == WarfrontRole.Hunter && contesterCount < 1 && anchorCount < 1)
            {
                SwitchRole(WarfrontRole.Contester);
                return;
            }

            if (_assignedRole == WarfrontRole.Contester || _assignedRole == WarfrontRole.Peeler)
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

                if (playerCount > 1)
                {
                    center /= playerCount;

                    var allClumped = true;
                    foreach (var member in members)
                    {
                        var b = member ? member.body : null;
                        if (b == null || b.healthComponent == null || !b.healthComponent.alive)
                        {
                            continue;
                        }

                        if ((b.corePosition - center).sqrMagnitude > 12f * 12f)
                        {
                            allClumped = false;
                            break;
                        }
                    }

                    if (allClumped && contesterCount >= 2 && Random.value < 0.3f)
                    {
                        SwitchRole(WarfrontRole.Artillery);
                        return;
                    }
                }
            }
        }

        private void SwitchRole(WarfrontRole newRole)
        {
            if (newRole == _assignedRole)
            {
                return;
            }

            _assignedRole = newRole;
            _roleBuffApplied = false;
            _skillDriversTuned = false;
            _flankPoint = Vector3.zero;
            _isRetreating = false;
        }

        #endregion

        #region 8 - LOS Awareness for Artillery

        private bool HasLineOfSight(CharacterBody body, Vector3 target)
        {
            var origin = body.corePosition + Vector3.up * 0.5f;
            var direction = target - origin;
            var distance = direction.magnitude;
            if (distance < 1f)
            {
                return true;
            }

            return !Physics.Raycast(origin, direction.normalized, distance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }

        private Vector3 FindLOSPosition(CharacterBody body, Vector3 target, Vector3 currentPosition)
        {
            if (HasLineOfSight(body, target))
            {
                return currentPosition;
            }

            var toTarget = target - currentPosition;
            toTarget.y = 0f;
            var lateral = Vector3.Cross(Vector3.up, toTarget.normalized);

            for (var attempt = 0; attempt < 4; attempt++)
            {
                var sign = attempt % 2 == 0 ? 1f : -1f;
                var offset = lateral * sign * (6f + attempt * 4f);
                var candidate = currentPosition + offset;

                var tempBody = body;
                var originalPos = body.corePosition;
                var candidateOrigin = candidate + Vector3.up * 0.5f;
                var dir = target - candidateOrigin;
                if (!Physics.Raycast(candidateOrigin, dir.normalized, dir.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    return candidate;
                }
            }

            return currentPosition + toTarget.normalized * 5f;
        }

        #endregion

        #region Role Steering

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

            if (!HasLineOfSight(body, objective))
            {
                var losPos = FindLOSPosition(body, objective, body.corePosition);
                var toLos = losPos - body.corePosition;
                toLos.y = 0f;
                if (toLos.sqrMagnitude > 2f)
                {
                    ApplySteering(body, toLos, 28f, 1.3f, deltaTime);
                    return;
                }
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

        #endregion

        private void TickCommanderSteering(CharacterBody body, float deltaTime)
        {
            var target = SelectCommanderTarget(body);
            if (target == null)
            {
                TickContester(body, deltaTime);
                return;
            }

            var toTarget = target.corePosition - body.corePosition;
            toTarget.y = 0f;
            var distance = toTarget.magnitude;

            var accel = 40f * CommanderSteeringAccelMult;
            var speed = 1.55f * CommanderSteeringSpeedMult;

            if (distance > 12f)
            {
                ApplySteering(body, toTarget, accel, speed, deltaTime);
                return;
            }

            var lateral = Vector3.Cross(Vector3.up, toTarget.normalized);
            var strafeSign = Mathf.Sign(Mathf.Sin(Time.time * 2.4f + GetInstanceID()));
            var strafeDir = lateral * strafeSign + toTarget.normalized * 0.3f;
            ApplySteering(body, strafeDir, accel * 0.8f, speed * 0.9f, deltaTime);
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

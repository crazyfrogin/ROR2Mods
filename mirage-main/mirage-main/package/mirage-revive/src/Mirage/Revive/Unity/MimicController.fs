module Mirage.Revive.Unity.MimicController

open UnityEngine
open UnityEngine.AI
open Mirage.Revive.Domain.Logger

let [<Literal>] WalkSpeed = 3.5f
let [<Literal>] SprintSpeed = 6.0f

/// A component attached to a possession protocol mimic (host only) that applies
/// player-driven movement to the mimic's NavMeshAgent and animator.
[<AllowNullLiteral>]
type MimicController() =
    inherit MonoBehaviour()

    let mutable enemyAI: EnemyAI = null
    let mutable maskedEnemy: MaskedPlayerEnemy = null
    let mutable agent: NavMeshAgent = null
    let mutable creatureAnimator: Animator = null
    let mutable isControlled = false

    member this.Awake() =
        enemyAI <- this.GetComponent<EnemyAI>()
        maskedEnemy <- this.GetComponent<MaskedPlayerEnemy>()
        agent <- this.GetComponent<NavMeshAgent>()
        if not (isNull enemyAI) then
            creatureAnimator <- enemyAI.creatureAnimator

    member this.StartControlling() =
        isControlled <- true
        if not (isNull agent) then
            agent.updateRotation <- false
            agent.isStopped <- true
        logInfo "MimicController: mimic is now player-controlled."

    member this.StopControlling() =
        if isControlled then
            isControlled <- false
            if not (isNull agent) then
                agent.updateRotation <- true
                agent.isStopped <- false
            logInfo "MimicController: player control released."

    member _.IsControlled with get() = isControlled

    member this.ApplyMovement(moveX: float32, moveZ: float32, yRotation: float32, isSprinting: bool) =
        if isNull agent || isNull enemyAI || enemyAI.isEnemyDead then ()
        else
            // Apply rotation.
            let currentAngles = this.transform.eulerAngles
            this.transform.eulerAngles <- Vector3(currentAngles.x, yRotation, currentAngles.z)

            // Compute movement direction.
            let forward = this.transform.forward
            let right = this.transform.right
            let direction = (forward * moveZ + right * moveX).normalized
            let speed = if isSprinting then SprintSpeed else WalkSpeed
            let movement = direction * speed * Time.deltaTime

            // Move via NavMeshAgent.
            if agent.isOnNavMesh then
                agent.isStopped <- true
                ignore <| agent.Move movement

            // Set animator parameters for walk/run animations.
            let velocity = if direction.magnitude > 0.01f then speed else 0f
            if not (isNull creatureAnimator) then
                creatureAnimator.SetFloat("VelocityX", moveX * speed)
                creatureAnimator.SetFloat("VelocityZ", moveZ * speed)
                creatureAnimator.SetBool("Running", isSprinting && velocity > 0f)
                creatureAnimator.SetFloat("speedMultiplier", velocity / WalkSpeed)

            // Keep the masked enemy in the correct behaviour state for collision kills.
            if not (isNull maskedEnemy) then
                maskedEnemy.SetDestinationToPosition(this.transform.position, false) |> ignore

    member this.OnDestroy() =
        this.StopControlling()

module Mirage.Revive.Unity.MimicController

open GameNetcodeStuff
open UnityEngine
open UnityEngine.AI
open Unity.Netcode
open Mirage.Revive.Domain.Logger

let [<Literal>] private WalkSpeed = 3.5f
let [<Literal>] private SprintSpeed = 6.0f
let [<Literal>] private MouseSensitivity = 2.0f
let [<Literal>] private CameraSmoothSpeed = 15.0f

/// A component attached to a possession protocol mimic that gives the dead player
/// first-person control over the mimic's movement and camera.
[<AllowNullLiteral>]
type MimicController() =
    inherit NetworkBehaviour()

    let mutable enemyAI: EnemyAI = null
    let mutable maskedEnemy: MaskedPlayerEnemy = null
    let mutable agent: NavMeshAgent = null
    let mutable creatureAnimator: Animator = null

    let mutable controllingPlayer: PlayerControllerB = null
    let mutable headTransform: Transform = null

    let mutable cameraPitch = 0f
    let mutable isControlled = false

    let tryFindHeadTransform (root: Transform) =
        let transforms = root.GetComponentsInChildren<Transform>()
        transforms
            |> Array.tryFind (fun t -> t.name = "HeadPoint" || t.name = "Head" || t.name = "spine.004")
            |> Option.map (fun t -> t)
            |> Option.defaultWith (fun () ->
                // Fallback: use the enemy transform offset upward.
                null
            )

    member this.Awake() =
        enemyAI <- this.GetComponent<EnemyAI>()
        maskedEnemy <- this.GetComponent<MaskedPlayerEnemy>()
        agent <- this.GetComponent<NavMeshAgent>()
        creatureAnimator <- enemyAI.creatureAnimator

    member this.StartControlling(player: PlayerControllerB) =
        controllingPlayer <- player
        isControlled <- true
        headTransform <- tryFindHeadTransform this.transform
        cameraPitch <- 0f

        if not (isNull agent) then
            agent.updateRotation <- false
            agent.isStopped <- true

        logInfo $"MimicController: player #{player.playerClientId} now controlling mimic."

    member this.StopControlling() =
        if isControlled then
            isControlled <- false

            if not (isNull agent) then
                agent.updateRotation <- true
                agent.isStopped <- false

            controllingPlayer <- null
            logInfo "MimicController: player control released."

    member _.IsControlled with get() = isControlled
    member _.ControllingPlayer with get() = controllingPlayer

    member this.LateUpdate() =
        if not isControlled || isNull controllingPlayer then ()
        else
            let localPlayer =
                if isNull StartOfRound.Instance then null
                else StartOfRound.Instance.localPlayerController
            if isNull localPlayer || localPlayer <> controllingPlayer then ()
            else
                // Override the dead player's camera to first-person on the mimic's head.
                let camera = localPlayer.gameplayCamera
                if isNull camera then ()
                else
                    let targetPos =
                        if not (isNull headTransform) then
                            headTransform.position + Vector3(0f, 0.1f, 0f)
                        else
                            this.transform.position + Vector3(0f, 2.2f, 0f)
                    let targetRot = Quaternion.Euler(cameraPitch, this.transform.eulerAngles.y, 0f)
                    camera.transform.position <- Vector3.Lerp(camera.transform.position, targetPos, CameraSmoothSpeed * Time.deltaTime)
                    camera.transform.rotation <- Quaternion.Slerp(camera.transform.rotation, targetRot, CameraSmoothSpeed * Time.deltaTime)

    member this.Update() =
        if not isControlled || isNull controllingPlayer then ()
        else
            let localPlayer =
                if isNull StartOfRound.Instance then null
                else StartOfRound.Instance.localPlayerController
            if isNull localPlayer || localPlayer <> controllingPlayer then ()
            else
                // Read mouse look input.
                let mouseX = Input.GetAxis("Mouse X") * MouseSensitivity
                let mouseY = Input.GetAxis("Mouse Y") * MouseSensitivity
                cameraPitch <- Mathf.Clamp(cameraPitch - mouseY, -80f, 80f)

                // Read movement input.
                let moveX = Input.GetAxis("Horizontal")
                let moveZ = Input.GetAxis("Vertical")
                let isSprinting = Input.GetKey(KeyCode.LeftShift)
                let yRotation = this.transform.eulerAngles.y + mouseX

                if this.IsHost then
                    this.ApplyMovement(moveX, moveZ, yRotation, isSprinting)
                else
                    this.MoveServerRpc(moveX, moveZ, yRotation, isSprinting)

    member private this.ApplyMovement(moveX, moveZ, yRotation, isSprinting) =
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

    [<ServerRpc(RequireOwnership = false)>]
    member this.MoveServerRpc(moveX: float32, moveZ: float32, yRotation: float32, isSprinting: bool) =
        this.ApplyMovement(moveX, moveZ, yRotation, isSprinting)

    override this.OnDestroy() =
        this.StopControlling()
        base.OnDestroy()

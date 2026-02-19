module Mirage.Revive.Unity.BodyDeactivator

open UnityEngine
open Unity.Netcode
open GameNetcodeStuff
open System.Threading.Tasks
open Mirage.Revive.Domain.Logger
open Mirage.Revive.Domain.Possession
open Mirage.Revive.Unity.MimicController

/// A component that should be attached to a __PlayerControllerB__, reviving the player as a masked enemy.
[<AllowNullLiteral>]
type BodyDeactivator () =
    inherit NetworkBehaviour()

    member private this.TryResolveEnemy(reference: NetworkObjectReference, retries) =
        task {
            let mutable enemyNetworkObject = null
            if reference.TryGet &enemyNetworkObject then
                let player = this.GetComponent<PlayerControllerB>()
                setActiveMimic player.playerClientId enemyNetworkObject
                this.DeactivateBody <| enemyNetworkObject.GetComponent<EnemyAI>()
            elif retries > 0 then
                do! Task.Delay 50
                do! this.TryResolveEnemy(reference, retries - 1)
            else
                logError "DeactivateBodyClientRpc failed to resolve enemy network object after retries."
        }

    /// Deactivate the player's dead body and redirect the enemy to the player.
    member this.DeactivateBody(enemy) =
        let player = this.GetComponent<PlayerControllerB>()
        player.redirectToEnemy <- enemy
        if not (isNull player.deadBody) then
            player.deadBody.DeactivateBody false
        if this.IsHost then
            let enemyNetworkObject = enemy.GetComponentInChildren<NetworkObject>()
            if not (isNull enemyNetworkObject) then
                setActiveMimic player.playerClientId enemyNetworkObject
                this.DeactivateBodyClientRpc enemyNetworkObject
            else
                logError "DeactivateBody failed to find enemy network object."

    [<ClientRpc>]
    member this.DeactivateBodyClientRpc(reference: NetworkObjectReference) =
        if not this.IsHost then
            ignore <| this.TryResolveEnemy(reference, 20)

    [<ServerRpc(RequireOwnership = false)>]
    member _.MimicMoveServerRpc(moveX: float32, moveZ: float32, yRotation: float32, isSprinting: bool) =
        let mimics = Object.FindObjectsOfType<MaskedPlayerEnemy>()
        for masked in mimics do
            let controller = masked.GetComponent<MimicController>()
            if not (isNull controller) && controller.IsControlled then
                controller.ApplyMovement(moveX, moveZ, yRotation, isSprinting)
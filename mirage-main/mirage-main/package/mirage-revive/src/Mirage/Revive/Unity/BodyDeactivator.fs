module Mirage.Revive.Unity.BodyDeactivator

open System.Collections
open UnityEngine
open Unity.Netcode
open GameNetcodeStuff
open Mirage.Revive.Domain.Logger
open Mirage.Revive.Domain.Possession
open Mirage.Revive.Unity.MimicController

/// A component that should be attached to a __PlayerControllerB__, reviving the player as a masked enemy.
[<AllowNullLiteral>]
type BodyDeactivator () =
    inherit NetworkBehaviour()

    member private this.TryResolveEnemyCoroutine(reference: NetworkObjectReference, retries: int) =
        seq {
            let mutable enemyNetworkObject = null
            let mutable remaining = retries
            while remaining > 0 && not (reference.TryGet &enemyNetworkObject) do
                remaining <- remaining - 1
                yield WaitForSeconds 0.05f :> obj
            if not (isNull enemyNetworkObject) then
                let player = this.GetComponent<PlayerControllerB>()
                setActiveMimic player.playerClientId enemyNetworkObject
                this.DeactivateBody <| enemyNetworkObject.GetComponent<EnemyAI>()
            else
                logError "DeactivateBodyClientRpc failed to resolve enemy network object after retries."
        } :?> IEnumerator

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
            this.StartCoroutine(this.TryResolveEnemyCoroutine(reference, 20)) |> ignore

    [<ServerRpc(RequireOwnership = false)>]
    member _.MimicMoveServerRpc(moveX: float32, moveZ: float32, yRotation: float32, isSprinting: bool) =
        let mimics = Object.FindObjectsOfType<MaskedPlayerEnemy>()
        for masked in mimics do
            let controller = masked.GetComponent<MimicController>()
            if not (isNull controller) && controller.IsControlled then
                controller.ApplyMovement(moveX, moveZ, yRotation, isSprinting)
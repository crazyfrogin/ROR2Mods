module Mirage.Revive.Unity.BodyDeactivator

open Unity.Netcode
open GameNetcodeStuff
open Mirage.Revive.Domain.Logger
open Mirage.Revive.Domain.Possession

/// A component that should be attached to a __PlayerControllerB__, reviving the player as a masked enemy.
type BodyDeactivator () =
    inherit NetworkBehaviour()

    /// Deactivate the player's dead body and redirect the enemy to the player.
    member this.DeactivateBody(enemy) =
        let player = this.GetComponent<PlayerControllerB>()
        if not (isNull player.deadBody) then
            player.redirectToEnemy <- enemy
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
            let mutable enemy = null
            if reference.TryGet &enemy then
                let player = this.GetComponent<PlayerControllerB>()
                setActiveMimic player.playerClientId enemy
                this.DeactivateBody <| enemy.GetComponent<EnemyAI>()
            else
                logError "DeactivateBodyClientRpc received an invalid network object reference."
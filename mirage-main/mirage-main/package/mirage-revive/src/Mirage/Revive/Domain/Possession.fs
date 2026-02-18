module Mirage.Revive.Domain.Possession

open System.Collections.Generic
open Unity.Netcode

let private queuedPlayers = Queue<uint64>()
let private queuedPlayerSet = HashSet<uint64>()
let private processedDeaths = HashSet<uint64>()

let mutable private activeOwnerId: Option<uint64> = None
let mutable private activeEnemyId: Option<uint64> = None

let reset () =
    queuedPlayers.Clear()
    queuedPlayerSet.Clear()
    processedDeaths.Clear()
    activeOwnerId <- None
    activeEnemyId <- None

/// Returns true when this player's death has not been processed for the current round yet.
let markDeathProcessed playerId = processedDeaths.Add playerId

let clearProcessedDeath playerId = ignore <| processedDeaths.Remove playerId

let setActiveMimic ownerId (enemyNetworkObject: NetworkObject) =
    activeOwnerId <- Some ownerId
    activeEnemyId <- Some enemyNetworkObject.NetworkObjectId

let clearActiveMimic () =
    activeOwnerId <- None
    activeEnemyId <- None

let tryGetActiveOwnerId () = activeOwnerId

let hasActiveMimic () = activeOwnerId.IsSome && activeEnemyId.IsSome

let isActiveMimicNetworkId networkObjectId = Option.exists ((=) networkObjectId) activeEnemyId

let isActiveMimicEnemy (enemyNetworkObject: NetworkObject) =
    not (isNull enemyNetworkObject)
        && isActiveMimicNetworkId enemyNetworkObject.NetworkObjectId

let enqueue playerId =
    if queuedPlayerSet.Add playerId then
        queuedPlayers.Enqueue playerId
        true
    else
        false

let tryDequeueNext () =
    if queuedPlayers.Count = 0 then
        None
    else
        let playerId = queuedPlayers.Dequeue()
        ignore <| queuedPlayerSet.Remove playerId
        Some playerId

let queueLength () = queuedPlayers.Count

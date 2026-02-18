module Mirage.Revive.Hook.MaskedPlayerEnemy

open System
open System.Collections
open System.IO
open System.Reflection
open System.Diagnostics
open System.Threading.Tasks
open GameNetcodeStuff
open UnityEngine
open UnityEngine.AI
open Unity.Netcode
open Mirage.Revive.Domain.Config
open Mirage.Revive.Domain.Possession
open Mirage.Revive.Unity.BodyDeactivator
open Mirage.Revive.Domain.Logger

let private random = Random()

let mutable private maskedEnemyPrefab = null

let [<Literal>] private reflectionFlags = BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance
let private recordingDirectory =
    let baseDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
    Path.Join(baseDirectory, "Mirage", "Recording")

let private tryGetEnemyNetworkObject (enemy: EnemyAI) =
    if isNull enemy then None
    else
        let networkObject = enemy.GetComponentInChildren<NetworkObject>()
        if isNull networkObject then None else Some networkObject

let private isActiveProtocolMimic (enemy: EnemyAI) =
    match tryGetEnemyNetworkObject enemy with
        | Some networkObject -> isActiveMimicEnemy networkObject
        | None -> false

let private tryFindActiveProtocolMimic () =
    Object.FindObjectsOfType<MaskedPlayerEnemy>()
        |> Seq.tryFind (fun maskedEnemy ->
            let enemy = maskedEnemy.GetComponent<EnemyAI>()
            isActiveProtocolMimic enemy
        )

let private synchronizeActiveMimicState () =
    if hasActiveMimic() && Option.isNone (tryFindActiveProtocolMimic()) then
        clearActiveMimic()

let private isFiniteVector3 (position: Vector3) =
    not (Single.IsNaN position.x || Single.IsNaN position.y || Single.IsNaN position.z)
        && not (Single.IsInfinity position.x || Single.IsInfinity position.y || Single.IsInfinity position.z)

let private onNavMesh position =
    let mutable meshHit = NavMeshHit()
    NavMesh.SamplePosition(position, &meshHit, 6f, NavMesh.AllAreas)

let private resolveSafeRespawnPosition (preferredPosition: Vector3) (owner: PlayerControllerB) =
    let inShip = owner.isInHangarShipRoom && StartOfRound.Instance.shipHasLanded
    if isFiniteVector3 preferredPosition && (inShip || onNavMesh preferredPosition) then
        preferredPosition
    else
        owner.transform.position

let private tryGetFieldValue<'A> (target: obj) fieldName =
    let field = target.GetType().GetField(fieldName, reflectionFlags)
    if isNull field then None
    else
        match field.GetValue target with
            | :? 'A as value -> Some value
            | _ -> None

let private tryInvokeNoArgMethod (target: obj) methodName =
    let method = target.GetType().GetMethod(methodName, reflectionFlags, null, Type.EmptyTypes, null)
    if isNull method then false
    else
        try
            ignore <| method.Invoke(target, [||])
            true
        with | _ -> false

let private trySetMemberValue (target: obj) memberName (value: obj) =
    let mutable didSet = false
    let field = target.GetType().GetField(memberName, reflectionFlags)
    if not (isNull field) then
        try
            field.SetValue(target, value)
            didSet <- true
        with | _ -> ()
    if not didSet then
        let property = target.GetType().GetProperty(memberName, reflectionFlags)
        if not (isNull property) && property.CanWrite then
            try
                property.SetValue(target, value)
                didSet <- true
            with | _ -> ()
    didSet

let private tryTeleportPlayer (player: PlayerControllerB) (position: Vector3) =
    let methods =
        player
            .GetType()
            .GetMethods(reflectionFlags)
            |> Array.filter (fun method -> method.Name = "TeleportPlayer")
    let mutable didTeleport = false
    for method in methods do
        if not didTeleport then
            let parameters = method.GetParameters()
            if parameters.Length > 0 && parameters[0].ParameterType = typeof<Vector3> then
                let args = Array.zeroCreate<obj> parameters.Length
                args[0] <- box position
                for index in 1 .. parameters.Length - 1 do
                    args[index] <-
                        let parameterType = parameters[index].ParameterType
                        if parameterType = typeof<bool> then box false
                        elif parameterType = typeof<int> then box 0
                        elif parameterType = typeof<float32> then box 0f
                        elif parameterType = typeof<float> then box 0.0
                        elif parameterType.IsValueType then Activator.CreateInstance parameterType
                        else null
                try
                    ignore <| method.Invoke(player, args)
                    didTeleport <- true
                with | _ -> ()
    if not didTeleport then
        player.transform.position <- position

let private revivePlayerAt (owner: PlayerControllerB) killPosition =
    let respawnPosition = resolveSafeRespawnPosition killPosition owner
    owner.redirectToEnemy <- null
    owner.deadBody <- null
    ignore <| trySetMemberValue (owner :> obj) "isPlayerDead" (box false)
    ignore <| trySetMemberValue (owner :> obj) "isPlayerControlled" (box true)
    ignore <| trySetMemberValue (owner :> obj) "inSpecialInteractAnimation" (box false)
    ignore <| trySetMemberValue (owner :> obj) "disableMoveInput" (box false)
    ignore <| trySetMemberValue (owner :> obj) "disableLookInput" (box false)
    ignore <| trySetMemberValue (owner :> obj) "health" (box 100)
    ignore <| tryInvokeNoArgMethod owner "ResetPlayerBloodObjects"
    ignore <| tryInvokeNoArgMethod owner "CancelSpecialTriggerAnimations"
    ignore <| tryInvokeNoArgMethod owner "SpawnPlayerAnimation"
    tryTeleportPlayer owner respawnPosition
    owner.transform.position <- respawnPosition

let private suppressProtocolDrops (enemy: MaskedPlayerEnemy) =
    let maskedAnimator = enemy.GetComponent("Mirage.Unity.MaskedAnimator")
    if isNull maskedAnimator then ()
    else
        let field = maskedAnimator.GetType().GetField("dropItemOnDeath", reflectionFlags)
        if not (isNull field) then
            field.SetValue(maskedAnimator, box false)

let private canSpawnFromPosition (player: PlayerControllerB) =
    let inShip = player.isInHangarShipRoom && StartOfRound.Instance.shipHasLanded
    inShip || onNavMesh player.transform.position

let private spawnLegacyMaskedEnemy (player: PlayerControllerB) causeOfDeath spawnBody bodyVelocity =
    let playerKilledByMaskItem =
        causeOfDeath = int CauseOfDeath.Suffocation
            && spawnBody
            && bodyVelocity.Equals Vector3.zero
    let config = getConfig()
    let isPlayerAloneAndRequired = not config.reviveOnlyWhenPlayerAlone || player.isPlayerAlone
    let spawnRateSuccess () = random.Next(1, 101) <= config.reviveChance
    if canSpawnFromPosition player
        && not playerKilledByMaskItem
        && spawnBody
        && isPlayerAloneAndRequired
        && spawnRateSuccess()
    then
        let rotationY = player.transform.eulerAngles.y
        let maskedEnemy =
            Object.Instantiate<GameObject>(
                maskedEnemyPrefab,
                player.transform.position,
                Quaternion.Euler <| Vector3(0f, rotationY, 0f)
            )
        let enemyAI = maskedEnemy.GetComponent<MaskedPlayerEnemy>()
        let networkObject = maskedEnemy.GetComponentInChildren<NetworkObject>()
        if isNull enemyAI || isNull networkObject then
            false
        else
            enemyAI.mimickingPlayer <- player
            networkObject.Spawn(destroyWithScene = true)
            player.GetComponent<BodyDeactivator>().DeactivateBody enemyAI
            true
    else
        false

let private spawnProtocolMimic (player: PlayerControllerB) =
    if not <| canSpawnFromPosition player then
        logWarning $"Possession Protocol: could not spawn mimic for player #{player.playerClientId}; invalid spawn position."
        false
    else
        let rotationY = player.transform.eulerAngles.y
        let maskedEnemy =
            Object.Instantiate<GameObject>(
                maskedEnemyPrefab,
                player.transform.position,
                Quaternion.Euler <| Vector3(0f, rotationY, 0f)
            )
        let enemyAI = maskedEnemy.GetComponent<MaskedPlayerEnemy>()
        let networkObject = maskedEnemy.GetComponentInChildren<NetworkObject>()
        if isNull enemyAI || isNull networkObject then
            logWarning $"Possession Protocol: failed to initialize mimic for player #{player.playerClientId}."
            false
        else
            enemyAI.mimickingPlayer <- player
            networkObject.Spawn(destroyWithScene = true)
            setActiveMimic player.playerClientId networkObject
            player.GetComponent<BodyDeactivator>().DeactivateBody enemyAI
            logInfo $"Possession Protocol: player #{player.playerClientId} is now the active mimic. Queue length: {queueLength()}"
            true

let rec private activateNextQueuedMimic () =
    match tryDequeueNext() with
        | None ->
            logInfo "Possession Protocol: active mimic slot is now empty."
        | Some playerId ->
            let round = StartOfRound.Instance
            if isNull round || int playerId > round.connectedPlayersAmount then
                activateNextQueuedMimic()
            else
                let player = round.allPlayerScripts[int playerId]
                if isNull player || player.disconnectedMidGame || not player.isPlayerDead then
                    activateNextQueuedMimic()
                elif not <| spawnProtocolMimic player then
                    activateNextQueuedMimic()

let private tryGetKillingEnemy (player: PlayerControllerB) =
    let candidateFields =
        [| "inAnimationWithEnemy"
           "inAnimationWithEnemyScript"
           "inAnimationWithEnemyAI"
           "killingEnemy"
           "redirectToEnemy" |]
    candidateFields
        |> Array.tryPick (tryGetFieldValue<EnemyAI> (player :> obj))

let private countAlivePlayers () =
    if isNull StartOfRound.Instance then
        0
    else
        StartOfRound.Instance.allPlayerScripts
            |> Seq.filter (fun player -> not (isNull player) && not player.isPlayerDead && not player.disconnectedMidGame)
            |> Seq.length

let private tryPickRecording () =
    try
        if Directory.Exists recordingDirectory then
            let recordings = Directory.GetFiles(recordingDirectory, "*.opus")
            if recordings.Length = 0 then None
            else Some recordings[random.Next(recordings.Length)]
        else
            None
    with | _ -> None

let private tryStreamClipFromEnemy (enemy: EnemyAI) filePath =
    let audioStreamType = Type.GetType("Mirage.Unity.AudioStream, Mirage")
    if isNull audioStreamType then
        false
    else
        let audioStreamComponent = enemy.GetComponent(audioStreamType)
        let method =
            if isNull audioStreamComponent then null
            else audioStreamComponent.GetType().GetMethod("StreamOpusFromFile", [| typeof<string> |])
        if isNull method then
            false
        else
            try
                match method.Invoke(audioStreamComponent, [| filePath :> obj |]) with
                    | :? ValueTask as task -> ignore <| task.AsTask()
                    | :? Task as task -> ignore task
                    | _ -> ()
                true
            with | error ->
                logWarning $"Possession Protocol: failed to stream voice clip. Error: {error}"
                false

let private tryUseMimicVoiceAbility (player: PlayerControllerB) =
    if not player.isPlayerDead || isNull player.redirectToEnemy then
        ()
    elif getConfig().enablePossessionProtocol && isActiveProtocolMimic player.redirectToEnemy then
        match tryPickRecording() with
            | None -> ()
            | Some recording ->
                let enemy = player.redirectToEnemy
                ignore <| tryStreamClipFromEnemy enemy recording

let private onActiveMimicKill (victim: PlayerControllerB) =
    match tryGetActiveOwnerId() with
        | None -> ()
        | Some ownerId ->
            let owner =
                if isNull StartOfRound.Instance || int ownerId > StartOfRound.Instance.connectedPlayersAmount then null
                else StartOfRound.Instance.allPlayerScripts[int ownerId]
            if not (isNull owner) then
                revivePlayerAt owner victim.transform.position
                clearProcessedDeath ownerId
            else
                logWarning $"Possession Protocol: cash-out owner #{ownerId} is not available to revive."
    tryFindActiveProtocolMimic()
        |> Option.iter (fun activeMimic ->
            let networkObject = activeMimic.GetComponentInChildren<NetworkObject>()
            if not (isNull networkObject) && networkObject.IsSpawned then
                networkObject.Despawn(true)
            else
                Object.Destroy activeMimic.gameObject
        )
    clearActiveMimic()
    activateNextQueuedMimic()

let private processPlayerDeath (player: PlayerControllerB) causeOfDeath deathAnimation spawnBody bodyVelocity alivePlayersBeforeDeath killerEnemy =
    if not <| markDeathProcessed player.playerClientId then
        ()
    elif not (getConfig().enablePossessionProtocol) then
        ignore <| spawnLegacyMaskedEnemy player causeOfDeath spawnBody bodyVelocity
    else
        let killedByActiveMimic = Option.exists isActiveProtocolMimic killerEnemy
        let isLastAlive = alivePlayersBeforeDeath <= 1
        let playerKilledByMaskItem =
            causeOfDeath = int CauseOfDeath.Suffocation
                && spawnBody
                && bodyVelocity.Equals Vector3.zero

        if killedByActiveMimic then
            logInfo $"Possession Protocol: active mimic killed player #{player.playerClientId}. Cash-out triggered."
            onActiveMimicKill player
        elif isLastAlive then
            logInfo $"Possession Protocol: player #{player.playerClientId} died as last alive. Not eligible."
        elif playerKilledByMaskItem || not spawnBody then
            ()
        elif hasActiveMimic() then
            if enqueue player.playerClientId then
                logInfo $"Possession Protocol: queued player #{player.playerClientId}. Queue length: {queueLength()}"
        else
            if not <| spawnProtocolMimic player then
                if enqueue player.playerClientId then
                    logWarning $"Possession Protocol: failed immediate spawn for player #{player.playerClientId}; added to queue instead."

let revivePlayersOnDeath () =
    On.GameNetcodeStuff.PlayerControllerB.add_Awake(fun orig self -> 
        orig.Invoke self
        ignore <| self.gameObject.AddComponent<BodyDeactivator>()
    )

    // Save the haunted mask item prefab.
    On.GameNetworkManager.add_Start(fun orig self ->
        orig.Invoke self
        for prefab in self.GetComponent<NetworkManager>().NetworkConfig.Prefabs.m_Prefabs do
            let maskedEnemy = prefab.Prefab.gameObject.GetComponent<MaskedPlayerEnemy>()
            // enemyName must be matched to avoid mods that extend from MaskedPlayerEnemy.
            if not <| isNull maskedEnemy && maskedEnemy.enemyType.enemyName = "Masked" then
                maskedEnemyPrefab <- maskedEnemy.gameObject
        if isNull maskedEnemyPrefab then
            logWarning "HauntedMaskItem prefab is missing. Another mod is messing with this prefab when they shouldn't be."
    )

    // Reset protocol state in between rounds.
    On.StartOfRound.add_StartGame(fun orig self ->
        orig.Invoke self
        reset()
    )

    // Spawn a masked enemy on player death.
    On.GameNetcodeStuff.PlayerControllerB.add_KillPlayerServerRpc(fun orig self playerId spawnBody bodyVelocity causeOfDeath deathAnimation positionOffset ->
        let alivePlayersBeforeDeath = if self.IsHost then countAlivePlayers() else 0
        let killerEnemy = if self.IsHost then tryGetKillingEnemy self else None
        orig.Invoke(self, playerId, spawnBody, bodyVelocity, causeOfDeath, deathAnimation, positionOffset)
        let killerEnemy =
            if killerEnemy.IsSome then killerEnemy
            elif self.IsHost then tryGetKillingEnemy self
            else None
        if self.IsHost && self <> StartOfRound.Instance.localPlayerController then
            processPlayerDeath self causeOfDeath deathAnimation spawnBody bodyVelocity alivePlayersBeforeDeath killerEnemy
    )
    On.GameNetcodeStuff.PlayerControllerB.add_KillPlayer(fun orig self bodyVelocity spawnBody causeOfDeath deathAnimation positionOffset ->
        let alivePlayersBeforeDeath = if self.IsHost then countAlivePlayers() else 0
        let killerEnemy = if self.IsHost then tryGetKillingEnemy self else None
        orig.Invoke(self, bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset)
        let killerEnemy =
            if killerEnemy.IsSome then killerEnemy
            elif self.IsHost then tryGetKillingEnemy self
            else None
        if self.IsHost && self = StartOfRound.Instance.localPlayerController then
            processPlayerDeath self (int causeOfDeath) deathAnimation spawnBody bodyVelocity alivePlayersBeforeDeath killerEnemy
    )

    On.MaskedPlayerEnemy.add_KillEnemy(fun orig self destroy ->
        let activeOnThisClient = getConfig().enablePossessionProtocol && isActiveProtocolMimic (self.GetComponent<EnemyAI>())
        let isHostActiveMimic = self.IsHost && activeOnThisClient
        if isHostActiveMimic then
            suppressProtocolDrops self
        orig.Invoke(self, destroy)
        if activeOnThisClient then
            clearActiveMimic()
        if isHostActiveMimic then
            logInfo "Possession Protocol: active mimic died before cash-out. Advancing queue."
            activateNextQueuedMimic()
    )

    On.GameNetcodeStuff.PlayerControllerB.add_Update(fun orig self ->
        orig.Invoke self
        let localPlayer =
            if isNull StartOfRound.Instance then null
            else StartOfRound.Instance.localPlayerController
        if self = localPlayer then
            synchronizeActiveMimicState()
            if Input.GetMouseButtonDown(0) then
                tryUseMimicVoiceAbility self
    )

    On.GameNetworkManager.add_StartDisconnect(fun orig self ->
        orig.Invoke self
        reset()
    )

    // After a round is over, the player's dead body is still set.
    // This causes the teleporter to attempt to move the dead body, which always fails.
    // The following hook fixes this by ensuring deadBody is null in between rounds.
    On.StartOfRound.add_EndOfGame(fun orig self bodiesEnsured connectedPlayersOnServer scrapCollected ->
        seq {
            yield orig.Invoke(self, bodiesEnsured, connectedPlayersOnServer, scrapCollected)
            reset()
            for player in self.allPlayerScripts do
                player.deadBody <- null
        } :?> IEnumerator
    )
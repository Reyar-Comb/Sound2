using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Sound2.LevelEditing
{
    [Serializable]
    public sealed class ElevatorConnection
    {
        public LevelElevator source, destination;
        public bool bidirectional;
        [Min(.1f)] public float travelSeconds = 3;
    }
    [DefaultExecutionOrder(-400),DisallowMultipleComponent]
    public sealed class LevelWorld : MonoBehaviour
    {
        public PlayerController player;
        public List<LevelLayout> levels = new();
        public List<ElevatorConnection> connections = new();
        public LevelLayout startingLevel;
        public Transform startingPoint;
        public FloorProgressionProfile profile;
        public bool activateOnlyCurrentLevel = true;
        public bool roomAcousticsEnabled = true;
        [Min(0), Tooltip("电梯开始切换时，出发关卡的 2D BGM（含初始房间音乐）淡出的秒数。取消切换时平滑恢复。")]
        public float elevatorMusicFadeSeconds = 1.5f;
        [Min(.1f), Tooltip("Main-thread activation budget per frame while the elevator travels (milliseconds). A single prop cannot be interrupted.")]
        public float activationBudgetMs = 2;
        [Min(1), Tooltip("Maximum props activated per frame; also spreads Start callbacks across frames.")]
        public int activationBatchSize = 24;
        public LevelLayout CurrentLevel { get; private set; }
        public LevelElevator ArrivalCheckpoint { get; private set; }
        public bool Travelling { get; private set; }

        LevelElevator departure, arrival;
        PlayerController teleportPassenger;
        LevelActivation preparation;
        Transform resetTemplates;
        sealed class InitialLevel
        {
            public LevelLayout template;
            public Dictionary<Transform, Transform> originals;
        }
        readonly Dictionary<LevelLayout, InitialLevel> initialLevels = new();
        readonly HashSet<AsyncInstantiateOperation<LevelLayout>> pendingCopies = new();
        readonly Dictionary<AudioSource, float> departingMusic = new();
        float departureMusicGain = 1, departureMusicFrom = 1, departureMusicTarget = 1, departureMusicTime;

        void BeginDepartureMusic()
        {
            // A previous cancelled trip may still be fading back in. Preserve authored gains.
            bool resumingFade = departingMusic.Count > 0;
            void Capture(AudioSource source)
            {
                if (source != null && source.isPlaying && source.spatialBlend <= .001f
                    && source.GetComponent<SpatialAudioEmitter>() == null)
                    departingMusic.TryAdd(source, source.volume);
            }
            if (CurrentLevel != null)
                foreach (var source in CurrentLevel.GetComponentsInChildren<AudioSource>()) Capture(source);
            foreach (var opening in FindObjectsByType<SoundShellOpening>())
                if (opening.world == this && opening.InStartingRoom)
                    foreach (var source in opening.roomMusic.Sources) Capture(source);
            departureMusicFrom = resumingFade ? departureMusicGain : 1;
            departureMusicGain = departureMusicFrom;
            departureMusicTarget = 0; departureMusicTime = 0;
        }

        void LateUpdate()
        {
            if (departingMusic.Count == 0 || SonicSpaceStartMenu.BlocksGameplay || AudioListener.pause) return;
            departureMusicTime += Time.deltaTime;
            float t = elevatorMusicFadeSeconds <= 0 ? 1 : Mathf.Clamp01(departureMusicTime / elevatorMusicFadeSeconds);
            departureMusicGain = Mathf.Lerp(departureMusicFrom, departureMusicTarget, Mathf.SmoothStep(0, 1, t));
            foreach (var pair in departingMusic)
                if (pair.Key != null) pair.Key.volume = pair.Value * departureMusicGain;
            if (t >= 1 && departureMusicTarget == 1) RestoreDepartureMusic(false);
        }

        void RestoreDepartureMusic(bool stop)
        {
            foreach (var pair in departingMusic)
                if (pair.Key != null)
                {
                    if (stop) pair.Key.Stop();
                    pair.Key.volume = pair.Value;
                }
            departingMusic.Clear();
        }

        static Dictionary<Transform, Transform> MatchHierarchy(Transform source, Transform target)
        {
            var result = new Dictionary<Transform, Transform>();
            void Match(Transform a, Transform b)
            {
                result[a] = b;
                for (int i = 0; i < a.childCount; i++) Match(a.GetChild(i), b.GetChild(i));
            }
            Match(source, target);
            return result;
        }

        void CaptureInitialLevel(LevelLayout level)
        {
            if (level == null || initialLevels.ContainsKey(level)) return;
            EnsureTemplateRoot();
            // Clone while inactive, before any gameplay callbacks mutate the authored state.
            var template = Instantiate(level, resetTemplates, false);
            initialLevels[level] = new InitialLevel
            {
                template = template,
                originals = MatchHierarchy(level.transform, template.transform)
            };
        }

        // Keep the destination inactive until the complete snapshot (including cross-prop
        // references) exists. Unity copies the hierarchy asynchronously; no WaitForCompletion.
        IEnumerator CaptureInitialLevelAsync(LevelLayout level)
        {
            if (level == null || initialLevels.ContainsKey(level)) yield break;
            EnsureTemplateRoot();
            var copy = InstantiateAsync(level, new InstantiateParameters { parent = resetTemplates, worldSpace = false });
            pendingCopies.Add(copy);
            try
            {
                yield return copy;
                if (level == null) { if (copy.Result.Length > 0) Destroy(copy.Result[0].gameObject); yield break; }
                initialLevels[level] = new InitialLevel
                {
                    template = copy.Result[0],
                    originals = MatchHierarchy(level.transform, copy.Result[0].transform)
                };
            }
            finally { pendingCopies.Remove(copy); if (!copy.isDone) copy.Cancel(); }
        }

        void EnsureTemplateRoot()
        {
            if (resetTemplates != null) return;
            var root = new GameObject("Level restart templates");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            root.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            resetTemplates = root.transform;
        }

        void ClearCollectedFloorHazards(LevelLayout level)
        {
            // Pickups can be destroyed after collection. The untouched template still identifies the floor.
            if (!initialLevels.TryGetValue(level, out var initial)) return;
            foreach (var item in initial.template.GetComponentsInChildren<StoryCollectible>(true))
            {
                if (!StoryRunProgress.Current.WasCarriedToNextFloor(item.CollectibleId)) continue;
                foreach (var enemy in level.GetComponentsInChildren<PatrolMonster>(true))
                    enemy.gameObject.SetActive(false);
                foreach (var area in level.GetComponentsInChildren<MonsterVineArea>(true))
                    area.gameObject.SetActive(false);
                return;
            }
        }

        LevelActivation PrepareEntry(LevelLayout level)
        {
            ClearCollectedFloorHazards(level);
            return new LevelActivation(level, activationBudgetMs, activationBatchSize);
        }

        void RecordCollectiblesCarriedOut(LevelLayout from, LevelLayout to)
        {
            if (from == null || !initialLevels.TryGetValue(from, out var initial)) return;
            int index = levels.IndexOf(from);
            if (index < 0 || index + 1 >= levels.Count || levels[index + 1] != to) return;
            foreach (var item in initial.template.GetComponentsInChildren<StoryCollectible>(true))
                StoryRunProgress.Current.RecordCarriedToNextFloor(item.CollectibleId);
        }

        public IEnumerator ResetCurrentLevel(bool respawningAfterDeath = false)
        {
            if (Travelling || CurrentLevel == null || !initialLevels.TryGetValue(CurrentLevel, out var initial)) yield break;
            var previous = CurrentLevel;
            var copy = InstantiateAsync(initial.template, new InstantiateParameters { parent = resetTemplates, worldSpace = false });
            pendingCopies.Add(copy);
            try { yield return copy; }
            finally { pendingCopies.Remove(copy); if (!copy.isDone) copy.Cancel(); }
            var replacement = copy.Result[0];
            var restored = MatchHierarchy(initial.template.transform, replacement.transform);
            replacement.name = previous.name;
            replacement.gameObject.SetActive(false);
            replacement.transform.SetParent(previous.transform.parent, false);
            replacement.transform.SetLocalPositionAndRotation(previous.transform.localPosition, previous.transform.localRotation);
            replacement.transform.localScale = previous.transform.localScale;
            replacement.transform.SetSiblingIndex(previous.transform.GetSiblingIndex());

            Transform Remap(Transform value)
            {
                // Original object references survive sibling deletion/reordering during play.
                return value != null && initial.originals.TryGetValue(value, out var original)
                    ? restored[original] : value;
            }
            LevelElevator RemapLift(LevelElevator lift) => lift != null ? Remap(lift.transform).GetComponent<LevelElevator>() : null;
            foreach (var connection in connections)
            {
                connection.source = RemapLift(connection.source);
                connection.destination = RemapLift(connection.destination);
            }
            ArrivalCheckpoint = RemapLift(ArrivalCheckpoint);
            startingPoint = Remap(startingPoint);
            if (startingLevel == previous) startingLevel = replacement;
            foreach (var opening in FindObjectsByType<SoundShellOpening>(FindObjectsInactive.Include))
                if (opening.world == this)
                {
                    opening.spawn = Remap(opening.spawn);
                    if (opening.startingRoom == previous) opening.startingRoom = replacement;
                }
            levels[levels.IndexOf(previous)] = replacement;
            initialLevels.Remove(previous);
            initial.originals = MatchHierarchy(replacement.transform, initial.template.transform);
            initialLevels[replacement] = initial;
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
            CurrentLevel = replacement;
            if (respawningAfterDeath)
            {
                // Restore progress before the new pickups read it in Awake.
                foreach (var item in replacement.GetComponentsInChildren<StoryCollectible>(true))
                    StoryRunProgress.Current.LoseCollectibleOnDeath(item.CollectibleId);
            }
            using (var activation = PrepareEntry(replacement))
            {
                yield return activation.Prepare();
                foreach (var nav in replacement.GetComponentsInChildren<RuntimePatrolNavigation>())
                    while (nav != null && nav.IsBuilding) yield return null;
                activation.Commit();
            }
            // The template predates arrival, so its property blocks have no completed
            // dissolve. Restore the checkpoint's settled appearance after reactivation.
            if (ArrivalCheckpoint != null && ArrivalCheckpoint.Level == replacement)
            {
                ArrivalCheckpoint.SetTravelInk(0, 0);
                ArrivalCheckpoint.SetPaperEffect("_RespawnPaper", 0);
                ArrivalCheckpoint.SetPaperEffect("_ArrivalDissolve", TryDestination(ArrivalCheckpoint, out _, out _) ? 0 : 1);
            }
        }
        void Awake()
        {
            if(!Application.isPlaying)return;
            Activate(startingLevel!=null?startingLevel:levels.Find(l=>l!=null));
        }
        void Start()
        {
            if(player!=null&&startingPoint!=null)player.Teleport(startingPoint.position);
            if(player!=null&&GetComponent<ElevatorHudContrast>()==null)gameObject.AddComponent<ElevatorHudContrast>();
#if UNITY_EDITOR
            if (player != null && GetComponent<LevelConsole>() == null) gameObject.AddComponent<LevelConsole>();
#endif
            if (player != null && GetComponent<WorldRoomAcoustics>() == null) gameObject.AddComponent<WorldRoomAcoustics>();
        }
        public bool TryDestination(LevelElevator source,out LevelElevator destination,out float seconds)
        {
            RepairStartingConnection(source);
            destination=null;seconds=0;
            foreach(var connection in connections)
            {
                if(connection.source==null||connection.destination==null||connection.source==connection.destination)continue;
                if(connection.source==source)destination=connection.destination;
                else if(connection.bidirectional&&connection.destination==source)destination=connection.source;
                if(destination!=null){seconds=connection.travelSeconds;return levels.Contains(source.Level)&&levels.Contains(destination.Level);}
            }
            return false;
        }

        void RepairStartingConnection(LevelElevator source)
        {
            // An editor save can erase a broken nested-prefab reference. Recover only the
            // authored first route, with its existing destination and a unique starting cabin.
            if (source == null || startingLevel == null || source.Level != startingLevel || connections.Count == 0) return;
            var first = connections[0];
            if (first == null || first.source != null || first.destination == null || first.destination.Level == startingLevel) return;
            var cabins = startingLevel.GetComponentsInChildren<LevelElevator>(true);
            if (cabins.Length != 1 || cabins[0] != source) return;
            first.source = source;
            Debug.LogWarning("已恢复初始电梯的出发端引用，保留原有目的地与运行时间。", this);
        }
        public bool Travel(LevelElevator source,PlayerController actor)
        {
            if(Travelling||source==null||!isActiveAndEnabled||actor==null||actor!=player||!source.Contains(actor.transform.position)||source.DoorOpen<.999f
                ||!TryDestination(source,out var destination,out float seconds)||destination.Busy)return false;
            StartCoroutine(Transport(source,destination,actor,seconds));return true;
        }

        internal bool TryTeleportToLevel(LevelLayout level)
        {
            if (!isActiveAndEnabled || Travelling || level == null || !levels.Contains(level) || player == null
                || player.TransportLocked || player.DeathSequenceLocked || player.EncounterInputLocked
                || player.OpeningSequenceLocked || SonicSpaceStartMenu.BlocksGameplay
                || !player.TryGetComponent<CharacterController>(out var capsule) || !capsule.enabled) return false;

            LevelElevator destination = null;
            foreach (var connection in connections)
            {
                if (connection.destination != null && connection.destination.Level == level)
                {
                    destination = connection.destination;
                    break;
                }
                if (connection.source != null && connection.source.Level == level && (connection.bidirectional || level == startingLevel))
                    destination ??= connection.source;
            }
            if (destination == null || destination.Busy) return false;

            float feetOffset = (capsule.height * .5f - capsule.center.y) * Mathf.Abs(player.transform.lossyScale.y);
            var offset = destination.interiorCenter;
            offset.y += (feetOffset + .05f) / Mathf.Max(.01f, Mathf.Abs(destination.transform.lossyScale.y))
                - destination.interiorSize.y * .5f;
            StartCoroutine(TeleportToElevator(destination, player, offset));
            return true;
        }

        IEnumerator TeleportToElevator(LevelElevator destination, PlayerController actor, Vector3 offset)
        {
            Travelling = true;
            BeginDepartureMusic();
            teleportPassenger = actor;
            actor.TransportLocked = true;
            arrival = destination;
            destination.Busy = true;
            var level = destination.Level;
            yield return CaptureInitialLevelAsync(level);
            if (destination == null || level == null || actor == null) { Release(true); yield break; }

            preparation = PrepareEntry(level);
            yield return preparation.Prepare();
            if (destination == null || level == null || actor == null) { Release(true); yield break; }
            foreach (var nav in level.GetComponentsInChildren<RuntimePatrolNavigation>())
                while (nav != null && nav.isActiveAndEnabled && nav.IsBuilding) yield return null;

            if (destination == null || actor == null) { Release(true); yield break; }
            destination.Open(false);
            while (destination != null && destination.DoorOpen > 0) yield return null;
            if (destination == null || actor == null) { Release(true); yield break; }

            actor.GetComponentInChildren<SonarPulseField>(true)?.ClearPulses();
            yield return CompleteArrival(destination, actor, offset, 0, faceExit: true);
        }

        IEnumerator Transport(LevelElevator source,LevelElevator destination,PlayerController actor,float seconds)
        {
            Travelling=true;departure=source;arrival=destination;source.Busy=destination.Busy=true;
            BeginDepartureMusic();
            source.SetPaperEffect("_ArrivalDissolve", 0);
            source.SetPaperEffect("_RespawnPaper", 0);
            source.Open(false);
            while(source!=null&&source.DoorOpen>0)yield return null;
            if(source==null||destination==null||destination.Level==null||actor==null){Release(true);yield break;}
            yield return CaptureInitialLevelAsync(destination.Level);
            if(source==null||destination==null||destination.Level==null||actor==null){Release(true);yield break;}
            preparation = PrepareEntry(destination.Level);
            var loading = preparation.Prepare();
            bool loadingComplete = false;
            float elapsed = 0;
            float duration = Mathf.Max(.1f, seconds);
            if (source.Contains(actor.transform.position)) source.PlayTravelSound();
            while ((elapsed < duration || !loadingComplete) && source != null && destination != null && actor != null && source.Contains(actor.transform.position))
            {
                if (!loadingComplete) loadingComplete = !loading.MoveNext();
                if (!SonicSpaceStartMenu.BlocksGameplay) elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                source.SetTravelInk(14 * Mathf.SmoothStep(0, 1, t), 4 * t * (1 - t));
                yield return null;
            }
            if (source != null)
            {
                source.SetTravelInk(0, 0);
                source.StopTravelSound();
            }
            if (source != null && actor != null && !source.Contains(actor.transform.position))
            {
                source.Open(true);
                Release(true);
                yield break;
            }
            if(source==null||destination==null||destination.Level==null||actor==null){Release(true);yield break;}
            // Navigation starts only after every staged collider is active.
            var navigation = destination.Level.GetComponentsInChildren<RuntimePatrolNavigation>();
            foreach (var nav in navigation)
            {
                while (nav != null && nav.isActiveAndEnabled && nav.IsBuilding)
                {
                    if(source==null||destination==null||actor==null||!source.Contains(actor.transform.position))
                    {Release(true);yield break;}
                    yield return null;
                }
                if (nav != null && nav.isActiveAndEnabled && !nav.Ready)
                {
                    source.Open(true); Release(true); yield break;
                }
            }
            if(source==null||destination==null||actor==null){Release(true);yield break;}
            destination.Open(false);
            while(destination!=null&&destination.DoorOpen>0)yield return null;
            if(source==null||destination==null||actor==null){Release(true);yield break;}
            Vector3 offset = source.transform.InverseTransformPoint(actor.transform.position);
            float headingDelta = destination.transform.eulerAngles.y - source.transform.eulerAngles.y;
            yield return CompleteArrival(destination, actor, offset, headingDelta);
        }

        IEnumerator CompleteArrival(LevelElevator destination, PlayerController actor, Vector3 offset, float headingDelta, bool faceExit = false)
        {
            // Even a very short debug transfer must finish its fade before disabling the old level.
            while (departingMusic.Count > 0 && departureMusicGain > 0)
            {
                if (destination == null || actor == null || departure != null && !departure.Contains(actor.transform.position))
                { Release(true); yield break; }
                yield return null;
            }
            if (destination == null || actor == null || actor.DeathSequenceLocked ||
                (!faceExit && (departure == null || !departure.Contains(actor.transform.position))))
            { Release(true); yield break; }
            var sourceLevel = departure != null && departure.Level == CurrentLevel ? CurrentLevel : null;
            RestoreDepartureMusic(true);
            Physics.SyncTransforms();
            destination.SetPaperEffect("_ArrivalDissolve", 0);
            destination.SetPaperEffect("_RespawnPaper", 0);
            // Clear only when the transfer commits: cancellation must keep the held item.
            // Loose objects stay in the departure level; only the player crosses levels.
            actor.GetComponent<PlayerPickup>()?.ClearHeldItem();
            actor.Teleport(destination.transform.TransformPoint(offset));
            if (faceExit)
            {
                actor.FaceDirection(destination.transform.forward);
                actor.GetComponent<FootstepEmitter>()?.ResetStride();
            }
            else actor.RotateTransport(headingDelta);
            Physics.SyncTransforms();
            Activate(destination.Level);
            preparation.Commit(); preparation = null;
            RecordCollectiblesCarriedOut(sourceLevel, CurrentLevel);
            actor.GetComponent<PlayerFloorProgression>()?.EnterAuthoredLevel(levels.IndexOf(destination.Level)+1);
            destination.Open(true);
            while(destination!=null&&destination.DoorOpen<.999f)yield return null;
            if (destination == null) { Release(true); yield break; }
            ArrivalCheckpoint = destination;
            Release(false);
            // A shared arrival/departure cabin (e.g. Home) must remain visible and usable.
            if (TryDestination(destination, out _, out _)) yield break;
            for (float time = 0; destination != null && !destination.Busy; time += Time.deltaTime)
            {
                destination.SetPaperEffect("_ArrivalDissolve", Mathf.Clamp01(time / .85f));
                if (time >= .85f) break;
                yield return null;
            }
        }
        void Activate(LevelLayout level)
        {
            CaptureInitialLevel(level);
            if (level != null && preparation == null) ClearCollectedFloorHazards(level);
            CurrentLevel=level;
            if (level != null && level.GetComponentInChildren<LevelArenaBoss>(true) != null)
                StoryRunProgress.Current.EnterBossRoom();
            if(activateOnlyCurrentLevel)foreach(var candidate in levels)if(candidate!=null)candidate.gameObject.SetActive(candidate==level);
        }
        public bool ReturnToStart(PlayerController actor)
        {
            if (actor == null || actor != player || startingLevel == null || startingPoint == null || Travelling) return false;
            Activate(startingLevel);
            ArrivalCheckpoint = null;
            actor.Teleport(startingPoint.position);
            actor.FaceDirection(startingPoint.forward);
            actor.GetComponent<PlayerFloorProgression>()?.EnterAuthoredLevel(levels.IndexOf(startingLevel) + 1);
            actor.GetComponentInChildren<SonarPulseField>(true)?.ClearPulses();
            return true;
        }

        void Release(bool cancelled)
        {
            if (cancelled && departingMusic.Count > 0)
            {
                departureMusicFrom = departureMusicGain;
                departureMusicTarget = 1; departureMusicTime = 0;
            }
            preparation?.Dispose(); preparation = null;
            if (teleportPassenger != null) teleportPassenger.TransportLocked = false;
            teleportPassenger = null;
            if (departure != null) departure.StopTravelSound();
            if(cancelled && departure!=null)departure.Open(true);
            if(departure!=null)departure.Busy=false;if(arrival!=null)arrival.Busy=false;
            departure=arrival=null;Travelling=false;
        }
        void OnDisable()
        {
            foreach (var copy in pendingCopies) if (!copy.isDone) copy.Cancel();
            pendingCopies.Clear();
            StopAllCoroutines();Release(true);
            RestoreDepartureMusic(false);
        }
    }
}

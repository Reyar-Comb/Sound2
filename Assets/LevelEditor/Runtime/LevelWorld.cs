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
        [Min(.1f), Tooltip("Main-thread activation budget per frame while the elevator travels (milliseconds). A single prop cannot be interrupted.")]
        public float activationBudgetMs = 2;
        [Min(1), Tooltip("Maximum props activated per frame; also spreads Start callbacks across frames.")]
        public int activationBatchSize = 24;
        public LevelLayout CurrentLevel { get; private set; }
        public LevelElevator ArrivalCheckpoint { get; private set; }
        public bool Travelling { get; private set; }
        readonly List<(Rigidbody body,bool kinematic,Transform parent,Vector3 position,Quaternion rotation)> cargo = new();

        LevelElevator departure, arrival;
        LevelActivation preparation;
        Transform resetTemplates;
        sealed class InitialLevel
        {
            public LevelLayout template;
            public Dictionary<Transform, Transform> originals;
        }
        readonly Dictionary<LevelLayout, InitialLevel> initialLevels = new();
        readonly HashSet<AsyncInstantiateOperation<LevelLayout>> pendingCopies = new();

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

        public IEnumerator ResetCurrentLevel()
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
            using (var activation = new LevelActivation(replacement, activationBudgetMs, activationBatchSize))
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
        }
        public bool TryDestination(LevelElevator source,out LevelElevator destination,out float seconds)
        {
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
        public bool Travel(LevelElevator source,PlayerController actor)
        {
            if(Travelling||source==null||!isActiveAndEnabled||actor==null||actor!=player||!source.Contains(actor.transform.position)||source.DoorOpen<.999f
                ||!TryDestination(source,out var destination,out float seconds)||destination.Busy)return false;
            StartCoroutine(Transport(source,destination,actor,seconds));return true;
        }
        IEnumerator Transport(LevelElevator source,LevelElevator destination,PlayerController actor,float seconds)
        {
            Travelling=true;departure=source;arrival=destination;source.Busy=destination.Busy=true;
            source.SetPaperEffect("_ArrivalDissolve", 0);
            source.SetPaperEffect("_RespawnPaper", 0);
            source.Open(false);
            cargo.Clear();
            foreach(var body in FindObjectsByType<Rigidbody>())
            {
                if(!source.Contains(body.worldCenterOfMass)||body.GetComponent<PickupItem>()?.IsHeld==true)continue;
                cargo.Add((body,body.isKinematic,body.transform.parent,source.transform.InverseTransformPoint(body.position),Quaternion.Inverse(source.transform.rotation)*body.rotation));
                if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}body.isKinematic=true;
            }
            while(source!=null&&source.DoorOpen>0)yield return null;
            if(source==null||destination==null||destination.Level==null||actor==null){Release(true);yield break;}
            yield return CaptureInitialLevelAsync(destination.Level);
            if(source==null||destination==null||destination.Level==null||actor==null){Release(true);yield break;}
            preparation = new LevelActivation(destination.Level, activationBudgetMs, activationBatchSize);
            var loading = preparation.Prepare();
            bool loadingComplete = false;
            float elapsed = 0;
            float duration = Mathf.Max(.1f, seconds);
            while ((elapsed < duration || !loadingComplete) && source != null && destination != null && actor != null && source.Contains(actor.transform.position))
            {
                if (!loadingComplete) loadingComplete = !loading.MoveNext();
                if (!SonicSpaceStartMenu.BlocksGameplay) elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                source.SetTravelInk(14 * Mathf.SmoothStep(0, 1, t), 4 * t * (1 - t));
                yield return null;
            }
            if (source != null) source.SetTravelInk(0, 0);
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
            Physics.SyncTransforms();
            Vector3 offset = source.transform.InverseTransformPoint(actor.transform.position);
            destination.SetPaperEffect("_ArrivalDissolve", 0);
            destination.SetPaperEffect("_RespawnPaper", 0);
            actor.Teleport(destination.transform.TransformPoint(offset));
            actor.RotateTransport(destination.transform.eulerAngles.y-source.transform.eulerAngles.y);
            foreach(var item in cargo)if(item.body!=null)
            {
                item.body.transform.SetParent(destination.Level.transform.Find("Props")??destination.Level.transform,true);
                // SetParent dirties the Transform; update it as well as the physics pose
                // so the next transform sync cannot overwrite the cargo's teleport.
                Vector3 position = destination.transform.TransformPoint(item.position);
                Quaternion rotation = destination.transform.rotation * item.rotation;
                item.body.transform.SetPositionAndRotation(position, rotation);
                item.body.position=position;item.body.rotation=rotation;
            }
            Physics.SyncTransforms();
            Activate(destination.Level);
            preparation.Commit(); preparation = null;
            actor.GetComponent<PlayerFloorProgression>()?.EnterAuthoredLevel(levels.IndexOf(destination.Level)+1);
            destination.Open(true);
            while(destination!=null&&destination.DoorOpen<.999f)yield return null;
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
            CurrentLevel=level;
            if(activateOnlyCurrentLevel)foreach(var candidate in levels)if(candidate!=null)candidate.gameObject.SetActive(candidate==level);
        }
        void Release(bool cancelled)
        {
            preparation?.Dispose(); preparation = null;
            if(cancelled && departure!=null)departure.Open(true);
            foreach(var item in cargo)if(item.body!=null){if(cancelled && item.parent!=null)item.body.transform.SetParent(item.parent,true);item.body.isKinematic=item.kinematic;}
            cargo.Clear();if(departure!=null)departure.Busy=false;if(arrival!=null)arrival.Busy=false;
            departure=arrival=null;Travelling=false;
        }
        void OnDisable()
        {
            foreach (var copy in pendingCopies) if (!copy.isDone) copy.Cancel();
            pendingCopies.Clear();
            StopAllCoroutines();Release(true);
        }
    }
}

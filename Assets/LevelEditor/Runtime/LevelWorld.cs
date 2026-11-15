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
    [DefaultExecutionOrder(-110),DisallowMultipleComponent]
    public sealed class LevelWorld : MonoBehaviour
    {
        public PlayerController player;
        public List<LevelLayout> levels = new();
        public List<ElevatorConnection> connections = new();
        public LevelLayout startingLevel;
        public Transform startingPoint;
        public FloorProgressionProfile profile;
        public bool activateOnlyCurrentLevel = true;
        public LevelLayout CurrentLevel { get; private set; }
        public LevelElevator ArrivalCheckpoint { get; private set; }
        public bool Travelling { get; private set; }
        readonly List<(Rigidbody body,bool kinematic,Transform parent,Vector3 position,Quaternion rotation)> cargo = new();

        LevelElevator departure, arrival;
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
            float elapsed = 0;
            float duration = Mathf.Max(.1f, seconds);
            while (elapsed < duration && source != null && actor != null && source.Contains(actor.transform.position))
            {
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
            destination.Level.gameObject.SetActive(true);destination.Open(false);
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
                item.body.position=destination.transform.TransformPoint(item.position);item.body.rotation=destination.transform.rotation*item.rotation;
            }
            Activate(destination.Level);
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
            CurrentLevel=level;
            if(activateOnlyCurrentLevel)foreach(var candidate in levels)if(candidate!=null)candidate.gameObject.SetActive(candidate==level);
        }
        void Release(bool cancelled)
        {

            foreach(var item in cargo)if(item.body!=null){if(cancelled && item.parent!=null)item.body.transform.SetParent(item.parent,true);item.body.isKinematic=item.kinematic;}
            cargo.Clear();if(departure!=null)departure.Busy=false;if(arrival!=null)arrival.Busy=false;
            departure=arrival=null;Travelling=false;
        }
        void OnDisable(){StopAllCoroutines();Release(true);}
    }
}

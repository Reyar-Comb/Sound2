using UnityEngine;
namespace Sound2.LevelEditing
{
    [DisallowMultipleComponent]
    public sealed class LevelElevator : MonoBehaviour
    {
        public Transform leftDoor, rightDoor;
        public Vector3 interiorCenter = new(0, 1.5f, 0);
        public Vector3 interiorSize = new(3.5f, 3, 4.6f);
        [Min(.1f)] public float doorSeconds = .8f;
        public float DoorOpen { get; private set; }
        public bool Busy { get; internal set; }
        float target;
        Vector3 leftClosed, rightClosed;
        public LevelLayout Level => GetComponentInParent<LevelLayout>(true);
        void Awake()
        {
            if(leftDoor!=null)leftClosed=leftDoor.localPosition;
            if(rightDoor!=null)rightClosed=rightDoor.localPosition;
            ConfigureCabinEdges();
        }
        void ConfigureCabinEdges()
        {
            var properties = new MaterialPropertyBlock();
            foreach (string part in new[] { "Floor", "Roof", "Back", "Left", "Right", "Left jamb", "Right jamb", "Lintel" })
            {
                var panel = transform.Find(part);
                if (panel == null || !panel.TryGetComponent<Renderer>(out var renderer)) continue;
                renderer.GetPropertyBlock(properties);
                properties.SetMatrix("_PanelToCabin", Matrix4x4.TRS(panel.localPosition, panel.localRotation, panel.localScale));
                properties.SetFloat("_CabinEdges", 1);
                renderer.SetPropertyBlock(properties);
            }
        }
        public bool Contains(Vector3 position) => new Bounds(interiorCenter,interiorSize).Contains(transform.InverseTransformPoint(position));
        public void Open(bool open) => target = open ? 1 : 0;
        void Update()
        {
            if(SonicSpaceStartMenu.BlocksGameplay)return;
            DoorOpen=Mathf.MoveTowards(DoorOpen,target,Time.deltaTime/Mathf.Max(.1f,doorSeconds));
            if(leftDoor!=null)leftDoor.localPosition=leftClosed+Vector3.left*1.4f*DoorOpen;
            if(rightDoor!=null)rightDoor.localPosition=rightClosed+Vector3.right*1.4f*DoorOpen;
        }
        public LevelWorld World()
        {
            foreach(var world in FindObjectsByType<LevelWorld>())
                if(world.TryDestination(this,out _,out _))return world;
            return null;
        }
        public bool CanUse(PlayerInteraction player,bool inside) => player!=null && !Busy && !SonicSpaceStartMenu.BlocksGameplay
            && (inside ? DoorOpen>.999f && Contains(player.transform.position) && World()!=null : DoorOpen<.001f);
        public void Use(PlayerInteraction player,bool inside)
        {
            if(!CanUse(player,inside))return;
            if(!inside)Open(true);else World()?.Travel(this,player.GetComponent<PlayerController>());
        }
    }
}

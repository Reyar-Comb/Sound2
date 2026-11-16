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

        [Header("电梯音效")]
        [SerializeField, InspectorName("按钮音效"), Tooltip("内外两个按钮共用。音源在实际按下的按钮上，单个按钮可以覆盖。")]
        AudioClip buttonPressSound;
        internal AudioClip ButtonPressSound => buttonPressSound;

        [SerializeField, Tooltip("在门扇开始打开时播放一次。")]
        AudioClip doorOpenSound;
        [SerializeField, Tooltip("在门扇开始关闭时播放一次。")]
        AudioClip doorCloseSound;
        [SerializeField, Tooltip("关门后开始运行时播放一次。")]
        AudioClip travelSound;
        [SerializeField, Tooltip("运行音效的空间音源位置，默认使用轿厢顶部。")]
        Transform travelSoundOrigin;

        public float DoorOpen { get; private set; }
        public bool Busy { get; internal set; }
        float target;
        int doorDirection;
        Vector3 leftClosed, rightClosed;
        SpatialAudioEmitter doorAudio, travelAudio;
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
            foreach (string part in new[] { "Roof", "Back", "Left", "Right", "Left jamb", "Right jamb", "Lintel" })
            {
                var panel = transform.Find(part);
                if (panel == null || !panel.TryGetComponent<Renderer>(out var renderer)) continue;
                renderer.GetPropertyBlock(properties);
                properties.SetMatrix("_PanelToCabin", Matrix4x4.TRS(panel.localPosition, panel.localRotation, panel.localScale));
                properties.SetFloat("_CabinEdges", 1);
                renderer.SetPropertyBlock(properties);
            }
        }
        Renderer[] panels;
        MaterialPropertyBlock effects;

        public void SetPaperEffect(string property, float value)
        {
            effects ??= new MaterialPropertyBlock();
            panels ??= GetComponentsInChildren<Renderer>(true);
            foreach (var panel in panels)
            {
                panel.GetPropertyBlock(effects);
                effects.SetFloat(property, value);
                panel.SetPropertyBlock(effects);
            }
        }

        public void SetTravelInk(float height, float strength)
        {
            effects ??= new MaterialPropertyBlock();
            panels ??= GetComponentsInChildren<Renderer>(true);
            foreach (var panel in panels)
            {
                panel.GetPropertyBlock(effects);
                effects.SetVector("_CabinMotion", new Vector4(height, strength, 0, 0));
                panel.SetPropertyBlock(effects);
            }
        }

        public bool Contains(Vector3 position) => new Bounds(interiorCenter,interiorSize).Contains(transform.InverseTransformPoint(position));
        internal bool ContainsForAcoustics(Vector3 position)
        {
            var point = transform.InverseTransformPoint(position);
            var half = interiorSize * .5f;
            float threshold = Mathf.Max(interiorCenter.z + half.z, leftDoor != null ? leftClosed.z : rightClosed.z);
            return Mathf.Abs(point.x - interiorCenter.x) <= half.x && Mathf.Abs(point.y - interiorCenter.y) <= half.y
                && point.z >= interiorCenter.z - half.z && point.z <= threshold;
        }
        public void Open(bool open) => target = open ? 1 : 0;
        void Update()
        {
            if(SonicSpaceStartMenu.BlocksGameplay)return;
            float previous = DoorOpen;
            DoorOpen=Mathf.MoveTowards(DoorOpen,target,Time.deltaTime/Mathf.Max(.1f,doorSeconds));
            if(leftDoor!=null)leftDoor.localPosition=leftClosed+Vector3.left*1.4f*DoorOpen;
            if(rightDoor!=null)rightDoor.localPosition=rightClosed+Vector3.right*1.4f*DoorOpen;

            int direction = DoorOpen.CompareTo(previous);
            if (direction == 0) return;
            if (direction != doorDirection)
                PlayDoorSound(direction > 0 ? doorOpenSound : doorCloseSound);
            doorDirection = direction;
        }

        void PlayDoorSound(AudioClip clip)
        {
            var origin = leftDoor != null ? leftDoor : rightDoor;
            if (clip == null || origin == null) return;
            doorAudio ??= GetOrAddAudioEmitter(origin);
            doorAudio.PlayOneShot(clip);
        }

        internal void PlayTravelSound()
        {
            if (travelSound == null) return;
            if (travelAudio == null)
            {
                var origin = travelSoundOrigin != null ? travelSoundOrigin : transform.Find("Roof");
                if (origin == null) return;
                travelAudio = GetOrAddAudioEmitter(origin);
            }
            travelAudio.PlayOneShot(travelSound);
        }

        internal void StopTravelSound()
        {
            if (travelAudio != null) travelAudio.Source.Stop();
        }

        internal static SpatialAudioEmitter GetOrAddAudioEmitter(Transform origin)
        {
            if (!origin.TryGetComponent<SpatialAudioEmitter>(out var emitter))
                emitter = origin.gameObject.AddComponent<SpatialAudioEmitter>();
            emitter.settings.bypassRoomAcoustics = true;
            return emitter;
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

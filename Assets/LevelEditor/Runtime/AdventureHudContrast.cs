using System.Linq;
using TMPro;
using UnityEngine;

namespace Sound2.LevelEditing
{
    /// <summary>Ink on white elevator surfaces; the original white HUD everywhere else.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class AdventureHudContrast : MonoBehaviour
    {
        TMP_Text[] labels;
        ReticleGraphic[] reticles;
        Camera view;
        Transform player;
        EightFloorAdventure adventure;
        TMP_Text floorLabel;
        PlayerSubtitles subtitles;
        bool subtitleWasEnabled;
        bool floorLabelWasEnabled;
        readonly RaycastHit[] hits=new RaycastHit[32];
        void Start()
        {
            adventure=GetComponent<EightFloorAdventure>();player=adventure.player.transform;
            subtitles=player.GetComponent<PlayerSubtitles>();floorLabel=subtitles.FloorText;
            subtitleWasEnabled=subtitles.SubtitleText!=null && subtitles.SubtitleText.enabled;
            if(floorLabel!=null)floorLabelWasEnabled=floorLabel.enabled;
            view=player.GetComponentInChildren<Camera>();
            labels=FindObjectsByType<TMP_Text>(FindObjectsInactive.Include).Where(t=>t.gameObject.scene==gameObject.scene && t.GetComponentInParent<Canvas>()!=null).ToArray();
            reticles=FindObjectsByType<ReticleGraphic>(FindObjectsInactive.Include).Where(t=>t.gameObject.scene==gameObject.scene).ToArray();
        }
        void LateUpdate()
        {
            if(view==null || SonicSpaceStartMenu.BlocksGameplay)return;
            bool inside=adventure.elevators.Any(e=>e.Contains(player.position));
            if(floorLabel!=null)floorLabel.enabled=floorLabelWasEnabled && !inside;
            if(subtitles.SubtitleText!=null)subtitles.SubtitleText.enabled=subtitleWasEnabled && !(inside && subtitles.CurrentText.StartsWith("第 "));
            foreach(var text in labels)if(text!=null && text.isActiveAndEnabled)
            {float value=OverPaper(text.rectTransform)?0:1;text.color=new Color(value,value,value,text.color.a);}
            foreach(var graphic in reticles)if(graphic!=null && graphic.isActiveAndEnabled)
            {float value=OverPaper(graphic.rectTransform)?0:1;graphic.color=new Color(value,value,value,graphic.color.a);}
        }
        void OnDisable()
        {
            if(floorLabel!=null)floorLabel.enabled=floorLabelWasEnabled;
            if(subtitles!=null && subtitles.SubtitleText!=null)subtitles.SubtitleText.enabled=subtitleWasEnabled;
        }
        bool OverPaper(RectTransform rect)
        {
            Vector2 screen=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            int count=Physics.RaycastNonAlloc(view.ScreenPointToRay(screen),hits,100,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            Collider nearest=null;float distance=float.PositiveInfinity;
            for(int i=0;i<count;i++)if(hits[i].distance<distance && !hits[i].transform.IsChildOf(player))
            {distance=hits[i].distance;nearest=hits[i].collider;}
            if(nearest==null || !nearest.TryGetComponent<Renderer>(out var renderer))return false;
            var material=renderer.sharedMaterial;
            // Thin ink seams sit on paper too; sampling a seam must not turn an entire caption white.
            return material!=null && material.shader.name=="Sound2/Elevator White Ink" && !nearest.name.StartsWith("Shaft");
        }
    }
}

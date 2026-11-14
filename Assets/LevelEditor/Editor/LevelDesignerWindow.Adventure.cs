using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sound2.LevelEditing.Editor
{
    public sealed partial class LevelDesignerWindow
    {
        void DrawAdventureHeader(VisualElement header)
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var adventure=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EightFloorAdventure>(true)).FirstOrDefault();
            if(adventure==null)
            {
                if(scene.path==AdventureMapBuilder.ScenePath)
                    Button(header,"生成八层上行探险",()=>AdventureMapBuilder.Rebuild(architecturalMode ? AdventureGenerationMode.Architectural : AdventureGenerationMode.DungeonLoops)).SetEnabled(!Application.isPlaying);
                return;
            }
            var names=AdventureMapBuilder.Themes.Select((name,i)=>$"{i+1} 层 · {name}").ToList();
            int current=layout!=null && layout.TryGetComponent<AdventureFloor>(out var floor)?floor.number:1;
            var picker=new PopupField<string>("探险楼层",names,current-1);
            picker.RegisterValueChangedCallback(e=>AdventureMapBuilder.ShowFloor(adventure,picker.index+1));
            picker.SetEnabled(!Application.isPlaying);header.Add(picker);
            header.Add(new Label("1 → 8 层 · 七部独立上行电梯 · 到站永久开门"));
            header.Add(new Label(architecturalMode ? "建筑平面：共墙门洞、功能分区；6 层医院、7 层雪林。" : "手工编辑当前地图；可切换建筑平面模式重建。"));
            var row=Row(header);
            var seed=new IntegerField("地图种子"){value=adventure.seed,isDelayed=true};
            seed.RegisterValueChangedCallback(e=>{Undo.RecordObject(adventure,"修改探险种子");adventure.seed=e.newValue;EditorUtility.SetDirty(adventure);});
            seed.SetEnabled(!Application.isPlaying);row.Add(seed);
            Button(row,"按种子重建八层",()=>
            {
                if(EditorUtility.DisplayDialog("重建八层地图","将替换八层布局及家具，当前场景会先备份到 Logs/AdventureBackups。","重建","取消"))AdventureMapBuilder.Rebuild(architecturalMode ? AdventureGenerationMode.Architectural : AdventureGenerationMode.DungeonLoops);
            }).SetEnabled(!Application.isPlaying);
        }
        void ExportAdventure(EightFloorAdventure map)
        {
            if(Application.isPlaying)return;
            string path=EditorUtility.SaveFilePanelInProject("导出完整八层探险","EightFloorAdventure","unity","包含八层布局、电梯、家具和玩家。");
            if(string.IsNullOrEmpty(path))return;
            try
            {
                foreach(var f in map.floors){LevelBuilder.Rebuild(f.layout);LevelBuilder.Persist(f.layout,map.gameObject.scene.path);}
                if(!EditorSceneManager.SaveScene(map.gameObject.scene,path,true))throw new Exception("导出失败。");
                status="已导出完整八层探险："+path;hasError=false;
            }
            catch(Exception e){status=e.Message;hasError=true;}
            UpdateStatus();
        }
    }
}

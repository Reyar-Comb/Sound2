using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
namespace Sound2.LevelEditing.Editor
{
    [CustomEditor(typeof(LevelWorld))]
    public sealed class LevelWorldEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root=new VisualElement();
            root.Add(new HelpBox("把独立关卡 Prefab 放到本对象下，再配对电梯入口。目的地只保存在此处，单关没有楼层编号或去向要求。",HelpBoxMessageType.Info));
            root.Add(new Button(() => {
                var levels = ((LevelWorld)target).GetComponentsInChildren<LevelLayout>(true);
                var level = levels.FirstOrDefault(l => l.gameObject.activeInHierarchy) ?? levels.FirstOrDefault();
                EditorWindow.GetWindow<LevelDesignerWindow>("关卡编辑器").Bind(level);
            }) { text = "打开总场景关卡编辑器" });
            root.Add(new Button(()=>{var world=(LevelWorld)target;Undo.RecordObject(world,"收集独立关卡");world.levels=world.GetComponentsInChildren<LevelLayout>(true).ToList();if(world.startingLevel==null)world.startingLevel=world.levels.FirstOrDefault();EditorUtility.SetDirty(world);serializedObject.Update();}){text="收集子关卡"});
            InspectorElement.FillDefaultInspector(root,serializedObject,this);return root;
        }
        [MenuItem("Tools/Sound2/创建关卡总场景")]
        static void CreateWorld()
        {
            if(Application.isPlaying||!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);LevelBuilder.AddLighting();
            var world=new GameObject("Level World",typeof(LevelWorld)).GetComponent<LevelWorld>();
            var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));world.player=player.GetComponent<PlayerController>();
            Undo.RegisterCreatedObjectUndo(world.gameObject,"创建关卡总场景");Selection.activeGameObject=world.gameObject;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public static class VerifyLevelEditingWorkflow
{
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Exit Play mode first.");
        var scene=EditorSceneManager.NewPreviewScene();
        var go=new GameObject("Workflow verification");SceneManager.MoveGameObjectToScene(go,scene);
        var layout=go.AddComponent<LevelLayout>();
        LevelDesignerWindow window=null;var checks=new List<string>();
        void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Chair.prefab");
            var first=LevelPropPlacement.Place(layout,prefab,new Pose(Vector3.zero,Quaternion.identity));
            var second=LevelPropPlacement.Place(layout,prefab,new Pose(Vector3.right,Quaternion.identity));
            var third=LevelPropPlacement.Place(layout,prefab,new Pose(Vector3.left,Quaternion.identity));
            Check(first.name=="Chair"&&second.name=="Chair (1)"&&third.name=="Chair (2)","Repeated placement uses Unity numeric suffixes");
            second.name=first.name;third.name=first.name;
            Check(LevelPropPlacement.EnsureUniqueNames(layout)==2,"Existing duplicates repaired");
            var props=LevelPropPlacement.PropsRoot(layout);
            Check(props.Cast<Transform>().Select(t=>t.name).Distinct().Count()==3,"All sibling names unique after repair");
            Check(LevelPropPlacement.EnsureUniqueNames(layout)==0,"Name repair is idempotent");
            Check(props.Cast<Transform>().All(t=>PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)==prefab),"Naming retains prefab connections");

            window=ScriptableObject.CreateInstance<LevelDesignerWindow>();window.CreateGUI();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var type=typeof(LevelDesignerWindow);
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(window,value);
            void Call(string name)=>type.GetMethod(name,flags).Invoke(window,null);
            Set("layout",layout);Set("selectedProp",first);Call("Refresh");
            var fields=window.rootVisualElement.Q("properties");
            Check(fields.Query<Vector3Field>().ToList().Count==0&&fields.Query<FloatField>().ToList().Count==0,"Prop panel does not duplicate Inspector parameters");
            Set("selectedProp",null);
            var tool=type.GetField("tool",flags);tool.SetValue(window,Enum.Parse(tool.FieldType,"Line"));
            var points=new[]{new Vector2(0,0),new Vector2(5,0),new Vector2(5,5),new Vector2(10,5)};
            foreach(var point in points){Set("hover",point);Call("Click");}
            Check(layout.corridors.Count==0,"Second and later clicks keep collecting points");
            Call("Click");
            var pending=(List<Vector2>)type.GetField("pending",flags).GetValue(window);
            Check(pending.Count==4,"Duplicate consecutive click is ignored");
            Call("FinishCorridor");
            Check(layout.corridors.Count==1&&!layout.corridors[0].curved,"Finish creates one straight corridor");
            Check(LevelGeometry.CorridorPath(layout,layout.corridors[0]).SequenceEqual(points),"Straight corridor preserves all four corners without smoothing");
            Check(layout.generatedRoot!=null&&layout.generatedRoot.GetComponentsInChildren<MeshCollider>().All(c=>c.sharedMesh!=null),"Multi-segment corridor generates collision geometry");
            Check(pending.Count==0,"Finishing clears draft points");
            Set("hover",Vector2.one);Call("Click");Call("FinishCorridor");
            Check(layout.corridors.Count==1&&pending.Count==1,"Single-point draft cannot create a corridor");
            pending.Clear();tool.SetValue(window,Enum.Parse(tool.FieldType,"Curve"));
            foreach(var point in points){Set("hover",point);Call("Click");}Call("FinishCorridor");
            Check(layout.corridors.Count==2&&layout.corridors[1].curved,"Curve drawing still uses the same multi-point workflow");
            return new{passed=true,checks};
        }
        finally
        {
            if(window!=null)Object.DestroyImmediate(window);
            if(go!=null)Object.DestroyImmediate(go);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}

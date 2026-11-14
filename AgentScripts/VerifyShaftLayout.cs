using System;using System.Linq;using UnityEngine;using UnityEditor;using Sound2.LevelEditing;
public static class VerifyShaftLayout {
 public static object Main(){var root=new GameObject("Shaft layout regression");root.SetActive(false);try{
 var map=root.AddComponent<LevelMap>();map.generateOnPlay=false;map.geometryMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
 root.transform.position=new Vector3(2000,0,2000);var f0=map.EnsureFloor(0);var f1=map.EnsureFloor(1);var plan=SeededFloorPlan.Generate(2);
 var nextOrigin=f1.transform.TransformPoint(new Vector3(f1.Plan.Exit.x,-map.floorSpacing,f1.Plan.Exit.y));
 // Force the next seed's unrotated exit into an existing vertical shaft column.
 f0.Exit.transform.position=nextOrigin+new Vector3(plan.Exit.x,0,plan.Exit.y);
 var f2=map.EnsureFloor(2);
 foreach(var previous in new[]{f0,f1}){var d=previous.Exit.transform.position-f2.Exit.transform.position;if(new Vector2(d.x,d.z).magnitude<6)throw new Exception("Stacked exit shafts remain");}
 if(!plan.Cells.SequenceEqual(f2.Plan.Cells)||!plan.Resources.SequenceEqual(f2.Plan.Resources)||plan.Exit!=f2.Plan.Exit)throw new Exception("Seeded layout changed instead of orientation");
 var f3=map.EnsureFloor(3);if(Vector3.Distance(f3.transform.position,f2.transform.TransformPoint(new Vector3(plan.Exit.x,-map.floorSpacing,plan.Exit.y)))>.001f)throw new Exception("Rotated floor's shaft no longer connects to next entrance");
 for(int f=0;f<256;f++){var p=SeededFloorPlan.Generate(f);if(p.Exit.sqrMagnitude<36 || p.IsPit(Vector2Int.zero))throw new Exception("Outgoing shaft overlaps safe arrival area F"+f);}
 return new{passed=true,checkedSeeds=256,rotation=f2.transform.eulerAngles.ToString(),checks=new[]{"forced vertical overlap eliminated","seed layout preserved","rotated shaft connects to next arrival","all arrival areas separate from exits"}};
 }finally{UnityEngine.Object.Destroy(root);}}
}

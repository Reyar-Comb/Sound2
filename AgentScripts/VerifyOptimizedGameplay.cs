using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Object=UnityEngine.Object;

public static class VerifyOptimizedGameplay
{
    public static async Task<object> Run()
    {
        if(!Application.isPlaying)throw new Exception("Requires Play Mode");
        var player=Object.FindAnyObjectByType<Sound2.LevelEditing.LevelWorld>().player;
        var picker=player.GetComponent<PlayerPickup>();
        if(picker.HeldItem!=null)throw new Exception("Fixture requires empty hands");
        var position=player.transform.position;var forward=picker.viewTransform.forward;bool suspended=player.InputSuspended;
        var checks=new List<string>();GameObject shelf=null,glass=null,remains=null;
        float throwSpeed=picker.throwSpeed,throwUp=picker.throwUpwardSpeed;
        void Check(bool v,string message){if(!v)throw new Exception(message);checks.Add(message);}
        try
        {
            player.InputSuspended=true;player.Teleport(new Vector3(5000,5000,5000));player.FaceDirection(Vector3.forward);
            shelf=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LibraryBookshelf.prefab"));
            shelf.transform.position=picker.viewTransform.position+Vector3.forward*3.5f;
            var solid=shelf.GetComponentsInChildren<Collider>();Physics.SyncTransforms();
            var bounds=solid[0].bounds;foreach(var c in solid)bounds.Encapsulate(c.bounds);
            shelf.transform.position+=picker.viewTransform.position+Vector3.forward*3.5f-bounds.center;
            Physics.SyncTransforms();
            Check(solid.Length==1&&solid[0] is MeshCollider,"Static bookshelf uses one baked collider");
            Check(solid[0].Raycast(new Ray(picker.viewTransform.position,Vector3.forward),out _,6),"Baked shelf still blocks the aim ray");
            glass=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupGlassMug.prefab"));
            glass.transform.position=picker.viewTransform.position+Vector3.forward*.6f-Vector3.up*.2f;
            var item=glass.GetComponent<PickupItem>();var body=glass.GetComponent<Rigidbody>();body.useGravity=false;
            var parts=glass.GetComponentsInChildren<Collider>();
            item.RemainsSpawned+=r=>remains=r;
            Physics.SyncTransforms();
            Check(picker.TryPickUp(item)&&item.IsHeld&&body.isKinematic,"Explicit pickup keeps the original interaction path");
            Check(parts.All(c=>!c.enabled),"Held compound colliders are disabled");
            picker.throwSpeed=12;picker.throwUpwardSpeed=0;
            Check(picker.TryThrow()&&!item.IsHeld&&!body.isKinematic&&parts.All(c=>c.enabled),"Throw restores all compound colliders and rigidbody motion");
            double end=EditorApplication.timeSinceStartup+5;
            while(remains==null&&EditorApplication.timeSinceStartup<end)await Task.Delay(20);
            Check(remains!=null,"Real collision with the optimized bookshelf breaks the thrown glass");
            Check(remains.GetComponentsInChildren<Rigidbody>().Length==25,"Breakage retains 25 fragment bodies");
            Check(remains.GetComponentsInChildren<MeshRenderer>().Length==49,"Breakage retains all 49 visual fragments");
            Check(remains.GetComponentsInChildren<Collider>().All(c=>c.attachedRigidbody!=null),"Every fragment retains a physical body");
            var result=new{passed=true,checks};File.WriteAllText("Logs/CollisionOptimization/gameplay-checks.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
        }
        finally
        {
            if(picker.HeldItem!=null)picker.HeldItem.Release(picker.HeldItem.transform.position,Vector3.zero);
            if(glass!=null)Object.Destroy(glass);if(remains!=null)Object.Destroy(remains);if(shelf!=null)Object.Destroy(shelf);
            picker.throwSpeed=throwSpeed;picker.throwUpwardSpeed=throwUp;
            player.Teleport(position);player.FaceDirection(forward);player.InputSuspended=suspended;
        }
    }
}

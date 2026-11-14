using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    /// <summary>Reusable, grounded furniture missing from the existing prop catalog.</summary>
    public static class AdventureFurniture
    {
        static Material material;
        static Mesh cone;
        public static void Ensure(Material preview)
        {
            material=preview;
            Directory.CreateDirectory("Assets/Prefabs/Adventure");AssetDatabase.Refresh();
            foreach(string kind in new[]{"Bed","Sofa","Counter","HospitalBed","WardCurtain","Shelf","Checkout","Pine","SnowRock","Bench","Pillar","Bedside","Wardrobe","Sink","Toilet","Washer","ProduceBin"})
            {
                string path="Assets/Prefabs/Adventure/Adventure"+kind+".prefab";
                if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)continue;
                var root=new GameObject("Adventure"+kind);
                try
                {
                    var prop=root.AddComponent<LevelProp>();prop.gameplayMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
                    switch(kind)
                    {
                        case "Bedside":
                            Box(root,"Cabinet",new(0,.35f,0),new(.6f,.7f,.5f));Box(root,"Top",new(0,.73f,0),new(.65f,.06f,.55f));
                            for(int k=0;k<2;k++)Box(root,"Drawer pull",new(0,.3f+k*.3f,-.28f),new(.2f,.035f,.04f));break;
                        case "Wardrobe":
                            Box(root,"Case",new(0,1.2f,0),new(2,2.4f,.65f));
                            foreach(float x in new[]{-.48f,.48f}){Box(root,"Door",new(x,1.2f,-.34f),new(.94f,2.3f,.04f));Box(root,"Handle",new(x*.3f,1.2f,-.39f),new(.04f,.3f,.04f));}break;
                        case "Sink":
                            Box(root,"Pedestal",new(0,.4f,0),new(.3f,.8f,.3f));Box(root,"Basin",new(0,.8f,0),new(.75f,.15f,.55f));
                            Box(root,"Tap",new(0,1.03f,.18f),new(.05f,.3f,.06f));Box(root,"Spout",new(0,1.16f,.08f),new(.05f,.05f,.22f));break;
                        case "Toilet":
                            Box(root,"Base",new(0,.2f,0),new(.4f,.4f,.65f));Box(root,"Seat",new(0,.45f,-.1f),new(.55f,.1f,.7f));
                            Box(root,"Cistern",new(0,.65f,.35f),new(.6f,.7f,.25f));break;
                        case "Washer":
                            Box(root,"Machine",new(0,.45f,0),new(.85f,.9f,.75f));Box(root,"Door",new(0,.4f,-.39f),new(.55f,.55f,.04f));Box(root,"Control",new(.2f,.8f,-.4f),new(.1f,.07f,.04f));break;
                        case "ProduceBin":
                            Box(root,"Stand",new(0,.5f,0),new(2.4f,1,1.2f));
                            for(int k=0;k<6;k++)Box(root,"Produce crate",new(-.8f+(k%3)*.8f,1.1f,-.3f+(k/3)*.6f),new(.72f,.25f,.52f));break;
                        case "Bed": case "HospitalBed":
                            Box(root,"Frame",new(0,.35f,0),new(1.5f,.18f,2.5f));
                            Box(root,"Mattress",new(0,.57f,0),new(1.45f,.25f,2.35f));
                            Box(root,"Pillow",new(0,.75f,.83f),new(.95f,.18f,.45f));
                            Box(root,"Headboard",new(0,.85f,1.25f),new(1.6f,1.2f,.12f));
                            foreach(float x in new[]{-.62f,.62f})foreach(float z in new[]{-1f,1f})Box(root,"Leg",new(x,.16f,z),new(.08f,.32f,.08f));
                            if(kind=="HospitalBed")
                            {foreach(float x in new[]{-.78f,.78f}){Box(root,"Safety rail",new(x,.9f,0),new(.06f,.06f,1.6f));foreach(float z in new[]{-.7f,.7f})Box(root,"Rail support",new(x,.7f,z),new(.06f,.4f,.06f));}Box(root,"Footboard",new(0,.7f,-1.25f),new(1.6f,.8f,.1f));}
                            break;
                        case "Sofa":
                            Box(root,"Plinth",new(0,.25f,0),new(2.8f,.4f,1.1f));Box(root,"Back",new(0,.9f,.48f),new(2.8f,1,.2f));
                            foreach(float x in new[]{-.9f,0,.9f})Box(root,"Cushion",new(x,.54f,-.08f),new(.85f,.22f,.85f));
                            foreach(float x in new[]{-1.35f,1.35f})Box(root,"Arm",new(x,.6f,0),new(.2f,.7f,1.1f));break;
                        case "Counter": case "Checkout":
                            Box(root,"Base",new(0,.5f,0),new(2.5f,1,.8f));Box(root,"Worktop",new(0,1.04f,0),new(2.7f,.1f,1));
                            for(int i=-1;i<=1;i++)Box(root,"Cabinet front",new(i*.8f,.53f,-.415f),new(.76f,.85f,.04f));
                            if(kind=="Checkout"){Box(root,"Register stand",new(.7f,1.3f,0),new(.1f,.4f,.1f));Box(root,"Register",new(.7f,1.55f,0),new(.6f,.4f,.1f));Box(root,"Conveyor",new(-.6f,1.1f,0),new(1.3f,.05f,.7f));}break;
                        case "Shelf":
                            Box(root,"Back",new(0,1.2f,0),new(3,2.4f,.1f));
                            foreach(float x in new[]{-1.5f,1.5f})Box(root,"Upright",new(x,1.2f,0),new(.09f,2.4f,1.4f));
                            for(int row=0;row<4;row++)
                            {Box(root,"Shelf",new(0,.12f+row*.58f,0),new(3,.08f,1.4f));for(int col=0;col<5;col++)foreach(float z in new[]{-.42f,.42f})Box(root,"Package",new(-1.1f+col*.55f,.35f+row*.58f,z),new(.35f,.38f,.35f));}break;
                        case "WardCurtain":
                            foreach(float x in new[]{-1.3f,1.3f}){Box(root,"Post",new(x,1.15f,0),new(.05f,2.3f,.05f));Box(root,"Foot",new(x,.05f,0),new(.2f,.1f,.65f));}
                            Box(root,"Rail",new(0,2.3f,0),new(2.65f,.05f,.05f));
                            for(int i=0;i<12;i++)Box(root,"Pleat",new(-1.2f+i*.218f,1.3f,(i%2)*.05f),new(.22f,1.8f,.08f));break;
                        case "Pine":
                            var trunk=GameObject.CreatePrimitive(PrimitiveType.Cylinder);trunk.name="Trunk";trunk.transform.SetParent(root.transform,false);trunk.transform.localPosition=new(0,1.4f,0);trunk.transform.localScale=new(.45f,1.4f,.45f);trunk.GetComponent<Renderer>().sharedMaterial=material;
                            EnsureCone();
                            for(int i=0;i<3;i++){var crown=new GameObject("Snow boughs",typeof(MeshFilter),typeof(MeshRenderer));crown.transform.SetParent(root.transform,false);crown.transform.localPosition=new(0,1.2f+i*1.2f,0);crown.transform.localScale=new Vector3(1.9f-i*.42f,2.4f,1.9f-i*.42f);crown.GetComponent<MeshFilter>().sharedMesh=cone;crown.GetComponent<Renderer>().sharedMaterial=material;}break;
                        case "SnowRock":
                            for(int i=0;i<3;i++){var rock=GameObject.CreatePrimitive(PrimitiveType.Sphere);rock.name="Snow stone";rock.transform.SetParent(root.transform,false);rock.transform.localPosition=new(i*.65f-.6f,.4f,i%2*.3f);rock.transform.localScale=new(1.4f,.9f,1.2f);rock.GetComponent<Renderer>().sharedMaterial=material;}break;
                        case "Bench":
                            Box(root,"Seat",new(0,.55f,0),new(2.5f,.12f,.65f));Box(root,"Back",new(0,1,.3f),new(2.5f,.7f,.1f));
                            foreach(float x in new[]{-1f,1f})Box(root,"Leg",new(x,.25f,0),new(.1f,.5f,.5f));break;
                        case "Pillar":
                            Box(root,"Foot",new(0,.2f,0),new(2,.4f,2));Box(root,"Column",new(0,2.5f,0),new(1.25f,5,1.25f));Box(root,"Capital",new(0,5,0),new(2,.3f,2));break;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{Object.DestroyImmediate(root);}
            }
        }
        static void Box(GameObject root,string name,Vector3 p,Vector3 scale)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform,false);go.transform.localPosition=p;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;
        }
        static void EnsureCone()
        {
            const string path="Assets/Prefabs/Adventure/PineCrown.asset";
            cone=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(cone!=null)return;
            var vertices=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;
                int n=vertices.Count;vertices.Add(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));vertices.Add(Vector3.up);vertices.Add(new Vector3(Mathf.Cos(b),0,Mathf.Sin(b)));
                triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);
            }
            cone=new Mesh{name="Snow pine crown"};cone.SetVertices(vertices);cone.SetTriangles(triangles,0);cone.RecalculateNormals();cone.RecalculateBounds();AssetDatabase.CreateAsset(cone,path);
        }
    }
}

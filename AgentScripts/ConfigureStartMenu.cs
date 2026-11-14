using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConfigureStartMenu
{
    public static object Main()
    {
        if(Application.isPlaying)throw new System.InvalidOperationException("Exit Play mode first.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/BaiYu-Test.unity" || scene.isDirty)
            throw new System.InvalidOperationException("Open the saved BaiYu-Test scene before configuring its entry screen.");
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu==null)menu=new GameObject("SONICSPACE Entry").AddComponent<SonicSpaceStartMenu>();
        menu.player=Object.FindAnyObjectByType<PlayerController>();
        menu.menuShader=Shader.Find("Sound2/SonicSpace Point Menu");menu.glyphMaskShader=Shader.Find("Sound2/Menu Glyph Mask");
        menu.transitionSeconds=2.6f;
        menu.titleDelay=.55f;menu.characterInterval=.13f;menu.titleDotScale=1.65f;
        menu.titlePointDensity=.5f;menu.characterFloat=5;
        EditorUtility.SetDirty(menu);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new {scene=scene.path,menu=menu.name,font=GameUiTheme.Current.textFont.name};
    }
}

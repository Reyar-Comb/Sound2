using UnityEngine;

/// <summary>Small read-only view of the economy and health components.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerWallet),typeof(PlayerVitality))]
public sealed class PlayerStatusHUD : MonoBehaviour
{
    public bool visible=true;
    PlayerWallet wallet; PlayerVitality vitality;
    void Awake() { wallet=GetComponent<PlayerWallet>();vitality=GetComponent<PlayerVitality>(); }
    void OnGUI()
    {
        if(!visible) return;
        var rect=new Rect(Mathf.Max(0,Screen.width-220),16,204,86);
        GUI.Box(rect,GUIContent.none);
        GUI.Label(new Rect(rect.x+12,rect.y+9,180,24),"COINS  "+wallet.Balance);
        GUI.Label(new Rect(rect.x+12,rect.y+34,180,24),"HP  "+Mathf.CeilToInt(vitality.Health)+" / "+Mathf.CeilToInt(vitality.maximumHealth));
        if(vitality.IsDead) GUI.Label(new Rect(rect.x+12,rect.y+59,180,22),"Health depleted");
    }
}

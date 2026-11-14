using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Only used by audiotest; cache HUD state instead of allocating strings in every OnGUI event.</summary>
public sealed class AudioReactiveDemoControls : MonoBehaviour
{
    public AudioReactiveSonarEmitter radio;
    public AudioReactiveBallModes ball;
    AudioReactiveSonarEmitter ballAudio;
    AudioReactiveBallMode lastMode;
    bool lastBallReady, lastRadioReady, initialized;
    string modeText, statusText;
    Rect panel;
    void Update()
    {
        if (!SonicSpaceStartMenu.BlocksGameplay && Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame && radio != null)
            radio.Toggle();
        if (radio == null || ball == null) return;
        if (ballAudio == null) ballAudio = ball.GetComponent<AudioReactiveSonarEmitter>();
        bool b = ballAudio != null && ballAudio.analysis != null && ballAudio.analysis.IsReady;
        bool r = radio.analysis != null && radio.analysis.IsReady;
        if (!initialized || lastMode != ball.mode || b != lastBallReady || r != lastRadioReady)
        {
            modeText = "B: ball mode = " + ball.mode + "  |  V: radio power";
            statusText = "Ball audio: " + (b ? "ready" : "not assigned / not baked")
                + "  |  Radio audio: " + (r ? "ready" : "not assigned / not baked");
            lastMode = ball.mode; lastBallReady = b; lastRadioReady = r; initialized = true;
        }
        panel = new Rect(20, Screen.height - 120, 680, 100);
        PlayerUiPointMask.SetLegacyPanel(this, panel, true);
    }
    void OnGUI()
    {
        if (!initialized || radio == null || ball == null || Event.current.type != EventType.Repaint) return;
        GUI.Box(panel, "audiotest - original manual waves / audio peak waves");
        GUI.Label(new Rect(panel.x + 12, panel.y + 24, 650, 22), modeText);
        GUI.Label(new Rect(panel.x + 12, panel.y + 47, 650, 22), "Assign BallImpact / RadioLoop audio, then Bake peak envelope in Edit mode.");
        GUI.Label(new Rect(panel.x + 12, panel.y + 70, 650, 22), statusText);
    }
    void OnDisable() => PlayerUiPointMask.SetLegacyPanel(this, default, false);
}

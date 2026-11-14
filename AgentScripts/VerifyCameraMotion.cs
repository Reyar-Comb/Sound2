using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class VerifyCameraMotion
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static async Task Tick(int ms = 100)
    {
        int frame = Time.frameCount; await Task.Delay(ms);
        Check(Application.isPlaying && Time.frameCount > frame, "Player loop stalled.");
    }
    public static async Task<object> Main()
    {
        Check(Application.isPlaying, "Enter Play mode first.");
        var existing = UnityEngine.Object.FindObjectsByType<PlayerController>();
        var states = existing.Select(p => p.InputSuspended).ToArray();
        foreach (var p in existing) p.InputSuspended = true;
        var oldKeyboard = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>();
        var oldMouse = Mouse.current; var mouse = InputSystem.AddDevice<Mouse>();
        var cleanup = new List<GameObject>();
        try
        {
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            cleanup.Add(root); root.transform.position = Vector3.one * 1000;
            root.GetComponentInChildren<Camera>().enabled = false;
            root.GetComponentInChildren<AudioListener>().enabled = false;
            var player = root.GetComponent<PlayerController>();
            var motion = root.GetComponent<PlayerCameraMotion>();
            Check(motion != null && motion.enabled, "Prefab missing camera motion.");
            var view = player.ViewTransform;
            Vector3 rest = view.localPosition;
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState());
            var idle = new List<float>();
            for (int i = 0; i < 16; i++) { await Tick(100); idle.Add(motion.PositionOffset.y); }
            Check(idle.Max() - idle.Min() > .001f && idle.All(v => Mathf.Abs(v) <= motion.idleHeight + .0001f), "Idle breathing missing or excessive.");
            Check(motion.WalkingBlend < .001f && Mathf.Abs(motion.PositionOffset.x) < .0001f, "Idle produced walking sway.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            var walk = new List<Vector3>(); var roll = new List<float>();
            Vector3 before = root.transform.position;
            for (int i = 0; i < 20; i++) { await Tick(80); walk.Add(motion.PositionOffset); roll.Add(motion.RotationOffset.z); }
            Check(root.transform.position.z > before.z + 3 && motion.WalkingBlend > .9f, "Actual movement did not enable walking motion.");
            Check(walk.Max(v => v.y) - walk.Min(v => v.y) > .008f && walk.Max(v => v.x) - walk.Min(v => v.x) > .004f, "Walking bob/sway missing.");
            Check(walk.All(v => v.magnitude < .04f) && roll.All(v => Mathf.Abs(v) <= .181f), "Walking motion exceeded gentle limits.");

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); cleanup.Add(wall);
            wall.transform.position = root.transform.position + Vector3.forward * .9f;
            wall.transform.localScale = new Vector3(6, 6, .2f); Physics.SyncTransforms();
            await Tick(1200);
            Check(motion.WalkingBlend < .05f, "Holding movement against a wall kept walking bob active.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); wall.SetActive(false);
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(15, -8) });
            await Tick();
            Check(Quaternion.Angle(player.AimLocalRotation, Quaternion.identity) > .01f, "Look input test did not rotate the aim.");
            Check(Quaternion.Angle(view.localRotation, player.AimLocalRotation) < .3f, "Motion overwrote or accumulated on mouse look.");
            player.InputSuspended = true; await Tick(600);
            Check(motion.PositionOffset.magnitude < .0001f && motion.RotationOffset.magnitude < .001f, "Menu pause did not settle camera.");
            player.InputSuspended = false;
            var settings = new SerializedObject(player); settings.FindProperty("useGravity").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); await Tick(600);
            Check(root.GetComponent<CharacterController>().velocity.y < -1 && motion.PositionOffset.magnitude < .0001f, "Airborne player retained walking motion.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            motion.enabled = false;
            Check(Vector3.Distance(view.localPosition, rest) < .00001f && Quaternion.Angle(view.localRotation, player.AimLocalRotation) < .001f, "Disable did not restore the neutral pose.");
            return new { passed = true, idleRange = idle.Max() - idle.Min(), walkVerticalRange = walk.Max(v => v.y) - walk.Min(v => v.y),
                checks = new[] { "idle breathing", "real movement bob and sway", "gentle amplitude bounds", "blocked movement settles", "mouse look preserved", "menu settles", "airborne settles", "disable restores neutral" } };
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            if (oldMouse != null && oldMouse.added) oldMouse.MakeCurrent();
            foreach (var go in cleanup) if (go != null) UnityEngine.Object.Destroy(go);
            for (int i = 0; i < existing.Length; i++) if (existing[i] != null) existing[i].InputSuspended = states[i];
        }
    }
}

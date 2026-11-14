using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Play-mode behavioural checks against the configured player prefab, with real queued mouse input.
public static class VerifyInteraction
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static async Task Tick(int ms = 100)
    {
        int frame = Time.frameCount;
        await Task.Delay(ms);
        Check(Application.isPlaying && Time.frameCount > frame, "Player loop did not advance.");
    }

    public static async Task<object> Main()
    {
        Check(Application.isPlaying, "Enter Play mode first.");
        var existing = UnityEngine.Object.FindObjectsByType<PlayerController>();
        var suspended = new bool[existing.Length];
        for (int i = 0; i < existing.Length; i++) { suspended[i] = existing[i].InputSuspended; existing[i].InputSuspended = true; }
        var cleanup = new List<GameObject>();
        var previousMouse = Mouse.current;
        var mouse = InputSystem.AddDevice<Mouse>();
        var previousKeyboard = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var checks = new List<string>();
        try
        {
            var player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            cleanup.Add(player);
            player.transform.position = new Vector3(1000, 1000, 1000);
            player.GetComponentInChildren<AudioListener>().enabled = false;
            var controller = player.GetComponent<PlayerController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("useGravity").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var interaction = player.GetComponent<PlayerInteraction>();
            var pickup = player.GetComponent<PlayerPickup>();
            Check(interaction != null && interaction.viewCamera != null, "Shared prefab is missing interaction setup.");
            await Tick();
            Vector3 origin = interaction.viewCamera.transform.position;
            Vector3 forward = interaction.viewCamera.transform.forward, right = interaction.viewCamera.transform.right;
            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cleanup.Add(targetObject);
            targetObject.transform.localScale = Vector3.one * .2f;
            targetObject.transform.position = origin + forward * 1.4f;
            var target = targetObject.AddComponent<InteractableObject>();
            int clicks = 0;
            target.onInteract.AddListener(_ => clicks++);
            Physics.SyncTransforms();
            await Tick(100);
            Check(interaction.FocusedTarget == target, "Nearby aimed target did not acquire focus.");
            Check(interaction.RingProgress > 0 && interaction.RingProgress < 1, "Ring did not animate from zero.");
            await Tick(400);
            Check(interaction.RingProgress > .99f, "Ring did not finish expanding.");
            Check(interaction.GetComponentInChildren<Canvas>().enabled && interaction.PointClearZone.z >= 14, "HUD or per-camera mask missing.");
            checks.Add("focus + nonlinear ring expansion + HUD mask");

            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            await Tick();
            Check(clicks == 1, "Left click did not invoke generic event once.");
            await Tick();
            Check(clicks == 1, "Held mouse repeated interaction.");
            InputSystem.QueueStateEvent(mouse, new MouseState());
            await Tick();
            checks.Add("left mouse event fires once per press");

            controller.InputSuspended = true;
            await Tick();
            Check(interaction.FocusedTarget == null && interaction.PointClearZone == Vector4.zero
                && !interaction.GetComponentInChildren<Canvas>().enabled && !interaction.TryInteractFocused(), "Menu suspension left HUD or interaction active.");
            controller.InputSuspended = false;
            await Tick(400);
            target.interactionEnabled = false;
            Check(!interaction.TryInteractFocused(), "Disabled target accepted a click.");
            target.interactionEnabled = true;
            checks.Add("menu suspension + disabled target");

            targetObject.transform.position = origin + forward * 1.4f + right * .5f;
            Physics.SyncTransforms();
            await Tick(400);
            Check(interaction.FocusedTarget == null && interaction.RingProgress == 0, "Off-axis target stayed focused.");
            targetObject.transform.position = origin + forward * 4;
            Physics.SyncTransforms();
            await Tick();
            Check(interaction.FocusedTarget == null, "Out-of-range target stayed focused.");
            targetObject.transform.position = origin - forward;
            Physics.SyncTransforms();
            await Tick();
            Check(interaction.FocusedTarget == null, "Target behind camera acquired focus.");
            targetObject.transform.position = origin + forward * 1.4f;
            Physics.SyncTransforms();
            await Tick(400);
            checks.Add("angle + distance + behind-camera rejection");

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cleanup.Add(wall);
            wall.transform.localScale = Vector3.one * .4f;
            wall.transform.position = origin + forward * .7f;
            Physics.SyncTransforms();
            Check(!interaction.TryInteractFocused(), "Click crossed a newly inserted wall.");
            await Tick();
            Check(interaction.FocusedTarget == null && interaction.RingProgress == 0, "Occluded ring remained visible.");
            wall.SetActive(false);
            targetObject.SetActive(false);
            checks.Add("occlusion + click-time revalidation");

            var itemObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupItem.prefab"));
            cleanup.Add(itemObject);
            var item = itemObject.GetComponent<PickupItem>();
            var body = itemObject.GetComponent<Rigidbody>();
            body.useGravity = false;
            itemObject.transform.position = origin + forward * 1.4f;
            var nearerObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupItem.prefab"));
            cleanup.Add(nearerObject);
            nearerObject.GetComponent<Rigidbody>().useGravity = false;
            nearerObject.transform.position = origin + forward * .8f + right * .5f;
            Physics.SyncTransforms();
            await Tick(400);
            Check(interaction.FocusedTarget == item, "A nearer off-axis item stole focus.");
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            await Tick();
            Check(pickup.HeldItem == item, "Mouse did not pick up the indicated item.");
            Check(interaction.FocusedTarget == null, "Held item still advertised interaction.");
            InputSystem.QueueStateEvent(mouse, new MouseState());
            await Tick();
            pickup.throwSpeed = pickup.throwUpwardSpeed = 0;
            item.pickupCooldown = 5;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            await Tick();
            Check(pickup.HeldItem == null && !item.CanPickUp, "Mouse throw did not preserve pickup cooldown.");
            InputSystem.QueueStateEvent(mouse, new MouseState());
            await Tick();
            Check(interaction.FocusedTarget != item, "Cooldown item advertised interaction.");
            itemObject.SetActive(false);
            nearerObject.SetActive(false);
            checks.Add("exact-target mouse pickup + throw + cooldown");

            targetObject.SetActive(true);
            Physics.SyncTransforms();
            await Tick(400);
            UnityEngine.Object.Destroy(targetObject);
            await Tick();
            Check(interaction.FocusedTarget == null && interaction.RingProgress == 0, "Destroyed target left stale focus.");
            interaction.enabled = false;
            Check(interaction.PointClearZone == Vector4.zero && !interaction.GetComponentInChildren<Canvas>().enabled,
                "Disabling the interaction component left HUD/mask active.");
            checks.Add("destroyed target + component cleanup");
            return new { passed = true, checks };
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keyboard);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            foreach (var go in cleanup) if (go != null) UnityEngine.Object.Destroy(go);
            for (int i = 0; i < existing.Length; i++) if (existing[i] != null) existing[i].InputSuspended = suspended[i];
        }
    }
}

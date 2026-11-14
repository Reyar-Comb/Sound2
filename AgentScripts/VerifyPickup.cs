using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Run in Play mode via unity command script run. All test objects/devices are removed in finally.
public static class VerifyPickup
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static async Task Tick(int milliseconds = 150)
    {
        int frame = Time.frameCount;
        await Task.Delay(milliseconds);
        Check(Application.isPlaying && Time.frameCount > frame, "Player loop is not advancing.");
    }

    static async Task Press(Keyboard keyboard, Key key = Key.None)
    {
        keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, key == Key.None ? new KeyboardState() : new KeyboardState(key));
        await Tick(80);
    }

    public static async Task<object> Main()
    {
        Check(Application.isPlaying, "Enter Play mode first.");
        var existingPlayers = UnityEngine.Object.FindObjectsByType<PlayerController>();
        var suspended = new bool[existingPlayers.Length];
        for (int i = 0; i < existingPlayers.Length; i++)
        {
            suspended[i] = existingPlayers[i].InputSuspended;
            existingPlayers[i].InputSuspended = true;
        }
        var cleanup = new List<GameObject>();
        var previousKeyboard = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            var player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            cleanup.Add(player);
            player.transform.position = new Vector3(1000, 1000, 1000);
            player.GetComponentInChildren<Camera>().enabled = false;
            player.GetComponentInChildren<AudioListener>().enabled = false;
            var controller = player.GetComponent<PlayerController>();
            var picker = player.GetComponent<PlayerPickup>();
            Check(picker != null && picker.enabled && picker.interactionKey == Key.E, "Player prefab not configured.");
            Check(picker.pickupRange.isTrigger && picker.pickupRange.transform.parent == player.transform, "Range is not a separate trigger.");
            Vector3 origin = picker.viewTransform.position;
            var itemObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupItem.prefab"));
            cleanup.Add(itemObject);
            var item = itemObject.GetComponent<PickupItem>();
            var body = item.GetComponent<Rigidbody>();
            body.useGravity = false;
            item.pickupCooldown = 5;
            item.transform.position = origin + Vector3.forward * 1.4f;
            Physics.SyncTransforms();
            await Press(keyboard);
            await Press(keyboard, Key.E);
            Check(picker.HeldItem == item && body.isKinematic && !item.GetComponent<Collider>().enabled, "E did not pick up the item.");
            await Press(keyboard);
            Vector3 local = picker.viewTransform.InverseTransformPoint(item.transform.position);
            Check(Mathf.Abs(local.x) < .001f && local.y < 0 && local.z > 0, "Held item is not lower-center.");
            picker.viewTransform.localRotation = Quaternion.Euler(20, 30, 0);
            // Suspend movement so PlayerController does not overwrite the camera rotation during this check.
            controller.InputSuspended = true;
            await Tick();
            Check(Vector3.Distance(item.transform.position, picker.holdPoint.position) < .001f, "Held item did not follow camera.");
            picker.viewTransform.localRotation = Quaternion.identity;
            controller.InputSuspended = false;
            picker.throwSpeed = 0;
            picker.throwUpwardSpeed = 0;
            await Press(keyboard, Key.E);
            Check(picker.HeldItem == null && !body.isKinematic && item.GetComponent<Collider>().enabled, "Second E did not throw/restore physics.");
            Check(!item.CanPickUp && !picker.TryPickUpNearest(), "Airborne item can be immediately picked up.");
            await Press(keyboard);
            await Press(keyboard, Key.E);
            Check(picker.HeldItem == null, "Repeated E bypassed cooldown.");
            await Press(keyboard);

            // A real wall collision must unlock early (while the five-second timeout is still active).
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cleanup.Add(wall);
            wall.transform.position = origin + Vector3.forward * 1.7f;
            wall.transform.localScale = new Vector3(3, 3, .2f);
            Physics.SyncTransforms();
            body.linearVelocity = Vector3.forward * 6;
            await Tick(300);
            Check(item.CanPickUp, "Wall collision did not unlock pickup early.");
            Check(picker.TryPickUpNearest(), "Item cannot be picked up after hitting a wall.");
            wall.SetActive(false);

            // Floor contact is not a wall contact.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cleanup.Add(floor);
            floor.transform.position = origin + new Vector3(0, -.7f, .9f);
            floor.transform.localScale = new Vector3(3, .2f, 3);
            Physics.SyncTransforms();
            Check(picker.TryThrow(), "Could not throw before floor test.");
            body.linearVelocity = Vector3.down * 5;
            await Tick(300);
            Check(body.position.y < origin.y - .3f && !item.CanPickUp, "Floor contact incorrectly unlocked pickup.");
            floor.SetActive(false);
            item.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(itemObject);

            // Fresh item tests cooldown expiry without leaving/re-entering the trigger and editable radius.
            itemObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupItem.prefab"));
            cleanup.Add(itemObject);
            item = itemObject.GetComponent<PickupItem>();
            body = item.GetComponent<Rigidbody>();
            body.useGravity = false;
            item.pickupCooldown = .4f;
            item.transform.position = origin + Vector3.forward * 1.4f;
            Physics.SyncTransforms();
            picker.pickupRange.radius = .4f;
            Check(!picker.TryPickUpNearest(), "Pickup ignores edited range collider radius.");
            picker.pickupRange.radius = 2;
            wall.transform.position = origin + Vector3.forward * .7f;
            wall.SetActive(true);
            Physics.SyncTransforms();
            Check(!picker.TryPickUpNearest(), "Pickup reaches through walls.");
            wall.SetActive(false);
            Physics.SyncTransforms();
            Check(picker.TryPickUpNearest() && picker.TryThrow(), "Cooldown expiry test setup failed.");
            Check(!item.CanPickUp, "Cooldown was not applied.");
            await Tick(550);
            Check(item.CanPickUp && picker.TryPickUpNearest(), "Item remained locked after cooldown expired inside range.");

            wall.transform.position = origin;
            wall.SetActive(true);
            Physics.SyncTransforms();
            Check(!picker.TryThrow() && picker.HeldItem == item, "Throw released item into obstructing geometry.");
            wall.SetActive(false);
            Physics.SyncTransforms();
            picker.throwSpeed = 10;
            picker.throwUpwardSpeed = 1;
            Check(picker.TryThrow() && body.linearVelocity.z > 9, "Throw has incorrect velocity.");
            Check(Physics.GetIgnoreCollision(item.GetComponent<Collider>(), player.GetComponent<CharacterController>()), "Thrower collision was not ignored at release.");
            await Tick(150);
            Check(!Physics.GetIgnoreCollision(item.GetComponent<Collider>(), player.GetComponent<CharacterController>()), "Thrower collision was not restored after separation.");
            return new { passed = true, checks = new[] { "prefab wiring", "E pickup/throw", "camera lower-center hold", "airborne cooldown", "wall unlock", "floor stays locked", "editable collider range", "no pickup through walls", "cooldown expiry in range", "blocked throw", "throw velocity", "thrower collision restoration" } };
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            foreach (var go in cleanup) if (go != null) UnityEngine.Object.Destroy(go);
            for (int i = 0; i < existingPlayers.Length; i++)
                if (existingPlayers[i] != null) existingPlayers[i].InputSuspended = suspended[i];
        }
    }
}

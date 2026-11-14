using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VerifyPushableProps
{
    static readonly List<string> checks = new();
    static void Check(bool value, string label) { checks.Add((value ? "PASS: " : "FAIL: ") + label); if (!value) throw new Exception(label); }
    static async Task Tick(int milliseconds = 100)
    {
        int frame = Time.frameCount; await Task.Delay(milliseconds);
        if (!Application.isPlaying || Time.frameCount <= frame) throw new Exception("Player loop did not advance");
    }
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
    static PatrolMonster Monster(Transform parent, Vector3 position, RuntimePatrolNavigation nav)
    {
        var go = new GameObject("Push hearing fixture"); go.SetActive(false); go.transform.SetParent(parent); go.transform.position = position;
        var monster = go.AddComponent<PatrolMonster>(); monster.navigation = nav; monster.soundPriorityMemory = 0; monster.investigateSpeed = .1f;
        go.SetActive(true); return monster;
    }
    static async Task Drive(Keyboard keyboard, Key key, int milliseconds)
    {
        for (int elapsed = 0; elapsed < milliseconds; elapsed += 50)
        {
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, key == Key.None ? new KeyboardState() : new KeyboardState(key));
            InputSystem.Update();
            await Tick(50);
        }
    }
    static async Task PushToward(Keyboard keyboard, PlayerController player, Rigidbody target, int milliseconds)
    {
        var yaw = typeof(PlayerController).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int elapsed = 0; elapsed < milliseconds; elapsed += 50)
        {
            var direction = target.position - player.transform.position;
            yaw.SetValue(player, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
            await Drive(keyboard, Key.W, 50);
        }
    }
    public static async Task<object> Play()
    {
        checks.Clear(); Check(Application.isPlaying, "Tests run in Play Mode");
        var roots = Enumerable.Range(0, SceneManager.sceneCount).SelectMany(i => SceneManager.GetSceneAt(i).GetRootGameObjects()).Where(g => g.activeSelf).ToArray();
        float originalTimeScale = Time.timeScale; bool originalPause = AudioListener.pause;
        var oldKeyboard = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>();
        var background = InputSystem.settings.backgroundBehavior;
        var routing = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var oldLock = Cursor.lockState; bool oldVisible = Cursor.visible;
        foreach (var go in roots) go.SetActive(false);
        Time.timeScale = 1; AudioListener.pause = false;
        var root = new GameObject("Push verification"); root.SetActive(false);
        var center = new Vector3(1500, 0, 1500);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform); floor.transform.position = center + new Vector3(10, -.5f, 0); floor.transform.localScale = new Vector3(50, 1, 25);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(root.transform); wall.transform.position = center + new Vector3(0, 1.5f, 4); wall.transform.localScale = new Vector3(6, 3, .25f);
        var navigation = root.AddComponent<RuntimePatrolNavigation>(); navigation.geometryRoot = root.transform;
        root.SetActive(true); Physics.SyncTransforms(); navigation.Rebuild();
        var near = Monster(root.transform, center + new Vector3(3, .05f, 0), navigation);
        var far = Monster(root.transform, center + new Vector3(25, .05f, 0), navigation);
        var player = Object.Instantiate(Prefab("Player"), center + new Vector3(0, .1f, -1.7f), Quaternion.identity, root.transform);
        var controller = player.GetComponent<PlayerController>();
        foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>())
            if (!(behaviour is PlayerController) && !(behaviour is SonarPulseField)) behaviour.enabled = false;
        foreach (var cam in player.GetComponentsInChildren<Camera>()) cam.enabled = false;
        foreach (var listener in player.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
        player.GetComponent<FootstepEmitter>().enabled = false;
        var move = (InputActionReference)typeof(PlayerController).GetField("moveAction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        var testActions = Object.Instantiate(move.action.actionMap.asset);
        testActions.devices = new InputDevice[] { keyboard };
        var testReferences = new List<InputActionReference>();
        foreach (string fieldName in new[] { "moveAction", "lookAction", "verticalAction" })
        {
            var member = typeof(PlayerController).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var originalReference = (InputActionReference)member.GetValue(controller);
            var replacement = InputActionReference.Create(testActions.FindAction(originalReference.action.id));
            member.SetValue(controller, replacement); testReferences.Add(replacement);
            if (fieldName == "moveAction") move = replacement;
        }
        typeof(PlayerController).GetField("jumpAction", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, move.action.actionMap.FindAction("Jump"));
        testActions.Enable(); controller.InputSuspended = false;
        var field = player.GetComponentInChildren<SonarPulseField>(); field.enabled = true;
        var s = player.GetComponentInChildren<SphericalSonar>(); if (s != null) s.automatic = false;
        int movementEvents = 0, impactEvents = 0; GameplayNoiseEvent lastMove = default;
        PushableProp watched = null;
        Action<GameplayNoiseEvent> observe = noise =>
        {
            if (watched != null && noise.source == watched) { movementEvents++; lastMove = noise; }
            if (watched != null && noise.source == watched.GetComponent<CollisionSonarEmitter>()) impactEvents++;
        };
        GameplayNoise.Emitted += observe;
        try
        {
            await Drive(keyboard, Key.None, 300);
            Check(navigation.Ready && near.NavigationReady && far.NavigationReady, "Near and far monsters use a real NavMesh");
            foreach (var name in new[] { "ShoppingCart", "TrashBin" })
            {
                var go = Object.Instantiate(Prefab(name), center + Vector3.up * .03f, Quaternion.identity, root.transform);
                watched = go.GetComponent<PushableProp>(); var body = go.GetComponent<Rigidbody>(); var sonar = go.GetComponent<SonarObjectEmitter>();
                sonar.pulseField = field;
                Check(watched != null && !body.isKinematic && go.GetComponent<PickupItem>() == null, name + " is dynamic and pushable without pickup");
                Check(!sonar.automatic && sonar.toggleKey == Key.None && !go.GetComponent<SonarCueSource>().generateDefaultPing, name + " has no timer, hotkey or synthesized ping");
                Check(go.GetComponentsInChildren<MeshCollider>().All(c => c.convex), name + " uses dynamic-compatible colliders");
                controller.Teleport(center + new Vector3(0, .1f, -1.7f));
                await Drive(keyboard, Key.None, 500);
                int idle = watched.MovementEmissions; await Drive(keyboard, Key.None, 300);
                Check(watched.MovementEmissions == idle, name + " is silent while resting");
                int pulses = sonar.Emissions, noises = movementEvents, heard = near.HeardSounds, distant = far.HeardSounds;
                Vector3 start = body.position;
                await PushToward(keyboard, controller, body, 1500);
                Check(watched.PushRequests > 0 && body.position.z > start.z + .25f, name + " moves from real W input and CharacterController contact (travel " + (body.position.z - start.z) + ", requests " + watched.PushRequests + ", grounded " + watched.Grounded + ", player " + (player.transform.position - center) + ", input " + move.action.ReadValue<Vector2>() + ", suspended " + controller.InputSuspended + ")");
                Check(sonar.Emissions > pulses && movementEvents > noises && lastMove.kind == GameplayNoiseKind.Movement, name + " actual motion emits shared sonar and Movement hearing events (sonar " + sonar.Emissions + "/" + pulses + ", noise " + movementEvents + "/" + noises + ")");
                Check(near.HeardSounds > heard && near.LastHeardKind == GameplayNoiseKind.Movement && Vector3.Distance(near.LastHeardPosition, lastMove.position) < .01f, name + " nearby monster investigates the moving sound source");
                Check(far.HeardSounds == distant, name + " distant monster does not hear movement");
                Check(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude < watched.maximumPushSpeed + .3f, name + " respects the configured push speed");
                await Drive(keyboard, Key.None, 1500);
                idle = watched.MovementEmissions; await Drive(keyboard, Key.None, 400);
                Check(watched.MovementEmissions == idle && Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude < .12f, name + " stops and becomes silent after release");
                body.position = center + new Vector3(0, .03f, 2.5f); body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                controller.Teleport(center + new Vector3(0, 1.05f, 1.25f)); Physics.SyncTransforms();
                await PushToward(keyboard, controller, body, 4000);
                var front = go.GetComponentsInChildren<Collider>().Max(c => c.bounds.max.z);
                Check(front < wall.GetComponent<Collider>().bounds.min.z + .06f, name + " cannot be pushed through a solid wall");
                idle = watched.MovementEmissions; await PushToward(keyboard, controller, body, 600);
                Check(watched.MovementEmissions == idle, name + " pushing against a wall without travel does not repeat movement sounds");
                Check(Vector3.Dot(go.transform.up, Vector3.up) > .99f, name + " remains upright");
                await Drive(keyboard, Key.None, 100);
                controller.Teleport(center + new Vector3(-4, .1f, -3));
                int beforeWall = impactEvents;
                body.position = center + new Vector3(0, .02f, name == "ShoppingCart" ? 3.1f : 3.4f); body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
                await Drive(keyboard, Key.None, 150);
                body.linearVelocity = Vector3.forward * 4;
                await Drive(keyboard, Key.None, 650);
                Check(impactEvents > beforeWall && go.GetComponent<CollisionSonarEmitter>().LastImpactSpeed >= .6f, name + " a strong real wall collision reuses impact sonar and hearing");
                body.position = center + new Vector3(0, 3, 0); body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
                idle = watched.MovementEmissions; await Drive(keyboard, Key.None, 250);
                Check(watched.MovementEmissions == idle, name + " falling does not produce rolling or scraping sounds");
                int beforeDrop = impactEvents; await Drive(keyboard, Key.None, 850);
                Check(impactEvents > beforeDrop, name + " landing emits impact sonar and hearing");
                sonar.enabled = false; body.position = center + new Vector3(0, .03f, 0); body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
                controller.Teleport(center + new Vector3(0, .1f, -1.7f));
                await Drive(keyboard, Key.None, 250); noises = movementEvents;
                await PushToward(keyboard, controller, body, 1200);
                Check(movementEvents > noises, name + " AI hearing still works when visual emitter is disabled");
                await Drive(keyboard, Key.None, 100); Object.Destroy(go); watched = null; await Tick();
            }
            var chair = Object.Instantiate(Prefab("Chair"), center, Quaternion.identity, root.transform);
            var chairStart = chair.transform.position; controller.Teleport(center + new Vector3(0, .1f, -1.3f));
            await Drive(keyboard, Key.W, 700); await Drive(keyboard, Key.None, 100);
            Check(chair.GetComponent<PushableProp>() == null && chair.transform.position == chairStart, "Other furniture remains static");
            Object.Destroy(chair);
            var ball = Object.Instantiate(Prefab("PickupBouncyBall"), center + Vector3.up * 2, Quaternion.identity, root.transform);
            Check(ball.GetComponent<PickupItem>() != null && ball.GetComponent<CollisionSonarEmitter>() != null && ball.GetComponent<PushableProp>() == null, "Existing pickup objects keep their original interaction components");
            var normalSonar = ball.GetComponent<SonarObjectEmitter>(); normalSonar.pulseField = field;
            int existing = normalSonar.Emissions; Check(normalSonar.EmitWithRange(4) && normalSonar.Emissions == existing + 1, "Existing one-argument sonar API still emits");
            var clip = AudioClip.Create("Temporary clip routing test", 2205, 1, 22050, false);
            Check(normalSonar.EmitWithClip(4, clip) && normalSonar.GetComponent<AudioSource>().isPlaying, "Movement clip override reaches existing spatial audio output");
            normalSonar.GetComponent<AudioSource>().Stop(); Object.Destroy(clip);
            return new { passed = true, checks };
        }
        finally
        {
            GameplayNoise.Emitted -= observe;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.RemoveDevice(keyboard); if (oldKeyboard != null) oldKeyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = routing;
            testActions.Disable(); Object.Destroy(testActions);
            foreach (var reference in testReferences) Object.Destroy(reference);
            Object.Destroy(root);
            foreach (var go in roots) if (go != null) go.SetActive(true);
            Time.timeScale = originalTimeScale; AudioListener.pause = originalPause; Cursor.lockState = oldLock; Cursor.visible = oldVisible;
            Directory.CreateDirectory("Logs/PushableProps"); File.WriteAllLines("Logs/PushableProps/play.txt", checks);
        }
    }
}

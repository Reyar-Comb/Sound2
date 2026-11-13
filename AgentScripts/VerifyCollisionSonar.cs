oiusing System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class VerifyCollisionSonar
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static async Task Tick(int milliseconds)
    {
        int before = Time.frameCount;
        await Task.Delay(milliseconds);
        Check(Application.isPlaying && Time.frameCount > before, "Play mode is not advancing.");
    }

    static async Task Hit(Rigidbody body, Vector3 wallPosition)
    {
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = wallPosition - Vector3.forward;
        body.transform.position = body.position;
        Physics.SyncTransforms();
        await Tick(40);
        body.linearVelocity = Vector3.forward * 8;
        bool reflected = false;
        for (int i = 0; i < 40; i++)
        {
            await Tick(15);
            if (body.linearVelocity.z < -1) { reflected = true; break; }
        }
        Check(reflected, "Test ball did not physically hit/bounce from the wall.");
        body.linearVelocity = Vector3.zero;
    }

    public static async Task<object> Main()
    {
        Check(Application.isPlaying, "Enter BlackBox Play mode first.");
        var field = UnityEngine.Object.FindAnyObjectByType<SonarPulseField>();
        var sceneBall = GameObject.Find("PickupBouncyBall");
        var picker = UnityEngine.Object.FindAnyObjectByType<PlayerPickup>();
        Check(field != null && sceneBall != null && picker != null, "Scene wiring missing.");
        var sceneBody = sceneBall.GetComponent<Rigidbody>();
        sceneBody.isKinematic = true;
        var sceneEmitter = sceneBall.GetComponent<SonarObjectEmitter>();
        Check(sceneEmitter.pulseField == field && !sceneEmitter.automatic
            && sceneEmitter.toggleKey == UnityEngine.InputSystem.Key.None, "Scene emitter wiring/defaults incorrect.");

        GameObject testBall = null, wall = null;
        try
        {
            testBall = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));
            var body = testBall.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            body.useGravity = false;
            body.position = new Vector3(1000, 1000, 999);
            testBall.transform.position = body.position;
            var collision = testBall.GetComponent<CollisionSonarEmitter>();
            var emitter = testBall.GetComponent<SonarObjectEmitter>();
            emitter.playSound = false;
            // The saved prefab may have user-tuned thresholds; this fixture exercises known boundary values.
            collision.minimumImpactSpeed = .5f;
            collision.emissionInterval = .5f;
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(1000, 1000, 1000);
            wall.transform.localScale = new Vector3(3, 3, .2f);
            Physics.SyncTransforms();
            await Tick(300);
            Check(emitter.pulseField == field, "Fresh prefab did not find the scene field.");
            Check(collision.Emissions == 0 && emitter.Emissions == 0, "Ball emitted without a collision.");
            await Hit(body, wall.transform.position);
            Check(collision.Emissions == 1 && emitter.Emissions == 1 && collision.LastImpactSpeed > 1, "Impact did not emit exactly one pulse.");
            await Hit(body, wall.transform.position);
            Check(collision.Emissions == 1, "Collision cooldown did not suppress a rapid second impact.");
            await Tick(600);
            await Hit(body, wall.transform.position);
            Check(collision.Emissions == 2, "Emission did not resume after cooldown.");
            await Tick(600);
            collision.minimumImpactSpeed = 100;
            await Hit(body, wall.transform.position);
            Check(collision.Emissions == 2, "Minimum impact speed was ignored.");
            collision.minimumImpactSpeed = .5f;
            collision.collisionLayers = 0;
            await Hit(body, wall.transform.position);
            Check(collision.Emissions == 2, "Collision layer mask was ignored.");
            collision.collisionLayers = ~0;
            collision.enabled = false;
            await Hit(body, wall.transform.position);
            Check(collision.Emissions == 2, "Disabled collision emitter still emitted.");
            collision.enabled = true;
            await Hit(body, wall.transform.position);
            Check(collision.Emissions == 3, "Re-enabled collision emitter did not recover.");

            // Verify the real scene instance and its pickup/throw integration, then freeze a visible pulse for capture.
            field.ClearPulses();
            var sceneCollision = sceneBall.GetComponent<CollisionSonarEmitter>();
            int before = sceneCollision.Emissions;
            Check(picker.TryPickUpNearest() && picker.HeldItem == sceneBall.GetComponent<PickupItem>(), "Scene ball cannot be picked up.");
            picker.throwSpeed = Mathf.Max(picker.throwSpeed, sceneCollision.minimumImpactSpeed + 5);
            Check(picker.TryThrow(), "Scene ball cannot be thrown.");
            for (int i = 0; i < 60 && sceneCollision.Emissions == before; i++) await Tick(15);
            Check(sceneCollision.Emissions > before && sceneBall.GetComponent<PickupItem>().CanPickUp,
                "Thrown scene ball did not emit/unlock after hitting the wall.");
            Check(Vector3.Distance(field.LastOrigin, sceneEmitter.LastOrigin) < .001f
                && Vector3.Distance(field.LastOrigin, picker.viewTransform.position) > .3f,
                "Pulse did not originate at the ball.");
            Check(field.LastHitCount > 0 && Shader.GetGlobalFloat("_SonarActive") > .5f, "Renderer did not receive sonar/occlusion data.");
            var source = sceneBall.GetComponent<AudioSource>();
            Check(source.spatialBlend == 1 && source.spatialize, "Collision sound is not spatialized.");
            sceneBody.linearVelocity = Vector3.zero;
            sceneBody.isKinematic = true;
            await Tick(200);
            EditorApplication.isPaused = true;
            return new { passed = true, checks = new[] { "prefab binding", "no automatic emissions", "physical impact emission",
                "cooldown and recovery", "speed threshold", "collision layers", "disabled component", "pickup and throw",
                "wall pickup unlock", "ball-origin pulse", "renderer/occlusion data", "spatial audio configuration" },
                sceneEmissions = sceneCollision.Emissions, field.LastHitCount };
        }
        finally
        {
            if (testBall != null) UnityEngine.Object.Destroy(testBall);
            if (wall != null) UnityEngine.Object.Destroy(wall);
            // The real ball is restored when the verification Play session exits.
        }
    }
}

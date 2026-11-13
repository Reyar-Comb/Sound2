using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class VerifyCollisionSonarConfiguration
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static async Task Tick(int milliseconds)
    {
        int frame = Time.frameCount;
        await Task.Delay(milliseconds);
        Check(Application.isPlaying && Time.frameCount > frame, "Player loop stopped.");
    }

    static async Task Hit(Rigidbody body, Vector3 wall, float speed)
    {
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = wall - Vector3.forward;
        body.transform.position = body.position;
        Physics.SyncTransforms();
        await Tick(60);
        body.linearVelocity = Vector3.forward * speed;
        bool bounced = false;
        for (int i = 0; i < 80; i++)
        {
            await Tick(15);
            if (body.linearVelocity.z < -.1f) { bounced = true; break; }
        }
        Check(bounced, "Wall collision was not physically observed.");
        body.linearVelocity = Vector3.zero;
    }

    public static async Task<object> Main()
    {
        Check(Application.isPlaying, "Run in BlackBox Play mode.");
        var ball = GameObject.Find("PickupBouncyBall");
        var collision = ball.GetComponent<CollisionSonarEmitter>();
        var emitter = ball.GetComponent<SonarObjectEmitter>();
        var body = ball.GetComponent<Rigidbody>();
        Check(!emitter.automatic && emitter.toggleKey == UnityEngine.InputSystem.Key.None, "Scene still permits autonomous emissions.");
        int pulses = emitter.Emissions;
        await Tick(3500);
        int idlePulses = emitter.Emissions - pulses;
        Check(idlePulses == 0, "Stationary ball emitted without impact.");
        Vector3 start = body.position;
        // Roll along the flat floor below the user's impact threshold; it may also rebound from the room wall.
        body.linearVelocity = Vector3.right * 1.5f;
        body.angularVelocity = Vector3.back * 7.5f;
        await Tick(3500);
        int rollingPulses = emitter.Emissions - pulses;
        Check(Vector3.Distance(start, body.position) > .2f, "The rolling test did not move the ball.");
        Check(rollingPulses == 0, "Rolling/weak room contacts emitted a pulse.");
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        GameObject testBall = null, wall = null;
        try
        {
            testBall = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));
            var testBody = testBall.GetComponent<Rigidbody>();
            testBody.useGravity = false;
            testBody.interpolation = RigidbodyInterpolation.None;
            testBody.position = new Vector3(1000, 1000, 999);
            testBall.transform.position = testBody.position;
            var testCollision = testBall.GetComponent<CollisionSonarEmitter>();
            var testEmitter = testBall.GetComponent<SonarObjectEmitter>();
            testEmitter.playSound = false;
            Check(Mathf.Approximately(testCollision.minimumImpactSpeed, collision.minimumImpactSpeed)
                && Mathf.Approximately(testEmitter.waveSpeed, emitter.waveSpeed)
                && Mathf.Approximately(testEmitter.afterglow, emitter.afterglow), "Prefab lost the user's tuning.");
            Check(!testEmitter.automatic && testEmitter.toggleKey == UnityEngine.InputSystem.Key.None, "Prefab permits autonomous emissions.");
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(1000, 1000, 1000);
            wall.transform.localScale = new Vector3(3, 3, .2f);
            Physics.SyncTransforms();
            await Tick(100);
            await Hit(testBody, wall.transform.position, collision.minimumImpactSpeed * .8f);
            Check(testCollision.Emissions == 0 && testEmitter.Emissions == 0, "Sub-threshold impact emitted a pulse.");
            await Hit(testBody, wall.transform.position, collision.minimumImpactSpeed + 5f);
            Check(testCollision.Emissions == 1 && testEmitter.Emissions == 1, "Strong impact did not emit exactly one pulse.");
            return new { passed = true, idleSeconds = 3.5, idlePulses, rollingSeconds = 3.5, rollingPulses,
                weakImpactPulses = 0, strongImpactPulses = testCollision.Emissions,
                strongImpactSpeed = testCollision.LastImpactSpeed, threshold = collision.minimumImpactSpeed,
                emitter.waveSpeed, emitter.afterglow };
        }
        finally
        {
            if (testBall != null) UnityEngine.Object.Destroy(testBall);
            if (wall != null) UnityEngine.Object.Destroy(wall);
        }
    }
}

using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class VerifyImpactRange
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static async Task Tick(int milliseconds)
    {
        int frame = Time.frameCount;
        await Task.Delay(milliseconds);
        Check(Application.isPlaying && Time.frameCount > frame, "Play session stopped or frames are not advancing.");
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
            await Tick(20);
            // Unity intentionally suppresses restitution below its Bounce Threshold (2 m/s here).
            // A slow ball stopping against the wall is also a real collision.
            if (body.linearVelocity.z < -.1f
                || (body.position.z > wall.z - .4f && Mathf.Abs(body.linearVelocity.z) < .1f))
            { bounced = true; break; }
        }
        Check(bounced, "Wall contact was not physically observed.");
        body.linearVelocity = Vector3.zero;
    }

    public static async Task<object> Main()
    {
        Check(Application.isPlaying, "Run in BlackBox Play mode.");
        var sceneBall = GameObject.Find("PickupBouncyBall");
        var sceneEmitter = sceneBall.GetComponent<SonarObjectEmitter>();
        var sceneBody = sceneBall.GetComponent<Rigidbody>();
        Check(!sceneEmitter.automatic && sceneEmitter.toggleKey == UnityEngine.InputSystem.Key.None, "Autonomous emissions are enabled.");
        int before = sceneEmitter.Emissions;
        await Tick(3500);
        Check(sceneEmitter.Emissions == before, "Stationary ball emitted without impact.");
        Vector3 start = sceneBody.position;
        sceneBody.linearVelocity = Vector3.right * 1.5f;
        sceneBody.angularVelocity = Vector3.back * 7.5f;
        await Tick(3500);
        Check(Vector3.Distance(start, sceneBody.position) > .2f && sceneEmitter.Emissions == before,
            "Rolling did not move the ball or unexpectedly emitted a pulse.");
        sceneBody.linearVelocity = Vector3.zero;
        sceneBody.angularVelocity = Vector3.zero;

        GameObject ball = null, wall = null;
        try
        {
            ball = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));
            var body = ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.position = new Vector3(1000, 1000, 999);
            ball.transform.position = body.position;
            var collision = ball.GetComponent<CollisionSonarEmitter>();
            var emitter = ball.GetComponent<SonarObjectEmitter>();
            emitter.playSound = false;
            float threshold = collision.minimumImpactSpeed, baseRange = emitter.range;
            Check(Mathf.Approximately(threshold, sceneBall.GetComponent<CollisionSonarEmitter>().minimumImpactSpeed)
                && Mathf.Approximately(emitter.waveSpeed, sceneEmitter.waveSpeed)
                && Mathf.Approximately(emitter.afterglow, sceneEmitter.afterglow), "Prefab lost scene tuning.");
            Check(collision.RangeForImpact(threshold - .01f) == 0
                && Mathf.Approximately(collision.RangeForImpact(threshold), baseRange), "Threshold boundary is incorrect.");
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(1000, 1000, 1000);
            wall.transform.localScale = new Vector3(3, 3, .2f);
            Physics.SyncTransforms();
            await Tick(100);
            await Hit(body, wall.transform.position, threshold * .8f);
            Check(collision.Emissions == 0 && emitter.Emissions == 0, "Sub-threshold impact emitted.");

            await Hit(body, wall.transform.position, threshold + 1);
            float slowerRange = emitter.LastEmissionRange;
            Check(collision.Emissions == 1 && slowerRange > baseRange, "Impact did not increase range.");
            int cooldown = Mathf.CeilToInt((collision.emissionInterval + .05f) * 1000);
            await Tick(cooldown);
            await Hit(body, wall.transform.position, threshold + 5);
            float fasterRange = emitter.LastEmissionRange;
            Check(collision.Emissions == 2 && fasterRange > slowerRange, "Faster collision did not have greater range.");
            await Tick(cooldown);
            float capSpeed = threshold + (collision.maximumRange - baseRange) / collision.rangePerSpeed + 5;
            await Hit(body, wall.transform.position, capSpeed);
            float cappedRange = emitter.LastEmissionRange;
            Check(collision.Emissions == 3 && Mathf.Approximately(cappedRange, collision.maximumRange), "Maximum range was not enforced.");
            var spatial = emitter.GetComponent<SpatialAudioEmitter>();
            Check(Mathf.Abs(spatial.settings.maxDistance - emitter.soundRange * cappedRange / baseRange) < .01f,
                "Audio reach did not scale with visual range.");
            Check(Array.Exists(Shader.GetGlobalVectorArray("_SonarParameters"), p => p.w > .5f && Mathf.Abs(p.x - cappedRange) < .01f),
                "Renderer did not receive scaled range.");
            await Tick(cooldown);
            await Hit(body, wall.transform.position, threshold + 1);
            Check(collision.Emissions == 4 && Mathf.Abs(emitter.LastEmissionRange - slowerRange) < .05f,
                "Range accumulated from previous collisions.");
            emitter.EmitNow();
            Check(Mathf.Approximately(emitter.LastEmissionRange, baseRange) && Mathf.Approximately(emitter.range, baseRange)
                && Mathf.Abs(spatial.settings.maxDistance - emitter.soundRange) < .01f, "Base range/audio configuration was mutated.");
            // Zero threshold is supported without division by zero.
            collision.minimumImpactSpeed = 0;
            Check(Mathf.Approximately(collision.RangeForImpact(0), baseRange), "Zero threshold is not supported.");
            return new { passed = true, idleAndRollingPulses = sceneEmitter.Emissions - before, threshold, baseRange,
                slowerRange, fasterRange, cappedRange, physicsEmissions = collision.Emissions,
                audioScaled = true, shaderRangeVerified = true, baseSettingsPreserved = true };
        }
        finally
        {
            if (ball != null) UnityEngine.Object.Destroy(ball);
            if (wall != null) UnityEngine.Object.Destroy(wall);
        }
    }
}

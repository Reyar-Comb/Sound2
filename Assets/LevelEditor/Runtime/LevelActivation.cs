using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sound2.LevelEditing
{
    /// <summary>Stages whole props, preserving child states required by their lifecycle callbacks.</summary>
    internal sealed class LevelActivation : IDisposable
    {
        readonly LevelLayout level;
        readonly double budgetSeconds;
        readonly int batchSize;
        readonly List<GameObject> surroundings = new();
        readonly List<GameObject> gravityProps = new();
        bool committed;

        public LevelActivation(LevelLayout level, float milliseconds, int batchSize)
        {
            this.level = level;
            budgetSeconds = Mathf.Max(.1f, milliseconds) / 1000;
            this.batchSize = Mathf.Max(1, batchSize);
        }

        public IEnumerator Prepare()
        {
            if (level.gameObject.activeInHierarchy) { committed = true; yield break; }
            level.IsPreparing = true;
            var pending = new Stack<Transform>();
            var components = new List<Component>();
            var bodies = new List<Rigidbody>();
            pending.Push(level.transform);
            double start = Time.realtimeSinceStartupAsDouble;
            int count = 0;
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == null || node != level.transform && !node.gameObject.activeSelf) continue;
                node.GetComponents(components);
                // Only traverse empty organizational transforms. Never split a prefab's
                // scripts from children they expect to be active during Awake/OnEnable.
                if (node == level.transform || components.Count == 1)
                {
                    for (int i = node.childCount - 1; i >= 0; i--) pending.Push(node.GetChild(i));
                }
                else
                {
                    // The level is still inactive. Include child bodies so a prop with
                    // its physics below the root waits for its support as a whole.
                    node.GetComponentsInChildren(true, bodies);
                    var phase = bodies.Exists(body => body.useGravity && !body.isKinematic)
                        ? gravityProps : surroundings;
                    phase.Add(node.gameObject);
                    node.gameObject.SetActive(false);
                }
                if (++count >= batchSize || Time.realtimeSinceStartupAsDouble - start >= budgetSeconds)
                {
                    yield return null;
                    start = Time.realtimeSinceStartupAsDouble; count = 0;
                }
            }
            level.gameObject.SetActive(true);
            yield return null;
            foreach (var phase in new[] { surroundings, gravityProps })
            {
                start = Time.realtimeSinceStartupAsDouble; count = 0;
                foreach (var item in phase)
                {
                    if (item != null) item.SetActive(true);
                    if (++count >= batchSize || Time.realtimeSinceStartupAsDouble - start >= budgetSeconds)
                    {
                        yield return null;
                        start = Time.realtimeSinceStartupAsDouble; count = 0;
                    }
                }
                // Allow Start to finish setting up support colliders before enabling gravity props.
                yield return null;
            }
            // Start queues passive voices instead of constructing them all in that frame.
            // Keep the floor in preparation until its voices are ready for immediate impacts.
            var emitters = level.GetComponentsInChildren<SonarObjectEmitter>();
            foreach (var emitter in emitters)
                while (emitter != null && emitter.isActiveAndEnabled && !emitter.AudioReady)
                    yield return null;
            level.IsPreparing = false;
        }

        public void Commit() => committed = true;

        public void Dispose()
        {
            if (level == null || committed) return;
            // Restore staged activeSelf values while the parent is disabled: cancellation
            // must neither initialize the rest of the floor nor lose authored inactive nodes.
            level.gameObject.SetActive(false);
            foreach (var item in surroundings) if (item != null) item.SetActive(true);
            foreach (var item in gravityProps) if (item != null) item.SetActive(true);
            level.IsPreparing = false;
        }
    }
}

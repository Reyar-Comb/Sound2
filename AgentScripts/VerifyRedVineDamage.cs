using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

public static class VerifyRedVineDamage
{
    public static async Task<object> Run()
    {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var menu = Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        bool menuEnabled = menu != null && menu.gameObject.activeSelf;
        GameObject fixture = null;
        try
        {
            int frame = Time.frameCount;
            await Task.Delay(150);
            Check(Time.frameCount > frame, "Play mode advances frames");
            if (menuEnabled) menu.gameObject.SetActive(false);
            fixture = new GameObject("Temporary vine damage verification");
            fixture.transform.position = new Vector3(10000, 10000, 10000);
            var player = new GameObject("Test player capsule");
            player.transform.SetParent(fixture.transform, false);
            var capsule = player.AddComponent<CharacterController>();
            capsule.height = 1.8f; capsule.radius = .3f; capsule.center = Vector3.up * .9f;
            var vitality = player.AddComponent<PlayerVitality>();
            vitality.invulnerabilitySeconds = 0;
            int died = 0, hurt = 0;
            vitality.Died += () => died++;
            vitality.Hurt += () => hurt++;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RedVineArea.prefab");
            var instance = Object.Instantiate(prefab, fixture.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            var area = instance.GetComponent<MonsterVineArea>();
            var vines = instance.GetComponent<MonsterVines>();
            typeof(MonsterVineArea).GetField("playerVitality", flags).SetValue(area, vitality);
            void Move(Vector3 local)
            {
                capsule.enabled = false;
                player.transform.position = instance.transform.TransformPoint(local);
                capsule.enabled = true;
                Physics.SyncTransforms();
            }
            void Contact()
            {
                typeof(MonsterVineArea).GetField("nextContactDamage", flags).SetValue(area, 0f);
                area.SendMessage("TryContactDamage", SendMessageOptions.RequireReceiver);
            }
            Check(area.contactDamageRadius == .65f && area.contactDamageInterval == .5f,
                "Existing prefab picks up monster contact defaults without migration");
            Move(new Vector3(8, 0, 0)); Contact();
            Check(!vitality.IsDead && !vitality.IsWounded, "Outside area and contact radius is safe");
            Move(new Vector3(0, 5, 0)); Contact();
            Check(!vitality.IsDead, "Above area and contact radius is safe");
            Move(new Vector3(2.5f, 0, 1)); Contact();
            Check(vitality.IsDead && died == 1 && hurt == 0 && !vitality.IsWounded,
                "Off-center area contact kills from full health with one death event and no wound event");
            Contact(); Check(died == 1, "Dead player cannot trigger duplicate death");
            vitality.RestoreHealth();
            instance.transform.localRotation = Quaternion.Euler(0, 37, 0);
            instance.transform.localScale = new Vector3(1.5f, 1, .75f);
            Move(new Vector3(-2.5f, 0, 1)); Contact();
            Check(vitality.IsDead, "Rotated and scaled area kills throughout its volume");
            vitality.RestoreHealth(); Move(new Vector3(5, 0, 0)); Contact();
            Check(!vitality.IsDead, "Rotated and scaled area preserves safe exterior");
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Move(new Vector3(3.8f, 0, 0));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(fixture.transform, false);
            wall.transform.localPosition = new Vector3(3.35f, 1, 0);
            wall.transform.localScale = new Vector3(.1f, 2, 4);
            Physics.SyncTransforms(); Contact();
            Check(!vitality.IsDead, "Wall occludes contact within lethal reach");
            wall.SetActive(false); Physics.SyncTransforms(); Contact();
            Check(vitality.IsDead, "Same capsule distance is lethal after removing wall");
            vitality.RestoreAfterRespawn(); Move(Vector3.zero); Contact();
            Check(!vitality.IsDead && vitality.IsInvulnerable, "Respawn immunity rejects lethal contact");
            vitality.RestoreHealth(); vitality.invulnerabilitySeconds = 1.5f;
            Check(vitality.TakeDamage(1), "Normal hit wounds player"); Contact();
            Check(vitality.IsWounded && !vitality.IsDead, "Wound immunity rejects lethal contact");
            vitality.RestoreHealth(); vitality.invulnerabilitySeconds = 0;
            Check(vitality.TakeDamage(1) && vitality.IsWounded && !vitality.IsDead
                && vitality.TakeDamage(1) && vitality.IsDead, "Normal monster damage still requires two accepted hits");
            vitality.RestoreHealth(); capsule.enabled = false; Contact();
            Check(!vitality.IsDead, "Disabled player capsule rejects contact"); capsule.enabled = true;
            vitality.enabled = false; Contact();
            Check(!vitality.IsDead, "Disabled vitality rejects contact"); vitality.enabled = true;
            vines.enabled = false; Contact();
            Check(!vitality.IsDead, "Disabled vine effect rejects contact"); vines.enabled = true;
            area.enabled = false;
            await Task.Delay(150);
            Check(!vitality.IsDead, "Disabled area causes no damage during live frames");
            area.enabled = true;
            await Task.Delay(150);
            Check(vitality.IsDead, "Enabled area automatically kills through Update");
            return new { passed = true, count = checks.Count, checks };
        }
        finally
        {
            if (fixture != null) { fixture.SetActive(false); Object.Destroy(fixture); }
            if (menuEnabled && menu != null) menu.gameObject.SetActive(true);
        }
    }
}

return await VerifyRedVineDamage.Run();

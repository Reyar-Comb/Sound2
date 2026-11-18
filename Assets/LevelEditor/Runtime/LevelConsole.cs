using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sound2.LevelEditing
{
    [DisallowMultipleComponent, RequireComponent(typeof(LevelWorld))]
    public sealed class LevelConsole : MonoBehaviour
    {
        const int MaxCommandLength = 128;
        const string LevelNames = "start  school  library  home  grocery  wedding  hospital  boss";
        const string Commands = "tp <level>  |  boss  |  skip  |  unlock";
        LevelWorld world;
        Keyboard inputKeyboard;
        string command = "", message = "";
        bool open;

        void Awake() => world = GetComponent<LevelWorld>();

        void Update()
        {
            var keyboard = Keyboard.current;
            var player = world.player;
            bool inBossIntro = player != null && player.EncounterInputLocked
                && world.CurrentLevel != null
                && world.CurrentLevel.GetComponentInChildren<LevelArenaBoss>()?.State == LevelArenaBoss.EncounterState.Intro;
            if (keyboard == null || player == null || player.DeathSequenceLocked || player.EncounterInputLocked && !inBossIntro
                || player.OpeningSequenceLocked || SonicSpaceStartMenu.BlocksGameplay) { Close(); return; }
            if (keyboard.backquoteKey.wasPressedThisFrame)
            {
                if (open) Close();
                else if (!world.Travelling && (!player.InputSuspended || inBossIntro) && !player.TransportLocked)
                {
                    open = true;
                    inputKeyboard = keyboard;
                    inputKeyboard.onTextInput += OnTextInput;
                    player.SetConsoleInput(true);
                }
                return;
            }

            if (!open) return;
            if (keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (keyboard.backspaceKey.wasPressedThisFrame && command.Length > 0) command = command[..^1];
            if ((keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) && keyboard.vKey.wasPressedThisFrame)
                foreach (char character in GUIUtility.systemCopyBuffer) OnTextInput(character);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Execute();
        }

        void OnTextInput(char character)
        {
            if (command.Length < MaxCommandLength && character != '`' && !char.IsControl(character)) command += character;
        }

        void Execute()
        {
            string[] args = command.Trim().Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 1 && args[0].Equals("boss", StringComparison.OrdinalIgnoreCase))
            {
                var bossLevel = world.levels.Find(item => item != null && item.GetComponentInChildren<LevelArenaBoss>(true) != null);
                if (bossLevel == null) { message = "No Boss level in this world."; return; }
                if (world.CurrentLevel == bossLevel)
                {
                    if (!StartBoss(bossLevel)) return;
                }
                else
                {
                    if (!world.TryTeleportToLevel(bossLevel)) { message = "The Boss arrival elevator is unavailable."; return; }
                    StartCoroutine(StartBossAfterArrival(bossLevel));
                }
                command = message = "";
                Close();
                return;
            }
            if (args.Length == 1 && args[0].Equals("unlock", StringComparison.OrdinalIgnoreCase))
            {
                var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                var items = new System.Collections.Generic.List<StoryCollectible>();
                foreach (var collectibleLevel in world.levels)
                {
                    if (collectibleLevel == null) continue;
                    foreach (var item in collectibleLevel.GetComponentsInChildren<StoryCollectible>(true))
                    {
                        items.Add(item);
                        if (!string.IsNullOrWhiteSpace(item.CollectibleId)) ids.Add(item.CollectibleId);
                    }
                    foreach (var gate in collectibleLevel.GetComponentsInChildren<BossMemoryGate>(true))
                        if (gate.memories != null)
                            foreach (var memory in gate.memories)
                                if (memory != null && !string.IsNullOrWhiteSpace(memory.id)) ids.Add(memory.id);
                }
                foreach (string id in ids) StoryRunProgress.Current.TryCollect(id);
                // Update already initialized props too; inactive floors bind the same progress on activation.
                foreach (var item in items) if (item != null && item.isActiveAndEnabled) item.BindProgress(StoryRunProgress.Current);
                command = "";
                message = $"Unlocked all {ids.Count} collectibles. Esc to close.";
                return;
            }
            if (args.Length == 1 && args[0].Equals("skip", StringComparison.OrdinalIgnoreCase))
            {
                var boss = world.CurrentLevel != null ? world.CurrentLevel.GetComponentInChildren<LevelArenaBoss>() : null;
                if (boss == null) { message = "Enter the Boss level first: tp boss"; return; }
                if (!boss.TrySkipToVictory(world.player)) { message = "Boss victory is unavailable in the current state."; return; }
                command = message = "";
                Close();
                return;
            }
            if (args.Length != 2 || !args[0].Equals("tp", StringComparison.OrdinalIgnoreCase))
            {
                message = "Usage: " + Commands;
                return;
            }

            string target = LevelKey(args[1]);
            var level = world.levels.Find(item => item != null && LevelKey(item.name) == target);
            if (level == null) { message = "Unknown level: " + args[1]; return; }
            if (!world.TryTeleportToLevel(level)) { message = "The arrival elevator is unavailable."; return; }

            command = message = "";
            Close();
        }

        IEnumerator StartBossAfterArrival(LevelLayout level)
        {
            while (world.Travelling) yield return null;
            if (level == null || world.CurrentLevel != level) { message = "Boss transfer cancelled."; yield break; }
            StartBoss(level);
        }

        bool StartBoss(LevelLayout level)
        {
            var boss = level.GetComponentInChildren<LevelArenaBoss>();
            if (boss != null && boss.TryStartFromConsole(world.player)) return true;
            message = "Boss encounter already started or unavailable.";
            return false;
        }

        static string LevelKey(string name)
        {
            string key = name.Trim().ToLowerInvariant();
            int separator = key.IndexOf(" - ", StringComparison.Ordinal);
            if (separator >= 0 && int.TryParse(key[..separator], out _)) key = key[(separator + 3)..];
            if (key.StartsWith("level", StringComparison.Ordinal)) key = key[5..];
            if (key.EndsWith("world", StringComparison.Ordinal)) key = key[..^5];
            return key switch
            {
                "starting white room" or "starting room" or "startingroom" or "初始房间" => "start",
                "学校" => "school",
                "图书馆" => "library",
                "家" => "home",
                "超市" or "supermarket" => "grocery",
                "婚礼" => "wedding",
                "医院" => "hospital",
                _ => key
            };
        }

        void Close()
        {
            if (!open) return;
            open = false;
            inputKeyboard.onTextInput -= OnTextInput;
            inputKeyboard = null;
            if (world.player != null) world.player.SetConsoleInput(false);
        }

        void OnDisable() => Close();

        void OnGUI()
        {
            if (!open) return;
            GUI.depth = -100;
            float width = Mathf.Min(Screen.width - 32f, 660f);
            GUI.Box(new Rect(16, 16, width, 106), "");
            GUI.Label(new Rect(28, 26, width - 24, 24), "> " + command + "_");
            GUI.Label(new Rect(28, 53, width - 24, 22), Commands + "    Enter to run    Esc to close");
            GUI.Label(new Rect(28, 78, width - 24, 24), string.IsNullOrEmpty(message) ? LevelNames : message);
        }
    }
}

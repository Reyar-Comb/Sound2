using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sound2.LevelEditing
{
    [DisallowMultipleComponent, RequireComponent(typeof(LevelWorld))]
    public sealed class LevelConsole : MonoBehaviour
    {
        const int MaxCommandLength = 128;
        const string LevelNames = "start  school  library  home  grocery  wedding  hospital  boss";
        LevelWorld world;
        Keyboard inputKeyboard;
        string command = "", message = "";
        bool open;

        void Awake() => world = GetComponent<LevelWorld>();

        void Update()
        {
            var keyboard = Keyboard.current;
            var player = world.player;
            if (keyboard == null || player == null || player.DeathSequenceLocked || player.EncounterInputLocked
                || player.OpeningSequenceLocked || SonicSpaceStartMenu.BlocksGameplay) { Close(); return; }
            if (keyboard.backquoteKey.wasPressedThisFrame)
            {
                if (open) Close();
                else if (!world.Travelling && !player.InputSuspended && !player.TransportLocked)
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
            if (args.Length != 2 || !args[0].Equals("tp", StringComparison.OrdinalIgnoreCase))
            {
                message = "Usage: tp <level>";
                return;
            }

            string target = LevelKey(args[1]);
            var level = world.levels.Find(item => item != null && LevelKey(item.name) == target);
            if (level == null) { message = "Unknown level: " + args[1]; return; }
            if (!world.TryTeleportToLevel(level)) { message = "The arrival elevator is unavailable."; return; }

            command = message = "";
            Close();
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
            GUI.Label(new Rect(28, 53, width - 24, 22), "tp <level>    Enter to run    Esc to close");
            GUI.Label(new Rect(28, 78, width - 24, 24), string.IsNullOrEmpty(message) ? LevelNames : message);
        }
    }
}

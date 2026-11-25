using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(ScaryBaboonTrollMod.Main), "Scary Baboon Troll Menu", "0.2.0", "Community")]
[assembly: MelonGame("BabTeam", "Baboon")]

namespace ScaryBaboonTrollMod
{
    // Reflection-only Unity access: compiles against the public MelonLoader NuGet package
    // without requiring a manually dumped Assembly-CSharp/interop folder.
    public sealed class Main : MelonMod
    {
        private bool menuOpen;
        private bool longArms;
        private bool speedBoost;
        private bool jumpBoost;
        private bool diagnostics = true;
        private float armMultiplier = 2.0f;
        private float speedMultiplier = 1.5f;
        private float jumpMultiplier = 1.5f;
        private float nextScan;
        private string status = "Waiting for Unity scene...";
        private readonly List<object> armTransforms = new List<object>();
        private readonly Dictionary<int, object> originalScales = new Dictionary<int, object>();
        private object localRoot;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Scary Baboon Troll Menu loaded. Quest toggle: left controller primary button (fallback).");
        }

        public override void OnUpdate()
        {
            if (KeyDown("F5") || KeyDown("JoystickButton3") || KeyDown("JoystickButton0"))
                menuOpen = !menuOpen;

            if (TimeNow() >= nextScan)
            {
                nextScan = TimeNow() + 2.0f;
                ScanScene();
            }

            if (longArms) ApplyLongArms();
            if (speedBoost) ApplySpeedBoost();
            if (jumpBoost) ApplyJumpBoost();
        }

        public override void OnGUI()
        {
            if (!menuOpen) return;
            try
            {
                Type rectType = FindType("UnityEngine.Rect");
                Type guiLayout = FindType("UnityEngine.GUILayout");
                if (rectType == null || guiLayout == null) return;

                object rect = Activator.CreateInstance(rectType, new object[] { 18f, 18f, 390f, 560f });
                InvokeStatic(guiLayout, "BeginArea", rect);
                InvokeStatic(guiLayout, "BeginVertical");
                InvokeStatic(guiLayout, "Label", "SCARY BABOON  /  TROLL MENU");
                InvokeStatic(guiLayout, "Label", "Quest 3 | F5 / controller button to hide");
                InvokeStatic(guiLayout, "Label", "MOVEMENT");
                longArms = Toggle(guiLayout, longArms, "Long arms");
                speedBoost = Toggle(guiLayout, speedBoost, "Speed boost (local Rigidbody)");
                jumpBoost = Toggle(guiLayout, jumpBoost, "Jump boost (local Rigidbody)");
                InvokeStatic(guiLayout, "Label", "Arm length: " + armMultiplier.ToString("0.0") + "x");
                if (Button(guiLayout, "Arm length -")) armMultiplier = Math.Max(1f, armMultiplier - 0.25f);
                if (Button(guiLayout, "Arm length +")) armMultiplier = Math.Min(5f, armMultiplier + 0.25f);
                InvokeStatic(guiLayout, "Label", "Speed: " + speedMultiplier.ToString("0.0") + "x");
                if (Button(guiLayout, "Speed -")) speedMultiplier = Math.Max(1f, speedMultiplier - 0.1f);
                if (Button(guiLayout, "Speed +")) speedMultiplier = Math.Min(3f, speedMultiplier + 0.1f);
                InvokeStatic(guiLayout, "Label", "Jump: " + jumpMultiplier.ToString("0.0") + "x");
                if (Button(guiLayout, "Jump -")) jumpMultiplier = Math.Max(1f, jumpMultiplier - 0.1f);
                if (Button(guiLayout, "Jump +")) jumpMultiplier = Math.Min(3f, jumpMultiplier + 0.1f);
                diagnostics = Toggle(guiLayout, diagnostics, "Show diagnostics");
                if (diagnostics) InvokeStatic(guiLayout, "Label", status);
                if (Button(guiLayout, "Restore arm scale")) RestoreArms();
                InvokeStatic(guiLayout, "EndVertical");
                InvokeStatic(guiLayout, "EndArea");
            }
            catch (Exception e) { status = "Menu reflection error: " + e.GetType().Name; }
        }

        private void ScanScene()
        {
            try
            {
                armTransforms.Clear();
                localRoot = null;
                Type goType = FindType("UnityEngine.GameObject");
                Type resourcesType = FindType("UnityEngine.Resources");
                if (goType == null || resourcesType == null) { status = "Unity types not loaded"; return; }
                MethodInfo find = resourcesType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "FindObjectsOfTypeAll" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(Type));
                if (find == null) { status = "Unity scene discovery unavailable"; return; }
                Array all = find.Invoke(null, new object[] { goType }) as Array;
                if (all == null) { status = "No scene objects returned"; return; }

                foreach (object go in all)
                {
                    if (go == null) continue;
                    string name = Convert.ToString(GetProperty(go, "name")) ?? "";
                    string lower = name.ToLowerInvariant();
                    object scene = GetProperty(go, "scene");
                    object valid = GetProperty(scene, "IsValid");
                    if (valid is bool b && !b) continue;

                    object transform = GetProperty(go, "transform");
                    if (transform == null) continue;
                    if (localRoot == null && (lower.Contains("localplayer") || lower.Contains("player rig") || lower == "player"))
                        localRoot = transform;
                    if ((lower.Contains("hand") || lower.Contains("palm") || lower.Contains("forearm")) && armTransforms.Count < 16)
                        armTransforms.Add(transform);
                }
                status = "Found " + armTransforms.Count + " arm candidates; local root: " +
                    (localRoot == null ? "not found" : Convert.ToString(GetProperty(localRoot, "name")));
            }
            catch (Exception e) { status = "Scan failed: " + e.GetType().Name; }
        }

        private void ApplyLongArms()
        {
            foreach (object t in armTransforms)
            {
                if (t == null) continue;
                int id = InstanceId(t);
                object scale = GetProperty(t, "localScale");
                if (scale == null) continue;
                if (!originalScales.ContainsKey(id)) originalScales[id] = scale;
                object baseScale = originalScales[id];
                float x = Number(GetField(baseScale, "x"));
                float y = Number(GetField(baseScale, "y"));
                float z = Number(GetField(baseScale, "z"));
                SetProperty(t, "localScale", MakeVector3(x, y * armMultiplier, z));
            }
        }

        private void RestoreArms()
        {
            foreach (object t in armTransforms)
            {
                if (t == null) continue;
                int id = InstanceId(t);
                if (originalScales.TryGetValue(id, out object scale)) SetProperty(t, "localScale", scale);
            }
            originalScales.Clear();
            longArms = false;
        }

        private void ApplySpeedBoost() => MultiplyVelocity(speedMultiplier, false);
        private void ApplyJumpBoost() => MultiplyVelocity(jumpMultiplier, true);

        private void MultiplyVelocity(float multiplier, bool verticalOnly)
        {
            if (localRoot == null) return;
            try
            {
                object go = GetProperty(localRoot, "gameObject");
                Type rbType = FindType("UnityEngine.Rigidbody");
                if (go == null || rbType == null) return;
                object rb = InvokeInstance(go, "GetComponent", rbType);
                if (rb == null) return;
                object velocity = GetProperty(rb, "velocity");
                if (velocity == null) return;
                float x = Number(GetField(velocity, "x"));
                float y = Number(GetField(velocity, "y"));
                float z = Number(GetField(velocity, "z"));
                if (verticalOnly) y *= multiplier;
                else { x *= multiplier; z *= multiplier; }
                SetProperty(rb, "velocity", MakeVector3(x, y, z));
            }
            catch { /* game may use a custom locomotion controller instead of Rigidbody */ }
        }

        private static bool Toggle(Type t, bool value, string label)
        {
            object result = InvokeStatic(t, "Toggle", value, label);
            return result is bool b ? b : value;
        }
        private static bool Button(Type t, string label)
        {
            object result = InvokeStatic(t, "Button", label);
            return result is bool b && b;
        }
        private static bool KeyDown(string enumName)
        {
            try
            {
                Type input = FindType("UnityEngine.Input");
                Type key = FindType("UnityEngine.KeyCode");
                if (input == null || key == null) return false;
                object code = Enum.Parse(key, enumName);
                MethodInfo m = input.GetMethod("GetKeyDown", BindingFlags.Public | BindingFlags.Static, null, new[] { key }, null);
                return m != null && (bool)m.Invoke(null, new[] { code });
            }
            catch { return false; }
        }
        private static float TimeNow()
        {
            try { return Convert.ToSingle(GetStaticProperty(FindType("UnityEngine.Time"), "unscaledTime")); }
            catch { return 0f; }
        }
        private static Type FindType(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }
        private static object GetProperty(object o, string name) => o?.GetType().GetProperty(name)?.GetValue(o);
        private static object GetStaticProperty(Type t, string name) => t?.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        private static object GetField(object o, string name) => o?.GetType().GetField(name)?.GetValue(o);
        private static void SetProperty(object o, string name, object value) => o?.GetType().GetProperty(name)?.SetValue(o, value);
        private static object MakeVector3(float x, float y, float z)
        {
            Type v = FindType("UnityEngine.Vector3");
            return v == null ? null : Activator.CreateInstance(v, new object[] { x, y, z });
        }
        private static int InstanceId(object o)
        {
            try { return Convert.ToInt32(o.GetType().GetMethod("GetInstanceID").Invoke(o, null)); }
            catch { return o.GetHashCode(); }
        }
        private static float Number(object o) => o == null ? 0f : Convert.ToSingle(o);
        private static object InvokeStatic(Type t, string name, params object[] args)
        {
            if (t == null) return null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name == name && x.GetParameters().Length == args.Length))
            {
                try { return m.Invoke(null, args); } catch { }
            }
            return null;
        }
        private static object InvokeInstance(object target, string name, params object[] args)
        {
            foreach (MethodInfo m in target.GetType().GetMethods().Where(x => x.Name == name && x.GetParameters().Length == args.Length))
            {
                try { return m.Invoke(target, args); } catch { }
            }
            return null;
        }
    }
}

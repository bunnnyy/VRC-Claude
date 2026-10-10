using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace SourceMaps.Bsp
{
    /// <summary>
    /// What bhop map triggers do to the player, read from their keyvalues and outputs (Source space):
    /// trigger_push (pushdir * speed), basevelocity / gravity boosters (outputs to !activator), trigger_gravity.
    /// </summary>
    public static class BspMechanics
    {
        public struct Boost
        {
            public Vector3 Velocity;   // Source units/s, Source axes; zero = none
            public bool SetGravity;
            public float Gravity;      // multiplier, 1 = normal
            public bool OnLeave;       // OnEndTouch instead of OnStartTouch / OnTrigger
        }

        /// <summary>trigger_push: velocity in Source units/s (Source axes).</summary>
        public static Vector3 Push(Entity e)
        {
            float speed;
            float.TryParse(e.Get("speed", "40"), NumberStyles.Float, CultureInfo.InvariantCulture, out speed);
            return BspGeometry.RotateSource(Vector3.UnitX, e.GetVector("pushdir")) * speed;
        }

        /// <summary>
        /// Boosters on a trigger: one entry for things done on entering and one for leaving (if any).
        /// trigger_multiple / trigger_once: outputs "!activator,AddOutput,basevelocity X Y Z" or "...,gravity G".
        /// trigger_gravity: its "gravity" keyvalue, on entering.
        /// </summary>
        /// <summary>"!activator AddOutput targetname X" on a trigger: the name, delay and whether it's on leaving.</summary>
        public struct NameSet { public string Name; public float Delay; public bool OnLeave, IsClass; }

        /// <summary>The player renames of a trigger_multiple / trigger_once (bhop blocks, boost selectors).</summary>
        public static List<NameSet> NameSets(Entity e)
        {
            var list = new List<NameSet>();
            if (e.ClassName != "trigger_multiple" && e.ClassName != "trigger_once") return list;
            foreach (var o in e.Outputs())
            {
                if (!o.Target.Equals("!activator", StringComparison.OrdinalIgnoreCase) ||
                    !o.Input.Equals("AddOutput", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = o.Parameter.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                bool isClass = parts.Length > 0 && parts[0].Equals("classname", StringComparison.OrdinalIgnoreCase);
                if (parts.Length == 0 || (!isClass && !parts[0].Equals("targetname", StringComparison.OrdinalIgnoreCase))) continue;
                bool onLeave = o.Event.StartsWith("OnEndTouch", StringComparison.OrdinalIgnoreCase);
                bool onEnter = o.Event.StartsWith("OnStartTouch", StringComparison.OrdinalIgnoreCase) ||
                               o.Event.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase);
                if (onLeave || onEnter) list.Add(new NameSet { Name = parts.Length > 1 ? parts[1].Trim() : "", Delay = o.Delay, OnLeave = onLeave, IsClass = isClass });
            }
            return list;
        }

        /// <summary>
        /// The player name an entity's "filtername" checks (filter_activator_name), and whether it's negated. Null if
        /// there's no filter (or it names no entity: Source then ignores it); "?" for other filter classes.
        /// </summary>
        public static string FilterName(BspFile bsp, Entity e, out bool negate) { return FilterName(bsp, e, out negate, out _); }

        /// <summary>FilterName, also telling whether it's a filter_activator_class (checks the class, not the name).</summary>
        public static string FilterName(BspFile bsp, Entity e, out bool negate, out bool isClass)
        {
            negate = false;
            isClass = false;
            string filter = e.Get("filtername");
            if (filter == "") return null;
            var f = bsp.Entities.Find(x => x.TargetName.Equals(filter, StringComparison.OrdinalIgnoreCase));
            if (f == null) return null; // Source ignores a filter that doesn't exist
            isClass = f.ClassName == "filter_activator_class";
            if (f.ClassName != "filter_activator_name" && !isClass) return "?";
            negate = f.Get("Negated") == "1";
            return isClass ? f.Get("filterclass") : f.Get("filtername");
        }

        /// <summary>trigger_multiple's refire time: "wait" (0 means 0.2 s in Source), negative = once; trigger_once: once.</summary>
        public static float TriggerWait(Entity e)
        {
            if (e.ClassName == "trigger_once") return -1f;
            float w;
            if (!float.TryParse(e.Get("wait", "0.2"), NumberStyles.Float, CultureInfo.InvariantCulture, out w)) w = 0.2f;
            return w == 0f ? 0.2f : w;
        }

        /// <summary>A func_door that opens when touched (spawnflag 1024): the bhop block kind.</summary>
        public static bool IsTouchDoor(Entity e)
        {
            int flags;
            return e.ClassName == "func_door" && int.TryParse(e.Get("spawnflags", "0"), out flags) && (flags & 1024) != 0;
        }

        /// <summary>
        /// How far a func_door moves when it opens (Source space): along "movedir" (pitch yaw roll) by its size in that
        /// direction minus "lip", as CBaseDoor does.
        /// </summary>
        public static Vector3 DoorMove(BspFile bsp, Entity e)
        {
            var a = e.GetVector("movedir");
            float p = a.X * (float)Math.PI / 180f, y = a.Y * (float)Math.PI / 180f;
            var dir = new Vector3((float)(Math.Cos(p) * Math.Cos(y)), (float)(Math.Cos(p) * Math.Sin(y)), (float)-Math.Sin(p));
            var m = bsp.Models[e.BrushModel];
            var size = m.Maxs - m.Mins;
            float lip;
            float.TryParse(e.Get("lip", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out lip);
            float dist = Math.Abs(dir.X) * size.X + Math.Abs(dir.Y) * size.Y + Math.Abs(dir.Z) * size.Z - lip;
            return dir * dist;
        }

        public static List<Boost> Boosts(Entity e)
        {
            var enter = new Boost { Gravity = 1f };
            var leave = new Boost { Gravity = 1f, OnLeave = true };
            bool hasEnter = false, hasLeave = false;
            if (e.ClassName == "trigger_gravity")
            {
                float g;
                if (float.TryParse(e.Get("gravity", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out g))
                {
                    enter.SetGravity = true;
                    enter.Gravity = g;
                    hasEnter = true;
                }
            }
            else if (e.ClassName == "trigger_multiple" || e.ClassName == "trigger_once")
            {
                foreach (var o in e.Outputs())
                {
                    if (!o.Target.Equals("!activator", StringComparison.OrdinalIgnoreCase) ||
                        !o.Input.Equals("AddOutput", StringComparison.OrdinalIgnoreCase)) continue;
                    bool onLeave = o.Event.StartsWith("OnEndTouch", StringComparison.OrdinalIgnoreCase);
                    bool onEnter = o.Event.StartsWith("OnStartTouch", StringComparison.OrdinalIgnoreCase) ||
                                   o.Event.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase);
                    if (!onLeave && !onEnter) continue;
                    var parts = o.Parameter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    var f = new float[3];
                    for (int i = 1; i < parts.Length && i <= 3; i++)
                        float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i - 1]);
                    string what = parts[0].ToLowerInvariant();
                    if (what == "basevelocity" && parts.Length >= 4)
                    {
                        if (onLeave) { leave.Velocity += new Vector3(f[0], f[1], f[2]); hasLeave = true; }
                        else { enter.Velocity += new Vector3(f[0], f[1], f[2]); hasEnter = true; }
                    }
                    else if (what == "gravity" && parts.Length >= 2)
                    {
                        if (onLeave) { leave.SetGravity = true; leave.Gravity = f[0]; hasLeave = true; }
                        else { enter.SetGravity = true; enter.Gravity = f[0]; hasEnter = true; }
                    }
                }
            }
            var list = new List<Boost>();
            if (hasEnter) list.Add(enter);
            if (hasLeave) list.Add(leave);
            return list;
        }
    }
}

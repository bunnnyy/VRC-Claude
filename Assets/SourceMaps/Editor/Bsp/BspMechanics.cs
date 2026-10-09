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

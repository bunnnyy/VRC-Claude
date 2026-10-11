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
            public float Delay;        // seconds after the touch (the outputs' delay)
        }

        /// <summary>trigger_push: velocity in Source units/s (Source axes).</summary>
        public static Vector3 Push(Entity e)
        {
            float speed;
            float.TryParse(e.Get("speed", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out speed);
            if (speed == 0f) speed = 100f; // CTriggerPush::Spawn
            return BspGeometry.RotateSource(Vector3.UnitX, e.GetVector("pushdir")) * speed;
        }

        /// <summary>
        /// Boosters on a trigger: one entry per moment something is done (entering or leaving, and the outputs' delay),
        /// e.g. gravity -2 on entering and gravity 1 half a second later. Each output sets the value (AddOutput sets a
        /// keyvalue), so of two at the same moment the last one counts.
        /// trigger_multiple / trigger_once: outputs "!activator,AddOutput,basevelocity X Y Z" or "...,gravity G".
        /// trigger_gravity: its "gravity" keyvalue, on entering.
        /// </summary>
        /// <summary>"!activator AddOutput targetname X" on a trigger: the name, delay and whether it's on leaving.</summary>
        public struct NameSet { public string Name; public float Delay; public bool OnLeave, IsClass, OnStart; } // OnStart: OnStartTouch (once per touch), else OnTrigger (again every wait)

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
                if (onLeave || onEnter) list.Add(new NameSet { Name = parts.Length > 1 ? parts[1].Trim() : "", Delay = o.Delay, OnLeave = onLeave, IsClass = isClass,
                    OnStart = o.Event.StartsWith("OnStartTouch", StringComparison.OrdinalIgnoreCase) });
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

        /// <summary>
        /// Does this trigger act on players? Source's PassesTriggerFilters: a trigger_* needs spawnflag 1 (clients) or
        /// 64 (everything), else it ignores players (bhop_kitsune's push 324 over a level start). Not a trigger: yes.
        /// </summary>
        public static bool TouchesPlayers(Entity e)
        {
            return !e.ClassName.StartsWith("trigger_") || (e.GetInt("spawnflags") & (1 | 64)) != 0;
        }

        /// <summary>
        /// How far to raise a trigger brush's top (the importer does, as VRChat's capsule floats above floors), at most
        /// `raise`: no higher than 1 unit under the top of a touch door (bhop block) lying on it, so standing on the
        /// closed door doesn't touch the trigger, as in Source (bhop_monster_jam's 8 unit door plates on teleports).
        /// </summary>
        public static float TriggerRaise(BspFile bsp, Entity trigger, int brush, float raise)
        {
            var polys = BspGeometry.BrushPolygons(bsp, brush);
            if (polys.Count == 0) return raise;
            Vector3 lo = new Vector3(float.MaxValue), hi = new Vector3(float.MinValue);
            foreach (var p in polys) foreach (var v in p) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            Vector3 at = trigger.GetVector("origin"); // an entity's brushes are stored around its origin
            lo += at;
            hi += at;
            foreach (var e in bsp.Entities)
            {
                if (e.BrushModel <= 0 || !IsTouchDoor(e)) continue;
                var m = bsp.Models[e.BrushModel];
                Vector3 o = e.GetVector("origin"), dlo = m.Mins + o, dhi = m.Maxs + o;
                if (dlo.X >= hi.X || dhi.X <= lo.X || dlo.Y >= hi.Y || dhi.Y <= lo.Y) continue; // not over it
                if (dhi.Z <= hi.Z || dlo.Z > hi.Z + raise) continue; // not lying on its top
                raise = Math.Min(raise, Math.Max(0f, dhi.Z - 1f - hi.Z));
            }
            return raise;
        }

        /// <summary>A func_door that opens when touched (spawnflag 1024): the bhop block kind.</summary>
        public static bool IsTouchDoor(Entity e)
        {
            int flags;
            return e.ClassName == "func_door" && int.TryParse(e.Get("spawnflags", "0"), out flags) && (flags & 1024) != 0;
        }

        /// <summary>CS:S knife: how far a slash reaches (units, from the eyes); health a single knife hit surely breaks.</summary>
        public const float KnifeReach = 48f, KnifeHealth = 15f;

        /// <summary>
        /// A breakable that one knife hit breaks in CS:S, like the glass panes in bhop maps (func_breakable, health 1).
        /// VRChat has no knife, so these break when the player comes within knife reach (the importer does it for upright
        /// panes only: glass you stand on stays). func_breakable_surf (window glass) breaks on any hit. A func_breakable
        /// doesn't if it's unbreakable glass (material 7), health 0 or "Only Break on Trigger" (spawnflag 1; such ones take
        /// no damage, an output breaks them), or has more than 15 health. Not either kind with a damage filter or (for
        /// func_breakable) health from prop data: those we can't tell.
        /// </summary>
        public static bool BreaksOnApproach(Entity e)
        {
            if (e.BrushModel <= 0 || e.Get("damagefilter") != "") return false;
            if (e.ClassName == "func_breakable_surf") return true;
            if (e.ClassName != "func_breakable" || e.GetInt("propdata") != 0) return false;
            float health = Number(e, "health"), minDamage = Number(e, "minhealthdmg");
            return (e.GetInt("spawnflags") & 1) == 0 && e.GetInt("material") != 7 &&
                   health > 0f && health <= KnifeHealth && minDamage <= KnifeHealth;
        }

        static float Number(Entity e, string key)
        {
            float f;
            return float.TryParse(e.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0f;
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
            var list = new List<Boost>();
            int At(bool onLeave, float delay) // the entry for this moment, made if needed
            {
                int i = list.FindIndex(b => b.OnLeave == onLeave && b.Delay == delay);
                if (i >= 0) return i;
                list.Add(new Boost { Gravity = 1f, OnLeave = onLeave, Delay = delay });
                return list.Count - 1;
            }
            if (e.ClassName == "trigger_gravity")
            {
                float g;
                if (float.TryParse(e.Get("gravity", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out g))
                    list.Add(new Boost { SetGravity = true, Gravity = g });
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
                        int i = At(onLeave, o.Delay);
                        var b = list[i]; b.Velocity = new Vector3(f[0], f[1], f[2]); list[i] = b;
                    }
                    else if (what == "gravity" && parts.Length >= 2)
                    {
                        int i = At(onLeave, o.Delay);
                        var b = list[i]; b.SetGravity = true; b.Gravity = f[0]; list[i] = b;
                    }
                }
            }
            return list;
        }
    }
}

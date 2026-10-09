using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Movement tests for SourceMovement. Every expected number comes from Source's own maths
/// (gamemovement.cpp with CS:S / bhop server cvars), so passing means it moves like Source.
/// The same tests run against two backends that each provide a Rig class: the C# script
/// (Tests/Sim) and the compiled Udon program in VRChat's Udon VM (Tests/UdonCompile).
/// </summary>
public static class MovementTests
{
    static int failures;
    static readonly List<string> report = new List<string>();

    public static int RunAll(Action<Action<string, Action>> extraTests = null)
    {
        Test("Stands still on flat ground", StandStill);
        Test("Ground run reaches 250 u/s (maxspeed)", GroundRun);
        Test("Half stick (VR) runs at 225 u/s like Source analog input", HalfStick);
        Test("Friction stops a 250 u/s run in ~0.55 s", FrictionStop);
        Test("Jump height ~55.5 units at 100 tick (57 minus Source's half-gravity steps)", JumpHeight);
        Test("Holding W while bhopping does not gain speed", StraightBhopNoGain);
        Test("Air strafing gains speed (v^2 grows ~900 per tick)", AirStrafeGain);
        Test("A/D + mouse zigzag bhop gains speed", ZigZagBhop);
        Test("Manual mode: holding jump only jumps once", ManualHoldJumpsOnce);
        Test("Manual mode: scroll wheel jumps on landing (no friction loss)", ScrollBhop);
        Test("Auto bhop toggles at runtime with the B key", ToggleKey);
        Test("Surf: landing on a 60 degree ramp slides, never grounded", SurfSlide);
        Test("Surf: holding into the ramp keeps height and speed", SurfHold);
        Test("Surf: landing on a ramp keeps horizontal speed", SurfLandingKeepsSpeed);
        Test("Step: walks up a 16 unit step", StepUp);
        Test("Step: blocked by a 24 unit wall", StepBlocked);
        Test("Step + surf: walking into a surf ramp never stands on it", WalkIntoRamp);
        Test("Walking down a 30 degree slope stays on the ground", WalkDownSlope);
        Test("Falling speed clamps at sv_maxvelocity 3500", MaxVelocity);
        Test("Ladder: walking into it grabs it and W climbs at 200 u/s", LadderClimb);
        Test("Ladder: looking down 60 degrees + W climbs down at 73 u/s", LadderClimbDown);
        Test("Ladder: no keys hangs on without falling", LadderHang);
        Test("Ladder: jump pushes off at 270 u/s", LadderJumpOff);
        Test("Ladder: climbing to the top gets you onto the ledge", LadderTop);
        Test("Ladder: S at the bottom walks off it", LadderWalkOff);
        Test("Ladder: one behind you is not grabbed", LadderBehind);
        Test("Water: swims at 200 u/s (max speed * 0.8)", WaterSwim);
        Test("Water: no keys sinks at 48 u/s, no gravity", WaterSink);
        Test("Water: jump swims up", WaterSwimUp);
        Test("Water: ankle deep walks normally at 250 u/s", WaterShallow);
        Test("Water: waist deep at a ledge, W climbs out (water jump)", WaterJumpOut);
        Test("Push: sideways trigger_push moves you, leaving keeps the speed", PushSideways);
        Test("Push: upward trigger_push lifts you (push - gravity)", PushUp);
        Test("Boost: basevelocity booster launches you once", BoostUp);
        Test("Gravity: half gravity doubles the jump height", HalfGravity);
        Test("Teleport resets velocity", TeleportResets);
        Test("Teleport onto the floor stands on it (no sinking)", TeleportOntoFloor);
        Test("TeleportPlayer can keep velocity (portals)", TeleportKeepsVelocity);
        Test("TeleportPlayer can reset velocity", TeleportPlayerResets);
        Test("Short teleport (100 u) moves the player and keeps speed", ShortTeleport);
        Test("Teleport still works if VRChat applies it 3 frames late", DelayedTeleport);
        Test("Respawn resets velocity", RespawnResets);
        Test("Same result at 45, 90 and 144 fps", FrameRateIndependent);
        Test("Tracks the player with one frame of VRChat latency", LatencyTolerant);
        Test("Turning off restores VRChat movement and keeps momentum", Deactivate);
        Test("Collision cost per tick fits Udon", CastBudget);
        extraTests?.Invoke(Test);

        Console.WriteLine();
        foreach (string line in report) Console.WriteLine(line);
        Console.WriteLine(failures == 0 ? "\nALL PASSED" : $"\n{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    static void Test(string name, Action body)
    {
        try
        {
            body();
            report.Add("PASS  " + name);
        }
        catch (Exception e)
        {
            failures++;
            report.Add("FAIL  " + name + "\n      " + (Environment.GetEnvironmentVariable("VERBOSE") != null ? e.ToString() : (e.InnerException ?? e).Message));
        }
    }

    public static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    static void Note(string s) => report.Add("      " + s);

    static Rig OnFloor()
    {
        var r = new Rig(Vector3.zero);
        Rig.Floor();
        r.Run(0.1f);
        return r;
    }

    // ------------------------------------------------------------------ ground

    static void StandStill()
    {
        var r = OnFloor();
        r.Run(2f);
        Check(r.OnGround, "not on ground");
        Check(Math.Abs(r.Pos.y) < 1f && Math.Abs(r.Pos.x) < 0.01f, "drifted to " + r.Pos);
    }

    static void GroundRun()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(2f);
        Note($"speed {r.Speed:F2} u/s");
        Check(Math.Abs(r.Speed - 250f) < 0.5f, "speed " + r.Speed);
        r.Move(1, 1); // diagonal is clamped to maxspeed too
        r.Run(1f);
        Check(Math.Abs(r.Speed - 250f) < 0.5f, "diagonal speed " + r.Speed);
    }

    static void HalfStick()
    {
        var r = OnFloor();
        r.Move(0.5f, 0);
        r.Run(2f);
        Note($"speed {r.Speed:F2} u/s");
        Check(Math.Abs(r.Speed - 225f) < 0.5f, "speed " + r.Speed);
    }

    static void FrictionStop()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(2f);
        r.Move(0, 0);
        float t = 0;
        while (r.Speed > 0 && t < 2f) { r.Frame(); t += r.frameTime; }
        Note($"stopped after {t:F2} s (theory 0.55 s)");
        Check(t > 0.5f && t < 0.6f, "stop time " + t);
    }

    static void JumpHeight()
    {
        var r = OnFloor();
        float startY = r.Origin.y, maxY = startY, t = 0;
        r.Jump(true);
        r.Frame();
        r.Jump(false);
        t += r.frameTime;
        while (!r.OnGround && t < 2f)
        {
            r.Frame();
            t += r.frameTime;
            maxY = Math.Max(maxY, r.Origin.y);
        }
        Note($"height {maxY - startY:F2} u, air time {t:F3} s");
        // Source subtracts half a tick of gravity twice on the jump tick, so 100 tick peaks ~55.5 not 57.
        Check(Math.Abs(maxY - startY - 55.5f) < 0.5f, "height " + (maxY - startY));
        Check(Math.Abs(t - 0.74f) < 0.02f, "air time " + t);
    }

    // ------------------------------------------------------------------ bhop

    static void StraightBhopNoGain()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(1f);
        r.Jump(true);
        r.Run(5f);
        Note($"speed after 5 s {r.Speed:F2} u/s");
        Check(r.Speed <= 250.5f && r.Speed > 245f, "speed " + r.Speed);
    }

    static void AirStrafeGain()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(1f);
        r.Move(0, 1);   // hold D only
        r.Jump(true);   // auto bhop
        // Mouse follows the velocity so the strafe key stays at 90 degrees to it (the optimal angle).
        r.Run(5f, () => r.Yaw = r.VelYaw);
        float expected = (float)Math.Sqrt(250 * 250 + 900 * 500 * 0.95f);
        Note($"speed after 5 s {r.Speed:F0} u/s (theory ~{expected:F0}, 1 ground tick per hop)");
        Check(r.Speed > 650 && r.Speed < 720, "speed " + r.Speed);
    }

    static void ZigZagBhop()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(1f);
        r.Jump(true);
        // Alternate A and D each hop, mouse turning with the velocity so the strafe key stays at 90 degrees to it.
        float dir = 1;
        bool wasGround = true;
        r.Run(6f, () =>
        {
            if (r.OnGround && !wasGround) dir = -dir;
            wasGround = r.OnGround;
            r.Move(0, dir);
            r.Yaw = r.VelYaw;
        });
        Note($"speed after 6 s {r.Speed:F0} u/s");
        Check(r.Speed > 650, "speed " + r.Speed);
    }

    static void ManualHoldJumpsOnce()
    {
        var r = OnFloor();
        r.AutoBhop = false;
        r.Jump(true);
        int jumps = 0;
        bool wasGround = true;
        r.Run(3f, () =>
        {
            if (!r.OnGround && wasGround) jumps++;
            wasGround = r.OnGround;
        });
        Check(jumps == 1, "jumps " + jumps);
    }

    static void ScrollBhop()
    {
        var r = OnFloor();
        r.AutoBhop = false;
        r.Move(1, 0);
        r.Run(1f);
        int jumps = 0;
        bool wasGround = true;
        // Scroll spam: one tick of +jump every 3 frames, like a mouse wheel.
        int frame = 0;
        r.Run(5f, () =>
        {
            if (frame++ % 3 == 0) r.Scroll(-0.1f);
            if (!r.OnGround && wasGround) jumps++;
            wasGround = r.OnGround;
        });
        Note($"{jumps} jumps in 5 s, speed {r.Speed:F2} u/s");
        Check(jumps >= 6, "jumps " + jumps);
        Check(r.Speed > 240, "lost speed: " + r.Speed);
    }

    static void ToggleKey()
    {
        var r = OnFloor();
        bool before = r.AutoBhop;
        r.PressKey(KeyCode.B);
        r.Frame();
        Check(r.AutoBhop == !before, "did not toggle");
        r.Frame();
        Check(r.AutoBhop == !before, "toggled twice");
    }

    // ------------------------------------------------------------------ surf

    // 60 degree ramp: rises towards +x, runs along z. Top face normal (-0.866, 0.5, 0), plane n.p = 500.
    static readonly Vector3 RampNormal = new Vector3(-0.8660254f, 0.5f, 0);
    static void Ramp() => Rig.Box(Vector3.zero, new Vector3(1000, 1000, 40000), rotZ: 60);
    static float RampGap(Rig r)
    {
        // Distance from ramp face to the bottom of the hull (support of a 16x36x16 half box).
        Vector3 c = r.Origin + new Vector3(0, 36, 0);
        return Vector3.Dot(RampNormal, c) - 500f - (0.8660254f * 16 + 0.5f * 36);
    }

    static void SurfSlide()
    {
        var r = new Rig(new Vector3(-433, 400, 0));
        Ramp();
        bool everGrounded = false;
        float minGap = float.MaxValue, maxGap = 0;
        r.Run(0.7f); // fall onto the ramp
        float s0 = Vector3.Dot(r.Vel, new Vector3(0.5f, 0.866f, 0));
        r.Run(0.5f, () =>
        {
            everGrounded |= r.OnGround;
            float g = RampGap(r);
            minGap = Math.Min(minGap, g);
            maxGap = Math.Max(maxGap, g);
        });
        float s1 = Vector3.Dot(r.Vel, new Vector3(0.5f, 0.866f, 0));
        Note($"down-slope accel {(s0 - s1) / 0.5f:F0} u/s^2 (theory g*sin60 = 693), gap {minGap:F2}..{maxGap:F2} u");
        Check(!everGrounded, "stood on a surf ramp");
        Check(minGap >= -0.01f && maxGap < 1f, "left the ramp surface");
        Check(Math.Abs((s0 - s1) / 0.5f - 693) < 20, "slide accel");
    }

    static void SurfHold()
    {
        float Drop(bool holdInto)
        {
            var r = new Rig(new Vector3(-433, 290, 0));
            Ramp();
            r.Run(0.05f);
            r.SetVelocity(new Vector3(0, 0, 1000));
            r.Yaw = 0; // looking along the ramp, D pushes into it
            if (holdInto) r.Move(0, 1);
            float y0 = r.Origin.y;
            r.Run(1f);
            Check(!r.OnGround, "grounded on ramp");
            Check(r.Vel.z > 990, "lost speed along ramp: " + r.Vel.z);
            Check(RampGap(r) < 1f, "left ramp");
            return r.Origin.y - y0;
        }
        float hold = Drop(true), slide = Drop(false);
        Note($"height change over 1 s: holding D {hold:F0} u, no input {slide:F0} u");
        Check(Math.Abs(hold) < 60, "holding into ramp still dropped " + hold);
        Check(slide < -200, "no input should slide down, got " + slide);
    }

    static void SurfLandingKeepsSpeed()
    {
        var r = new Rig(new Vector3(-433, 400, 0));
        Ramp();
        r.SetVelocity(new Vector3(0, 0, 800));
        r.Run(0.6f);
        Note($"speed along ramp after landing {r.Vel.z:F1} u/s");
        Check(RampGap(r) < 1f, "not on ramp");
        Check(r.Vel.z > 799, "lost speed " + r.Vel.z);
    }

    // ------------------------------------------------------------------ steps / slopes

    static Rig Walker(float stepHeight)
    {
        var r = OnFloor();
        Rig.Box(new Vector3(300, stepHeight / 2, 0), new Vector3(400, stepHeight, 1000));
        r.Yaw = 90; // face +x
        r.Move(1, 0);
        r.Run(1f);
        return r;
    }

    static void StepUp()
    {
        var r = Walker(16);
        Note($"x {r.Origin.x:F0}, y {r.Origin.y:F2}");
        Check(r.Origin.x > 150 && Math.Abs(r.Origin.y - 16) < 1f && r.OnGround, "did not step: " + r.Origin);
    }

    static void StepBlocked()
    {
        var r = Walker(24);
        Check(r.Origin.x < 85 && r.Origin.y < 1f, "climbed a wall: " + r.Origin);
    }

    static void WalkIntoRamp()
    {
        var r = new Rig(new Vector3(-900, -183, 0));
        Rig.Floor(-183.0127f);
        Ramp(); // its face meets the floor at x = -683
        r.Run(0.3f);
        r.Yaw = 90;
        r.Move(1, 0);
        // Holding W into a surf ramp slides you up it slowly (like Source), but stepping
        // must never pop you up onto it or let you stand on it.
        bool groundedOnRamp = false;
        float biggestHop = 0, lastY = r.Origin.y, maxX = float.MinValue;
        r.Run(2f, () =>
        {
            if (r.OnGround && r.Origin.y > -180) groundedOnRamp = true;
            biggestHop = Math.Max(biggestHop, r.Origin.y - lastY);
            lastY = r.Origin.y;
            maxX = Math.Max(maxX, r.Origin.x);
        });
        Note($"reached x {maxX:F0}, height {r.Origin.y + 183:F1} u up the ramp, largest one-frame rise {biggestHop:F2} u (a step is 18)");
        Check(maxX > -700, "never reached the ramp");
        Check(!groundedOnRamp, "stood on the surf ramp");
        Check(biggestHop < 5, "stepped onto the ramp");
    }

    static void WalkDownSlope()
    {
        var r = new Rig(new Vector3(0, 300, 0));
        Rig.Box(Vector3.zero, new Vector3(4000, 400, 4000), rotZ: 30); // walkable, normal.y = 0.866
        r.Run(1f);
        r.Yaw = 270; // face -x, downhill
        r.Move(1, 0);
        int airTicks = 0;
        r.Run(1f, () => { if (!r.OnGround) airTicks++; });
        Note($"frames off the ground going downhill: {airTicks}");
        Check(airTicks == 0, "bounced down the slope");
        Check(Math.Abs(r.Speed - 250) < 1f, "speed " + r.Speed);
    }

    // ------------------------------------------------------------------ limits and robustness

    static void MaxVelocity()
    {
        var r = new Rig(new Vector3(0, 100000, 0));
        r.Run(6f);
        // Clamped to -3500 each tick; FinishGravity then adds its half tick (-4) like Source.
        Check(r.Vel.y <= -3500 && r.Vel.y >= -3504.01f, "vy " + r.Vel.y);
    }

    static void TeleportResets()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(1f);
        r.Move(0, 0);
        r.Teleport(new Vector3(5000, 0, 0));
        r.Frame();
        Check(r.Speed < 1f, "speed after teleport " + r.Speed);
        Check(Math.Abs(r.Origin.x - 5000) < 5, "sim did not follow " + r.Origin);
    }

    static void TeleportOntoFloor()
    {
        // Spawn points sit exactly on the floor, so the hull starts touching it (found in the ClientSim play test).
        foreach (bool viaApi in new[] { false, true })
        {
            var r = OnFloor();
            if (viaApi) r.TeleportPlayer(new Vector3(5000, 0, 0), 0, false);
            else r.Teleport(new Vector3(5000, 0, 0));
            float lowest = float.MaxValue;
            r.Run(0.5f, () => lowest = Math.Min(lowest, r.Origin.y));
            Note($"{(viaApi ? "TeleportPlayer" : "TeleportTo")}: lowest hull height {lowest:F2} u, on ground {r.OnGround}");
            Check(lowest > -0.01f, "hull sank into the floor: " + lowest);
            Check(r.OnGround, "not standing after teleport");
        }
    }

    // Ladder test map: a 512 unit high block whose face is at z = 48, with an 8 unit ladder volume against it.
    static Rig LadderWall()
    {
        var r = OnFloor();
        Rig.Box(new Vector3(0, 256, 448), new Vector3(256, 512, 800));
        Rig.Ladder(new Vector3(0, 256, 44), new Vector3(64, 512, 8));
        return r;
    }

    static Rig OnLadder()
    {
        var r = LadderWall();
        r.Move(1, 0);
        r.Run(1f);
        Check(r.OnLadder, "did not grab the ladder");
        return r;
    }

    static void LadderClimb()
    {
        var r = OnLadder();
        float y0 = r.Origin.y;
        r.Run(1f);
        float climb = r.Origin.y - y0;
        Note($"climbed {climb:F1} u in 1 s, at z {r.Origin.z:F1} (ladder face 40)");
        Check(Math.Abs(climb - 200) < 3, "climb speed " + climb);
        Check(r.OnLadder, "fell off the ladder");
    }

    static void LadderClimbDown()
    {
        var r = OnLadder();
        r.Run(1f); // up to y ~200
        r.Pitch = 60;
        float y0 = r.Origin.y;
        r.Run(1f);
        float rate = r.Origin.y - y0;
        Note($"looking down 60 degrees: {rate:F1} u/s (Source: 200 * (cos 60 - sin 60) = -73.2)");
        Check(Math.Abs(rate + 73.2f) < 2, "down speed " + rate);
    }

    static void LadderHang()
    {
        var r = OnLadder();
        r.Run(0.5f);
        r.Move(0, 0);
        float y0 = r.Origin.y;
        r.Run(1f);
        Note($"hanging: moved {r.Origin.y - y0:F2} u in 1 s");
        Check(Math.Abs(r.Origin.y - y0) < 0.5f && r.OnLadder, "did not hang on");
    }

    static void LadderJumpOff()
    {
        var r = OnLadder();
        r.Run(0.5f);
        r.Move(0, 0);
        r.Jump(true);
        r.Frame();
        Note($"after jump: velocity {r.Vel}");
        Check(!r.OnLadder, "still on the ladder");
        Check(Math.Abs(r.Vel.z + 270) < 1 && Math.Abs(r.Vel.x) < 1, "push off velocity " + r.Vel);
    }

    static void LadderTop()
    {
        var r = OnLadder();
        r.Run(4f);
        Note($"after 4 s: at {r.Origin}, on ground {r.OnGround}, on ladder {r.OnLadder}");
        Check(r.OnGround && Math.Abs(r.Origin.y - 512) < 1 && r.Origin.z > 32, "not standing on the ledge");
    }

    static void LadderWalkOff()
    {
        var r = LadderWall();
        r.Move(1, 0);
        for (int i = 0; i < 100 && !r.OnLadder; i++) r.Frame();
        Check(r.OnLadder, "did not grab the ladder");
        r.Move(-1, 0);
        float z0 = r.Origin.z;
        r.Run(0.5f);
        Note($"S at the bottom: z {z0:F1} -> {r.Origin.z:F1}, on ladder {r.OnLadder}");
        Check(r.Origin.z < z0 - 50 && !r.OnLadder, "did not walk off");
    }

    static void LadderBehind()
    {
        var r = LadderWall();
        r.TeleportPlayer(new Vector3(0, 0, 4), 180, false); // ladder face 4 units behind the hull's back
        r.Run(0.1f);
        r.Move(1, 0);
        r.Run(0.5f);
        Note($"walking away from it: z {r.Origin.z:F1}, on ladder {r.OnLadder}");
        Check(!r.OnLadder && r.Origin.z < -50, "grabbed a ladder behind");
    }

    static void WaterSwim()
    {
        var r = OnFloor();
        Rig.Water(new Vector3(0, 200, 0), new Vector3(4000, 400, 4000));
        r.Move(1, 0);
        r.Run(2f);
        Note($"water level {r.WaterLevel}, horizontal speed {r.Speed:F1} u/s");
        Check(r.WaterLevel == 3 && Math.Abs(r.Speed - 200) < 2, "swim speed " + r.Speed);
    }

    static void WaterSink()
    {
        var r = new Rig(new Vector3(0, 200, 0));
        Rig.Floor();
        Rig.Water(new Vector3(0, 250, 0), new Vector3(4000, 500, 4000));
        r.Run(2f);
        Note($"sinking at {r.Vel.y:F1} u/s after 2 s (Source: 60 * 0.8 = 48)");
        Check(Math.Abs(r.Vel.y + 48) < 1, "sink speed " + r.Vel.y);
    }

    static void WaterSwimUp()
    {
        var r = new Rig(new Vector3(0, 100, 0));
        Rig.Floor();
        Rig.Water(new Vector3(0, 500, 0), new Vector3(4000, 1000, 4000));
        r.Run(0.1f);
        r.Jump(true);
        float y0 = r.Origin.y;
        r.Run(1f);
        Note($"holding jump: rose {r.Origin.y - y0:F1} u in 1 s");
        Check(r.Origin.y - y0 > 90 && r.Origin.y - y0 < 120, "swim up speed");
    }

    static void WaterShallow()
    {
        var r = OnFloor();
        Rig.Water(new Vector3(0, 10, 0), new Vector3(4000, 20, 4000));
        r.Move(1, 0);
        r.Run(2f);
        Note($"water level {r.WaterLevel}, speed {r.Speed:F1}");
        Check(r.WaterLevel == 1 && Math.Abs(r.Speed - 250) < 1, "ankle deep speed " + r.Speed);
    }

    static void WaterJumpOut()
    {
        // Water 40 deep (waist height is 36), a 40 unit ledge ahead at z = 200.
        var r = OnFloor();
        Rig.Water(new Vector3(0, 20, 0), new Vector3(4000, 40, 4000));
        Rig.Box(new Vector3(0, 20, 1200), new Vector3(4000, 40, 2000));
        r.Move(1, 0);
        float maxY = 0;
        r.Run(2f, () => maxY = Math.Max(maxY, r.Origin.y));
        Note($"after 2 s: at {r.Origin}, on ground {r.OnGround}, highest {maxY:F1} u");
        Check(r.OnGround && Math.Abs(r.Origin.y - 40) < 1 && r.Origin.z > 200, "did not climb out");
    }

    static void PushSideways()
    {
        var r = OnFloor();
        r.SetPush(new Vector3(0, 0, 500));
        float z0 = r.Origin.z;
        r.Run(0.5f);
        float moved = r.Origin.z - z0, inside = r.Vel.z;
        r.SetPush(Vector3.zero);
        r.Frame();
        Note($"inside: moved {moved:F1} u in 0.5 s with own speed {inside:F1}; after leaving: speed {r.Vel.z:F1}");
        Check(Math.Abs(moved - 250) < 3 && Math.Abs(inside) < 1, "push while inside");
        Check(r.Vel.z > 470 && r.Vel.z <= 500, "momentum after leaving " + r.Vel.z);
    }

    static void PushUp()
    {
        var r = OnFloor();
        r.SetPush(new Vector3(0, 1000, 0));
        r.Run(1f);
        Note($"after 1 s in a 1000 u/s upward push: height {r.Origin.y:F1}, vertical speed {r.Vel.y:F1} (theory 100, 200)");
        Check(!r.OnGround && Math.Abs(r.Vel.y - 200) < 5 && Math.Abs(r.Origin.y - 100) < 8, "upward push"); // tick steps add ~5 u
    }

    static void BoostUp()
    {
        var r = OnFloor();
        r.AddVelocity(new Vector3(0, 600, 0));
        float maxY = 0;
        r.Run(1.5f, () => maxY = Math.Max(maxY, r.Origin.y));
        Note($"600 u/s booster: peak {maxY:F1} u (theory 600^2 / 1600 = 225)");
        Check(Math.Abs(maxY - 225) < 5, "boost height " + maxY);
    }

    static void HalfGravity()
    {
        var r = OnFloor();
        r.SetGravityScale(0.5f);
        r.Jump(true);
        r.Frame();
        r.Jump(false);
        float maxY = 0;
        r.Run(2f, () => maxY = Math.Max(maxY, r.Origin.y));
        Note($"jump at half gravity: peak {maxY:F1} u (normal ~55.5, theory ~2x)");
        Check(Math.Abs(maxY - 111) < 4, "half gravity jump " + maxY);
    }

    static Rig Running()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(1f);
        r.Move(0, 0);
        return r;
    }

    static void TeleportKeepsVelocity()
    {
        var r = OnFloor();
        r.SetVelocity(new Vector3(0, 0, 600));
        r.TeleportPlayer(new Vector3(3000, 500, 0), 90, true);
        r.Frame();
        Note($"after teleport: at {r.Pos}, speed {r.Speed:F0} u/s");
        Check(Math.Abs(r.Pos.x - 3000) < 20 && r.Pos.y > 450, "not at destination " + r.Pos);
        Check(r.Speed > 590, "lost speed " + r.Speed);
    }

    static void TeleportPlayerResets()
    {
        var r = Running();
        r.TeleportPlayer(new Vector3(3000, 0, 0), 0, false);
        r.Frame();
        Check(r.Speed < 1, "speed " + r.Speed);
        Check(Math.Abs(r.Pos.x - 3000) < 5, "not at destination " + r.Pos);
    }

    static void ShortTeleport()
    {
        var r = OnFloor();
        r.SetVelocity(new Vector3(0, 0, 400));
        r.TeleportPlayer(new Vector3(100, 200, 0), 0, true); // in the air, so no ground friction
        r.Frame();
        Check(Math.Abs(r.Pos.x - 100) < 5 && r.Pos.y > 190, "not at destination " + r.Pos);
        Check(r.Speed > 399, "lost speed " + r.Speed);
    }

    static void DelayedTeleport()
    {
        var r = OnFloor();
        r.teleportDelayFrames = 3;
        r.SetVelocity(new Vector3(0, 0, 600));
        r.TeleportPlayer(new Vector3(3000, 500, 0), 0, true);
        r.Run(0.1f);
        Note($"10 frames later: at {r.Pos}, speed {r.Speed:F0} u/s");
        Check(Math.Abs(r.Pos.x - 3000) < 20 && r.Pos.y > 450, "not at destination " + r.Pos);
        Check(r.Speed > 590, "lost speed " + r.Speed);
    }

    static void RespawnResets()
    {
        var r = OnFloor();
        r.Move(1, 0);
        r.Run(1f);
        r.Move(0, 0);
        r.Respawn();
        r.Frame();
        Check(r.Speed < 1f, "speed after respawn " + r.Speed);
    }

    static void FrameRateIndependent()
    {
        foreach (float fps in new[] { 45f, 90f, 144f })
        {
            var r = OnFloor();
            r.frameTime = 1f / fps;
            r.Move(1, 0);
            r.Run(2f);
            Check(Math.Abs(r.Speed - 250) < 0.5f, $"{fps} fps speed {r.Speed}");
            float y0 = r.Origin.y, maxY = y0;
            r.Jump(true);
            r.Frame();
            r.Jump(false);
            r.Run(1f, () => maxY = Math.Max(maxY, r.Origin.y));
            Check(Math.Abs(maxY - y0 - 55.5f) < 0.5f, $"{fps} fps jump {maxY - y0}");
            Check(Math.Abs(r.Pos.y - r.Origin.y) < 6f, $"{fps} fps player off simulation by {r.Pos.y - r.Origin.y}");
        }
    }

    static void LatencyTolerant()
    {
        var r = OnFloor();
        r.oneFrameLatency = true;
        r.frameTime = 1f / 72f;
        r.Move(1, 0);
        r.Run(1f);
        r.Jump(true);
        float worst = 0;
        r.Run(3f, () => worst = Math.Max(worst, (r.Pos - r.Origin).magnitude));
        Note($"worst player/simulation gap {worst:F1} u");
        Check(r.Speed > 240, "speed " + r.Speed);
        Check(worst < 20, "gap " + worst);
    }

    static void Deactivate()
    {
        var r = OnFloor();
        Check(r.PlayerWalk == 0 && r.PlayerGravity == 0, "VRChat movement not disabled");
        r.Move(1, 0);
        r.Run(1f);
        r.SetActive(false);
        Check(r.PlayerWalk == 2 && r.PlayerRun == 4 && r.PlayerGravity == 1, "not restored");
        Check(Math.Abs(r.PlayerVelocity.magnitude / Rig.U - 250) < 1, "momentum lost");
    }

    static void CastBudget()
    {
        var r = OnFloor();
        Rig.Box(new Vector3(300, 8, 0), new Vector3(400, 16, 1000));
        r.Move(1, 0);
        r.Jump(true);
        CollisionWorld.castCount = 0;
        r.Run(5f);
        float perTick = CollisionWorld.castCount / 500f;
        Note($"{perTick:F1} box casts per tick ({perTick * 100:F0}/s at 100 tick)");
        Check(perTick < 8, "too many casts");
    }
}

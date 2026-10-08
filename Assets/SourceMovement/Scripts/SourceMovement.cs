using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

/// <summary>
/// Source engine (CS:S / HL2 / GMod) player movement for VRChat.
///
/// A direct port of Source SDK 2013 gamemovement.cpp: friction, ground acceleration,
/// air strafing, jumping, slide-move against any surface (surfing) and stair stepping.
///
/// VRChat's own walking/jumping/gravity is switched off. Every frame we run fixed-rate
/// Source ticks on our own box hull (like Source's bounding box), then set the real
/// player's velocity so they arrive where the simulation says they should be.
///
/// All physics runs in Source units (1 Hammer unit = 0.01905 m) on Unity axes (Y is up).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMovement : UdonSharpBehaviour
{
    [Header("Movement cvars (Source units, bhop server defaults)")]
    [Tooltip("sv_gravity")] public float gravity = 800f;
    [Tooltip("sv_accelerate")] public float accelerate = 5f;
    [Tooltip("sv_airaccelerate (CS:S default 10, surf servers 100-150, bhop servers 1000)")] public float airAccelerate = 1000f;
    [Tooltip("sv_friction")] public float friction = 4f;
    [Tooltip("sv_stopspeed")] public float stopSpeed = 75f;
    [Tooltip("Player max speed (CS:S knife = 250)")] public float maxSpeed = 250f;
    [Tooltip("sv_maxvelocity, clamped per axis")] public float maxVelocity = 3500f;
    [Tooltip("Air wish speed cap, 30 in every Source game. This is what makes air strafing work.")] public float airSpeedCap = 30f;
    [Tooltip("Jump velocity, sqrt(2 * 800 * 57) = 57 unit jump")] public float jumpImpulse = 301.993377f;
    [Tooltip("sv_stepsize, highest step you walk up without jumping")] public float stepSize = 18f;
    [Tooltip("Server tickrate (bhop servers use 100, CS:S default 66)")] public float tickRate = 100f;

    [Header("Player hull (Source units, CS:S standing hull)")]
    public float hullWidth = 32f;
    public float hullHeight = 72f;

    [Header("World")]
    [Tooltip("Metres per Source unit. 0.01905 = 1 Hammer unit, so imported Source maps feel identical.")]
    public float metersPerUnit = 0.01905f;
    [Tooltip("Layers the player hull collides with")]
    public LayerMask collisionLayers = (1 << 0) | (1 << 11); // Default, Environment

    [Header("Controls")]
    [Tooltip("Start with Source movement on. Turn off if a SourceMovementZone switches it on.")]
    public bool activeOnStart = true;
    [Tooltip("On: hold jump to bhop. Off: every jump needs a fresh press (scroll wheel jumps too).")]
    public bool autoBhop = true;
    [Tooltip("Desktop key that toggles auto bhop")]
    public KeyCode autoBhopToggleKey = KeyCode.B;

    private const float ForwardSpeed = 450f;      // cl_forwardspeed / cl_sidespeed
    private const float NonJumpVelocity = 140f;   // moving up faster than this = not on ground
    private const float WalkableNormal = 0.7f;    // steeper than ~45.6 degrees is a surf ramp
    private const float GroundCheckDistance = 2f;
    private const float DistEpsilon = 0.03125f;
    private const float Skin = 0.25f;             // gap kept between hull and surfaces
    private const float ResyncDistance = 8f;      // real player drifted from simulation
    private const float TeleportDistance = 64f;   // real player was teleported
    private const int MaxBumps = 4;
    private const int MaxClipPlanes = 5;
    private const int MaxTicksPerFrame = 8;

    private VRCPlayerApi localPlayer;
    private bool active;
    private bool pendingResync;
    private float savedWalk, savedRun, savedStrafe, savedJump, savedGravity;

    // Simulation state (Source units)
    private Vector3 origin;
    private Vector3 prevOrigin;
    private Vector3 lastTarget;
    private Vector3 velocity;
    private bool onGround;
    private float surfaceFriction = 1f;
    private float accumulator;

    // Input
    private float moveForward;
    private float moveRight;
    private bool jumpHeld;
    private bool scrollJump;
    private bool oldJump;
    private float prevYaw;
    private Vector3 wishForward;
    private Vector3 wishRight;

    // Hull and trace results
    private Vector3 hullHalf;
    private Vector3 hullCenter;
    private float trFraction;
    private Vector3 trEnd;
    private Vector3 trNormal;
    private Vector3[] planes = new Vector3[MaxClipPlanes];

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (localPlayer == null) return;
        savedWalk = localPlayer.GetWalkSpeed();
        savedRun = localPlayer.GetRunSpeed();
        savedStrafe = localPlayer.GetStrafeSpeed();
        savedJump = localPlayer.GetJumpImpulse();
        savedGravity = localPlayer.GetGravityStrength();
        if (activeOnStart) SetMovementActive(true);
    }

    /// <summary>Switch between Source movement and normal VRChat movement, keeping momentum.</summary>
    public void SetMovementActive(bool on)
    {
        if (localPlayer == null || on == active) return;
        active = on;
        if (on)
        {
            localPlayer.SetWalkSpeed(0f);
            localPlayer.SetRunSpeed(0f);
            localPlayer.SetStrafeSpeed(0f);
            localPlayer.SetJumpImpulse(0f);
            localPlayer.SetGravityStrength(0f);
            velocity = localPlayer.GetVelocity() / metersPerUnit;
            pendingResync = true;
        }
        else
        {
            localPlayer.SetWalkSpeed(savedWalk);
            localPlayer.SetRunSpeed(savedRun);
            localPlayer.SetStrafeSpeed(savedStrafe);
            localPlayer.SetJumpImpulse(savedJump);
            localPlayer.SetGravityStrength(savedGravity);
            localPlayer.SetVelocity(velocity * metersPerUnit);
        }
    }

    public void _ToggleAutoBhop() { autoBhop = !autoBhop; }

    /// <summary>Horizontal speed in Source units per second (what a CS:S speedometer shows).</summary>
    public float GetSpeed() { return new Vector3(velocity.x, 0f, velocity.z).magnitude; }
    public Vector3 GetSourceVelocity() { return velocity; }
    public bool IsOnGround() { return onGround; }
    public bool IsMovementActive() { return active; }

    public override void InputMoveVertical(float value, UdonInputEventArgs args) { moveForward = value; }
    public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { moveRight = value; }
    public override void InputJump(bool value, UdonInputEventArgs args) { jumpHeld = value; }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        velocity = Vector3.zero;
        pendingResync = true;
    }

    private void Update()
    {
        if (!active) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (Input.GetKeyDown(autoBhopToggleKey)) _ToggleAutoBhop();
        if (Input.GetAxis("Mouse ScrollWheel") != 0f) scrollJump = true;

        hullHalf = new Vector3(hullWidth * 0.5f, hullHeight * 0.5f, hullWidth * 0.5f);
        hullCenter = new Vector3(0f, hullHeight * 0.5f, 0f);

        // Follow the real player if something else moved them (teleport, respawn, blocked).
        Vector3 actual = localPlayer.GetPosition() / metersPerUnit;
        float lag = velocity.magnitude * dt * 2f;
        float drift = (actual - lastTarget).magnitude;
        if (pendingResync || drift > TeleportDistance + lag)
        {
            if (!pendingResync) velocity = Vector3.zero;
            origin = actual;
            prevOrigin = actual;
            lastTarget = actual;
            accumulator = 0f;
            pendingResync = false;
        }
        else if (drift > ResyncDistance + lag)
        {
            origin = actual;
            prevOrigin = actual;
            lastTarget = actual;
        }

        // Run fixed Source ticks, turning the view smoothly across them.
        float tick = 1f / tickRate;
        accumulator += dt;
        int ticks = (int)(accumulator / tick);
        if (ticks > MaxTicksPerFrame)
        {
            ticks = MaxTicksPerFrame;
            accumulator = ticks * tick;
        }
        float yaw = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation.eulerAngles.y;
        for (int i = 0; i < ticks; i++)
        {
            SetWishAxes(Mathf.LerpAngle(prevYaw, yaw, (i + 1f) / ticks));
            prevOrigin = origin;
            PlayerMove(tick);
            accumulator -= tick;
        }
        if (ticks > 0) prevYaw = yaw;

        // Drive the real player: this frame's simulated motion plus half the remaining error.
        // Correcting only half keeps it stable even if VRChat applies the velocity a frame late.
        Vector3 target = Vector3.Lerp(prevOrigin, origin, accumulator / tick);
        Vector3 drive = (target - lastTarget) + (lastTarget - actual) * 0.5f;
        lastTarget = target;
        localPlayer.SetVelocity(drive * (metersPerUnit / dt));
    }

    private void SetWishAxes(float yaw)
    {
        float rad = yaw * Mathf.Deg2Rad;
        wishForward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        wishRight = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
    }

    // ---------------------------------------------------------------- Source movement

    private void PlayerMove(float dt)
    {
        CategorizePosition();
        FullWalkMove(dt);
    }

    private void FullWalkMove(float dt)
    {
        // StartGravity
        velocity.y -= gravity * 0.5f * dt;
        CheckVelocity();

        bool jump = jumpHeld || scrollJump;
        scrollJump = false;
        if (jump)
        {
            if (autoBhop) oldJump = false;
            CheckJumpButton(dt);
        }
        else
        {
            oldJump = false;
        }

        if (onGround)
        {
            velocity.y = 0f;
            Friction(dt);
        }
        CheckVelocity();

        if (onGround) WalkMove(dt);
        else AirMove(dt);

        CategorizePosition();
        CheckVelocity();

        // FinishGravity
        if (!onGround) velocity.y -= gravity * 0.5f * dt;
        if (onGround) velocity.y = 0f;
    }

    private void CheckJumpButton(float dt)
    {
        if (!onGround)
        {
            oldJump = true;
            return;
        }
        if (oldJump) return; // don't pogo stick

        onGround = false;
        velocity.y += jumpImpulse;
        velocity.y -= gravity * 0.5f * dt; // FinishGravity
        oldJump = true;
    }

    private void Friction(float dt)
    {
        float speed = velocity.magnitude;
        if (speed < 0.1f) return;
        float control = speed < stopSpeed ? stopSpeed : speed;
        float drop = control * friction * surfaceFriction * dt;
        float newSpeed = speed - drop;
        if (newSpeed < 0f) newSpeed = 0f;
        velocity *= newSpeed / speed;
    }

    private Vector3 GetWishVelocity()
    {
        Vector3 wishVel = wishForward * (moveForward * ForwardSpeed) + wishRight * (moveRight * ForwardSpeed);
        if (wishVel.magnitude > maxSpeed) wishVel = wishVel.normalized * maxSpeed;
        return wishVel;
    }

    private void WalkMove(float dt)
    {
        Vector3 wishVel = GetWishVelocity();
        velocity.y = 0f;
        Accelerate(wishVel.normalized, wishVel.magnitude, accelerate, dt);
        velocity.y = 0f;

        if (velocity.magnitude < 1f)
        {
            velocity = Vector3.zero;
            return;
        }

        // First try moving straight to the destination.
        Vector3 dest = origin + velocity * dt;
        TraceHull(origin, dest);
        if (trFraction == 1f)
        {
            origin = trEnd;
            StayOnGround();
            return;
        }

        StepMove(dt);
        StayOnGround();
    }

    private void AirMove(float dt)
    {
        Vector3 wishVel = GetWishVelocity();
        AirAccelerate(wishVel.normalized, wishVel.magnitude, airAccelerate, dt);
        TryPlayerMove(dt);
    }

    private void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float addSpeed = wishSpeed - Vector3.Dot(velocity, wishDir);
        if (addSpeed <= 0f) return;
        float accelSpeed = accel * dt * wishSpeed * surfaceFriction;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;
        velocity += wishDir * accelSpeed;
    }

    private void AirAccelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float cappedSpeed = wishSpeed > airSpeedCap ? airSpeedCap : wishSpeed;
        float addSpeed = cappedSpeed - Vector3.Dot(velocity, wishDir);
        if (addSpeed <= 0f) return;
        float accelSpeed = accel * wishSpeed * dt * surfaceFriction;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;
        velocity += wishDir * accelSpeed;
    }

    /// <summary>Slide along everything we touch. This is what makes surfing work.</summary>
    private void TryPlayerMove(float dt)
    {
        Vector3 originalVelocity = velocity;
        Vector3 primalVelocity = velocity;
        float timeLeft = dt;
        float allFraction = 0f;
        int numPlanes = 0;

        for (int bump = 0; bump < MaxBumps; bump++)
        {
            if (velocity.sqrMagnitude == 0f) break;

            TraceHull(origin, origin + velocity * timeLeft);
            allFraction += trFraction;
            if (trFraction > 0f)
            {
                origin = trEnd;
                originalVelocity = velocity;
                numPlanes = 0;
            }
            if (trFraction == 1f) break;

            timeLeft -= timeLeft * trFraction;
            if (numPlanes >= MaxClipPlanes)
            {
                velocity = Vector3.zero;
                break;
            }
            planes[numPlanes] = trNormal;
            numPlanes++;

            if (numPlanes == 1 && !onGround)
            {
                velocity = ClipVelocity(originalVelocity, planes[0]);
                originalVelocity = velocity;
            }
            else
            {
                int i;
                for (i = 0; i < numPlanes; i++)
                {
                    velocity = ClipVelocity(originalVelocity, planes[i]);
                    int j;
                    for (j = 0; j < numPlanes; j++)
                    {
                        if (j != i && Vector3.Dot(velocity, planes[j]) < 0f) break;
                    }
                    if (j == numPlanes) break;
                }
                if (i == numPlanes)
                {
                    // Stuck in a crease: slide along it, or stop in a corner.
                    if (numPlanes != 2)
                    {
                        velocity = Vector3.zero;
                        break;
                    }
                    Vector3 dir = Vector3.Cross(planes[0], planes[1]).normalized;
                    velocity = dir * Vector3.Dot(dir, velocity);
                }

                // Don't bounce back the way we came (avoids corner jitter).
                if (Vector3.Dot(velocity, primalVelocity) <= 0f)
                {
                    velocity = Vector3.zero;
                    break;
                }
            }
        }

        if (allFraction == 0f) velocity = Vector3.zero;
    }

    private Vector3 ClipVelocity(Vector3 v, Vector3 normal)
    {
        Vector3 result = v - normal * Vector3.Dot(v, normal);
        float adjust = Vector3.Dot(result, normal);
        if (adjust < 0f) result -= normal * adjust;
        return result;
    }

    /// <summary>Try walking up a stair. Only runs on the ground, so surf ramps never step.</summary>
    private void StepMove(float dt)
    {
        Vector3 startPos = origin;
        Vector3 startVel = velocity;

        // Plain slide move.
        TryPlayerMove(dt);
        Vector3 downPos = origin;
        Vector3 downVel = velocity;

        // Step up, move, step back down.
        origin = startPos;
        velocity = startVel;
        TraceHull(origin, origin + Vector3.up * (stepSize + DistEpsilon));
        origin = trEnd;
        TryPlayerMove(dt);
        TraceHull(origin, origin - Vector3.up * (stepSize + DistEpsilon));

        // Landed on something too steep (or nothing): keep the plain slide move.
        if (trNormal.y < WalkableNormal)
        {
            origin = downPos;
            velocity = downVel;
            return;
        }
        origin = trEnd;

        // Keep whichever went further.
        float downDist = (downPos.x - startPos.x) * (downPos.x - startPos.x) + (downPos.z - startPos.z) * (downPos.z - startPos.z);
        float upDist = (origin.x - startPos.x) * (origin.x - startPos.x) + (origin.z - startPos.z) * (origin.z - startPos.z);
        if (downDist > upDist)
        {
            origin = downPos;
            velocity = downVel;
        }
        else
        {
            velocity.y = downVel.y;
        }
    }

    /// <summary>Snap down onto stairs and slopes we walk off, so we don't skip down them.</summary>
    private void StayOnGround()
    {
        TraceHull(origin, origin + Vector3.up * GroundCheckDistance);
        Vector3 start = trEnd;
        TraceHull(start, origin - Vector3.up * stepSize);
        if (trFraction > 0f && trFraction < 1f && trNormal.y >= WalkableNormal)
        {
            if (Mathf.Abs(origin.y - trEnd.y) > 0.5f * DistEpsilon) origin = trEnd;
        }
    }

    private void CategorizePosition()
    {
        if (velocity.y > NonJumpVelocity)
        {
            onGround = false;
            return;
        }

        TraceHull(origin, origin - Vector3.up * GroundCheckDistance);
        if (trFraction == 1f || trNormal.y < WalkableNormal)
        {
            onGround = false;
            if (velocity.y > 0f) surfaceFriction = 0.25f;
        }
        else
        {
            onGround = true;
            velocity.y = 0f;
            surfaceFriction = 1f;
        }
    }

    private void CheckVelocity()
    {
        if (float.IsNaN(velocity.x) || float.IsNaN(velocity.y) || float.IsNaN(velocity.z)) velocity = Vector3.zero;
        velocity.x = Mathf.Clamp(velocity.x, -maxVelocity, maxVelocity);
        velocity.y = Mathf.Clamp(velocity.y, -maxVelocity, maxVelocity);
        velocity.z = Mathf.Clamp(velocity.z, -maxVelocity, maxVelocity);
    }

    /// <summary>
    /// Sweep the player's axis-aligned box (Source's bounding box) from start to end.
    /// Sets trFraction (1 = no hit), trEnd and trNormal. Stops Skin units short of surfaces,
    /// measured along the surface normal like Source's DIST_EPSILON.
    /// </summary>
    private void TraceHull(Vector3 start, Vector3 end)
    {
        trFraction = 1f;
        trEnd = end;
        trNormal = Vector3.zero;

        Vector3 delta = end - start;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return;
        Vector3 dir = delta / dist;

        RaycastHit hit;
        if (!Physics.BoxCast((start + hullCenter) * metersPerUnit, hullHalf * metersPerUnit, dir, out hit,
            Quaternion.identity, (dist + Skin) * metersPerUnit, collisionLayers, QueryTriggerInteraction.Ignore)) return;

        // Started inside something: ignore it so we can move out instead of getting stuck.
        if (hit.distance <= 0f && hit.point == Vector3.zero) return;

        float cos = -Vector3.Dot(dir, hit.normal);
        if (cos < 0.0001f) cos = 0.0001f;
        float fraction = (hit.distance / metersPerUnit - Skin / cos) / dist;
        if (fraction >= 1f) return;
        if (fraction < 0f) fraction = 0f;

        trFraction = fraction;
        trEnd = start + delta * fraction;
        trNormal = hit.normal;
    }
}

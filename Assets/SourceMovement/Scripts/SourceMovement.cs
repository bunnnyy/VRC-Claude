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
    [Tooltip("Layers of ladder volumes (CS:S func_ladder): trigger colliders against the climbable face. " +
        "You pass through them, the wall behind stops you. Layer 22 is the first free user layer in VRChat.")]
    public LayerMask ladderLayers = 1 << 22;
    [Tooltip("Layers of water volumes: trigger colliders filling the water. Layer 4 is Unity's Water layer.")]
    public LayerMask waterLayers = 1 << 4;

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
    private const int TeleportWaitFrames = 30;    // how long to wait for VRChat to finish our teleport
    private const float MaxClimbSpeed = 200f;     // MAX_CLIMB_SPEED
    private const float LadderJumpSpeed = 270f;   // jumping off a ladder
    private const float LadderDistance = 2f;      // how close a ladder must be to grab it
    private const float ViewHeight = 64f;         // eye height of the standing hull
    private const float SwimUpSpeed = 100f;       // jump in water
    private const float SinkSpeed = 60f;          // no keys in water
    private const float WaterJumpUp = 256f;       // climbing out of water onto a ledge
    private const int MaxBumps = 4;
    private const int MaxClipPlanes = 5;
    private const int MaxTicksPerFrame = 8;

    private VRCPlayerApi localPlayer;
    private bool active;
    private bool pendingResync;
    private int teleportWait;
    private float savedWalk, savedRun, savedStrafe, savedJump, savedGravity;

    // Simulation state (Source units)
    private Vector3 origin;
    private Vector3 prevOrigin;
    private Vector3 lastTarget;
    private Vector3 velocity;
    private bool onGround;
    private bool onLadder;
    private Vector3 pushVelocity;  // trigger_push we're inside (Source's base velocity)
    private float gravityScale = 1f; // player gravity from map triggers (AddOutput gravity, trigger_gravity)
    private int waterLevel;        // 0 dry, 1 feet, 2 waist, 3 eyes
    private float waterJumpTime;
    private Vector3 waterJumpVel;
    private Vector3 ladderNormal;
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
    private Vector3 viewForward; // with pitch, for ladders
    private float pitch;

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
        UpdateHull();
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

    /// <summary>
    /// Teleport the local player, optionally keeping their Source velocity (portals, stage teleports).
    /// Plain VRCPlayerApi.TeleportTo works too: a jump over 64 units resets velocity, smaller ones keep it.
    /// </summary>
    public void TeleportPlayer(Vector3 position, Quaternion rotation, bool keepVelocity)
    {
        if (localPlayer == null) return;
        localPlayer.TeleportTo(position, rotation);
        localPlayer.SetVelocity(Vector3.zero); // VRChat keeps the old velocity through a teleport
        if (!keepVelocity) velocity = Vector3.zero;
        PlaceAt(position / metersPerUnit);
        teleportWait = TeleportWaitFrames;
    }

    /// <summary>
    /// trigger_push (SourcePushTrigger): push velocity in Source units/s while inside, Vector3.zero when leaving.
    /// Like Source, a sideways push moves you without becoming your speed until you leave; then it is added as
    /// momentum. An upward push accelerates you (against gravity) and keeps you off the ground.
    /// </summary>
    public void SetPush(Vector3 push)
    {
        if (push == Vector3.zero) velocity += new Vector3(pushVelocity.x, 0f, pushVelocity.z);
        pushVelocity = push;
        if (push.y > 0f) onGround = false;
    }

    /// <summary>Booster (AddOutput basevelocity, SourceBoostTrigger): add this velocity once, in Source units/s.</summary>
    public void AddVelocity(Vector3 impulse)
    {
        velocity += impulse;
        if (impulse.y > 0f) onGround = false;
    }

    /// <summary>Player gravity multiplier (AddOutput gravity, trigger_gravity). 1 is normal.</summary>
    public void SetGravityScale(float scale) { gravityScale = scale; }

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
        pushVelocity = Vector3.zero;
        gravityScale = 1f;
        pendingResync = true;
    }

    private void Update()
    {
        if (!active) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (Input.GetKeyDown(autoBhopToggleKey)) _ToggleAutoBhop();
        if (Input.GetAxis("Mouse ScrollWheel") != 0f) scrollJump = true;

        UpdateHull();

        Vector3 actual = localPlayer.GetPosition() / metersPerUnit;

        // After TeleportPlayer, hold still until VRChat has actually moved the player.
        if (teleportWait > 0)
        {
            teleportWait--;
            if (teleportWait > 0 && (actual - origin).magnitude > ResyncDistance)
            {
                localPlayer.SetVelocity(Vector3.zero);
                return;
            }
            teleportWait = 0;
        }

        // Follow the real player if something else moved them (teleport, respawn, blocked).
        float lag = velocity.magnitude * dt * 2f;
        float drift = (actual - lastTarget).magnitude;
        if (pendingResync || drift > TeleportDistance + lag)
        {
            if (!pendingResync) velocity = Vector3.zero;
            PlaceAt(actual);
            accumulator = 0f;
            pendingResync = false;
        }
        else if (drift > ResyncDistance + lag)
        {
            PlaceAt(actual);
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
        Vector3 view = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation.eulerAngles;
        float yaw = view.y;
        pitch = view.x > 180f ? view.x - 360f : view.x;
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

    private void UpdateHull()
    {
        hullHalf = new Vector3(hullWidth * 0.5f, hullHeight * 0.5f, hullWidth * 0.5f);
        hullCenter = new Vector3(0f, hullHeight * 0.5f, 0f);
    }

    /// <summary>
    /// Move the simulation to a new position. Spawn points and teleport targets usually sit exactly on the
    /// floor, where the hull would start inside it and fall through, so drop it from a step above instead.
    /// </summary>
    private void PlaceAt(Vector3 pos)
    {
        TraceHull(pos + Vector3.up * stepSize, pos);
        origin = trEnd;
        onLadder = false;
        waterJumpTime = 0f;
        prevOrigin = origin;
        lastTarget = origin;
    }

    private void SetWishAxes(float yaw)
    {
        float rad = yaw * Mathf.Deg2Rad;
        wishForward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        wishRight = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));
        float p = pitch * Mathf.Deg2Rad; // looking down is positive in Unity
        viewForward = wishForward * Mathf.Cos(p) + Vector3.down * Mathf.Sin(p);
    }

    // ---------------------------------------------------------------- Source movement

    private void PlayerMove(float dt)
    {
        CategorizePosition();
        if (LadderMove()) MoveWithPush(dt); // FullLadderMove: no gravity, no friction
        else FullWalkMove(dt);
    }

    /// <summary>
    /// Source's LadderMove. Grab a ladder by moving towards it, then W/S climb along the view (look up or down),
    /// A/D move sideways, jump pushes off. Sets the velocity; returns false when not on a ladder.
    /// </summary>
    private bool LadderMove()
    {
        Vector3 wishDir;
        if (onLadder) wishDir = -ladderNormal; // already climbing: keep holding on
        else
        {
            if (moveForward == 0f && moveRight == 0f) return false;
            wishDir = (viewForward * moveForward + wishRight * moveRight).normalized;
        }

        // Is there a ladder within 2 units that way? We may already be inside the ladder volume, where a cast
        // can't see it, so cast from one hull width back. A hit behind our position only counts if we touch it.
        Vector3 start = origin - wishDir * hullWidth;
        RaycastHit hit;
        onLadder = Physics.BoxCast((start + hullCenter) * metersPerUnit, hullHalf * metersPerUnit, wishDir, out hit,
            Quaternion.identity, (hullWidth + LadderDistance) * metersPerUnit, ladderLayers, QueryTriggerInteraction.Collide)
            && hit.point != Vector3.zero
            && (hit.distance >= hullWidth * metersPerUnit || Physics.CheckBox((origin + hullCenter) * metersPerUnit,
                hullHalf * metersPerUnit, Quaternion.identity, ladderLayers, QueryTriggerInteraction.Collide));
        if (!onLadder) return false;
        ladderNormal = hit.normal;

        bool jump = jumpHeld || scrollJump;
        scrollJump = false;
        if (jump)
        {
            onLadder = false;
            oldJump = true;
            velocity = ladderNormal * LadderJumpSpeed;
            return true;
        }

        float forwardSpeed = moveForward * MaxClimbSpeed;
        float rightSpeed = moveRight * MaxClimbSpeed;
        if (forwardSpeed == 0f && rightSpeed == 0f)
        {
            velocity = Vector3.zero;
            return true;
        }

        // Turn movement into the ladder face into climbing along it.
        Vector3 wishVel = viewForward * forwardSpeed + wishRight * rightSpeed;
        Vector3 perp = Vector3.Cross(Vector3.up, ladderNormal).normalized;
        float into = Vector3.Dot(wishVel, ladderNormal);
        Vector3 lateral = wishVel - ladderNormal * into;
        velocity = lateral - Vector3.Cross(ladderNormal, perp) * into;
        if (onGround && into > 0f) velocity += ladderNormal * MaxClimbSpeed; // walking away at the bottom
        return true;
    }

    private void FullWalkMove(float dt)
    {
        // StartGravity (not when swimming)
        if (waterLevel < 2) velocity.y -= gravity * gravityScale * 0.5f * dt;
        velocity.y += pushVelocity.y * dt; // an upward trigger_push accelerates you
        CheckVelocity();

        // Climbing out of water onto a ledge.
        if (waterJumpTime > 0f)
        {
            WaterJump(dt);
            TryPlayerMove(dt);
            return;
        }

        bool jump = jumpHeld || scrollJump;
        scrollJump = false;
        if (waterLevel >= 2)
        {
            if (waterLevel == 2) CheckWaterJump();
            if (jump) CheckJumpButton(dt);
            else oldJump = false;
            WaterMove(dt);
            CategorizePosition();
            if (onGround) velocity.y = 0f;
            return;
        }

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
        if (!onGround && waterLevel < 2) velocity.y -= gravity * gravityScale * 0.5f * dt;
        if (onGround) velocity.y = 0f;
    }

    private void CheckJumpButton(float dt)
    {
        if (waterJumpTime > 0f) return;
        if (waterLevel >= 2)
        {
            // Swimming, not jumping.
            onGround = false;
            velocity.y = SwimUpSpeed;
            return;
        }
        if (!onGround)
        {
            oldJump = true;
            return;
        }
        if (oldJump) return; // don't pogo stick

        onGround = false;
        velocity.y += jumpImpulse;
        velocity.y -= gravity * gravityScale * 0.5f * dt; // FinishGravity
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

        Vector3 push = new Vector3(pushVelocity.x, 0f, pushVelocity.z);
        velocity += push;
        if (velocity.magnitude < 1f) velocity = Vector3.zero;
        else WalkStep(dt);
        velocity -= push;
    }

    private void WalkStep(float dt)
    {

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
        MoveWithPush(dt);
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
        CheckWater();
        if (velocity.y > NonJumpVelocity || pushVelocity.y > 0f)
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

    /// <summary>TryPlayerMove with a horizontal trigger_push added for this move only (Source's base velocity).</summary>
    private void MoveWithPush(float dt)
    {
        Vector3 push = new Vector3(pushVelocity.x, 0f, pushVelocity.z);
        velocity += push;
        TryPlayerMove(dt);
        velocity -= push;
    }

    // ---------------------------------------------------------------- water

    /// <summary>How deep we are: 1 feet, 2 waist, 3 eyes. Returns true when swimming (waist deep or more).</summary>
    private bool CheckWater()
    {
        waterLevel = 0;
        if (waterLayers.value != 0 && InWater(origin + Vector3.up))
        {
            waterLevel = 1;
            if (InWater(origin + Vector3.up * (hullHeight * 0.5f)))
            {
                waterLevel = 2;
                if (InWater(origin + Vector3.up * ViewHeight)) waterLevel = 3;
            }
        }
        return waterLevel > 1;
    }

    private bool InWater(Vector3 point)
    {
        return Physics.CheckBox(point * metersPerUnit, Vector3.one * 0.001f, Quaternion.identity, waterLayers,
            QueryTriggerInteraction.Collide);
    }

    /// <summary>Source's WaterMove: swim along the view at 80% of max speed, sink slowly with no keys.</summary>
    private void WaterMove(float dt)
    {
        Vector3 wishVel = viewForward * (moveForward * ForwardSpeed) + wishRight * (moveRight * ForwardSpeed);
        if (jumpHeld) wishVel.y += maxSpeed;
        else if (moveForward == 0f && moveRight == 0f) wishVel.y -= SinkSpeed;
        else wishVel.y += Mathf.Clamp(moveForward * ForwardSpeed * viewForward.y * 2f, 0f, maxSpeed);

        float wishSpeed = wishVel.magnitude;
        Vector3 wishDir = wishSpeed > 0f ? wishVel / wishSpeed : Vector3.zero;
        if (wishSpeed > maxSpeed) wishSpeed = maxSpeed;
        wishSpeed *= 0.8f;

        // Water friction.
        float speed = velocity.magnitude;
        float newSpeed = 0f;
        if (speed > 0f)
        {
            newSpeed = speed - dt * speed * friction * surfaceFriction;
            if (newSpeed < 0.1f) newSpeed = 0f;
            velocity *= newSpeed / speed;
        }

        // Water acceleration.
        if (wishSpeed >= 0.1f)
        {
            float addSpeed = wishSpeed - newSpeed;
            if (addSpeed > 0f)
            {
                float accelSpeed = accelerate * wishSpeed * dt * surfaceFriction;
                if (accelSpeed > addSpeed) accelSpeed = addSpeed;
                velocity += wishDir * accelSpeed;
            }
        }

        velocity += pushVelocity;
        SwimStep(dt);
        velocity -= pushVelocity;
    }

    /// <summary>Move, pressing down from a step above so we swim up slopes and stairs.</summary>
    private void SwimStep(float dt)
    {
        Vector3 dest = origin + velocity * dt;
        TraceHull(origin, dest);
        if (trFraction == 1f)
        {
            TraceHull(dest + Vector3.up * (stepSize + 1f), dest);
            origin = trEnd;
            return;
        }
        if (!onGround) TryPlayerMove(dt);
        else StepMove(dt);
    }

    /// <summary>Waist deep against a wall with a ledge we can stand on just above: jump out.</summary>
    private void CheckWaterJump()
    {
        if (waterJumpTime > 0f || velocity.y < -180f) return;
        Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
        if (flatVel.sqrMagnitude > 0f && Vector3.Dot(flatVel, wishForward) < 0f) return; // backing up

        Vector3 start = origin + Vector3.up * (hullHeight * 0.5f);
        TraceHull(start, start + wishForward * 24f);
        if (trFraction == 1f) return; // nothing in front at the waist
        Vector3 jumpVel = -trNormal * 50f;

        start = origin + Vector3.up * (ViewHeight + 8f);
        TraceHull(start, start + wishForward * 24f);
        if (trFraction < 1f) return; // blocked at eye height too
        TraceHull(trEnd, trEnd - Vector3.up * 1024f);
        if (trFraction < 1f && trNormal.y >= WalkableNormal)
        {
            velocity.y = WaterJumpUp;
            oldJump = true;
            waterJumpTime = 2f;
            waterJumpVel = jumpVel;
        }
    }

    private void WaterJump(float dt)
    {
        waterJumpTime -= dt;
        if (waterJumpTime <= 0f || waterLevel == 0) waterJumpTime = 0f;
        velocity.x = waterJumpVel.x;
        velocity.z = waterJumpVel.z;
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

        // Mesh colliders report an edge normal where two triangles meet, even inside a flat face, which acts
        // like a wall that isn't there (surfers stop dead mid-ramp). Use the face normal under the hit point.
        Vector3 normal = hit.normal;
        RaycastHit face;
        float back = Skin * metersPerUnit;
        if (Physics.Raycast(hit.point + normal * back, -normal, out face, back * 2f, collisionLayers, QueryTriggerInteraction.Ignore)
            && Vector3.Dot(dir, face.normal) < 0f) normal = face.normal;

        float cos = -Vector3.Dot(dir, normal);
        if (cos < 0.0001f) cos = 0.0001f;
        float fraction = (hit.distance / metersPerUnit - Skin / cos) / dist;
        if (fraction >= 1f) return;
        if (fraction < 0f) fraction = 0f;

        trFraction = fraction;
        trEnd = start + delta * fraction;
        trNormal = normal;
    }
}

using UnityEngine;

/// <summary>
/// Kinematic spider on the X/Y plane: ground, wall climb, web crawl, and rope swing.
/// </summary>
public class SpiderController : MonoBehaviour
{
    public enum SpiderState
    {
        Idle,
        Walking,
        Jumping,
        Falling,
        Climbing,
        OnWeb,
        Rappelling
    }

    [SerializeField] WebBody web;
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float climbSpeed = 3.5f;
    [SerializeField] float jumpSpeed = 19f;
    [SerializeField] float gravity = 28f;
    [SerializeField] float radius = 0.35f;
    [SerializeField] float skin = 0.03f;
    [SerializeField] float webMoveSpeed = 4.5f;
    [SerializeField] float attachDistance = 0.9f;
    [SerializeField] float riderWeight = 36f;
    [SerializeField] float swingAccel = 11f;
    [SerializeField] float ropeClimbSpeed = 2.6f;
    [SerializeField] float ropeDropSpeed = 3.2f;

    SpiderState state = SpiderState.Idle;
    Vector2 velocity;
    float inputX;
    float inputY;
    bool jumpPressed;
    bool layToggle;
    bool connectPressed;
    bool rappelPressed;
    float webIgnore;

    bool grounded;
    int wallSide;
    float climbX;
    float climbCooldown;
    bool onWeb;
    int webSegment;
    float webT;
    int ropeNode;
    float ropeLength;

    public SpiderState State => state;

    void Update()
    {
        // The camera looks along -Z, so world +X is the left side of the screen.
        inputX = -Input.GetAxisRaw("Horizontal");
        inputY = Input.GetAxisRaw("Vertical");

        if (Input.GetKeyDown(KeyCode.Space))
            jumpPressed = true;
        if (Input.GetKeyDown(KeyCode.Q))
            layToggle = true;
        if (Input.GetKeyDown(KeyCode.E))
            connectPressed = true;
        if (Input.GetKeyDown(KeyCode.R))
            rappelPressed = true;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (climbCooldown > 0f)
            climbCooldown -= dt;
        if (webIgnore > 0f)
            webIgnore -= dt;

        HandleWebKeys();

        if (web != null)
        {
            if (onWeb)
                MoveOnWeb(dt);

            if (onWeb)
                web.SetRider(webSegment, webT, riderWeight);
            else
                web.ClearRider();

            if (state == SpiderState.Rappelling)
                ApplyRopeLoad(dt);
            else
                web.ClearRopeLoad();

            web.Simulate(dt);
        }

        if (onWeb)
            PlaceOnWeb();
        else if (state == SpiderState.Rappelling)
            SimulateRappel(dt);
        else if (state == SpiderState.Climbing)
            SimulateClimb(dt);
        else
            SimulateBody(dt);

        if (web == null)
            return;

        if (web.IsLaying)
        {
            web.Lay(Center);
            web.Preview(Center);
        }
        else if (state == SpiderState.Rappelling)
            web.ShowRope(web.GetNodePosition(ropeNode), Center);
        else
        {
            web.HideRope();
            if (!onWeb && webIgnore <= 0f && TryAttach())
                PlaceOnWeb();
        }
    }

    void HandleWebKeys()
    {
        if (web == null)
        {
            layToggle = false;
            connectPressed = false;
            rappelPressed = false;
            return;
        }

        if (layToggle)
        {
            if (web.IsLaying)
                StopLay();
            else
                TryStartLay();
            layToggle = false;
        }

        if (connectPressed)
        {
            web.ConnectToNearest(Center, 0.9f);
            connectPressed = false;
        }

        if (rappelPressed)
        {
            if (state != SpiderState.Rappelling)
                TryBeginRappel();
            rappelPressed = false;
        }

    }

    void TryStartLay()
    {
        if (onWeb)
            web.BeginLay(web.GetNodePosition(web.GetSegmentNode(webSegment, webT >= 0.5f)), 0.25f, false);
        else if (state == SpiderState.Rappelling)
            web.BeginLay(web.GetNodePosition(ropeNode), 0.25f, false);
        else if (grounded || state == SpiderState.Climbing)
            web.BeginLay(SurfaceAnchor(), 0.7f, true);
        else
            web.BeginLay(Center, 0.7f, false);
    }

    void StopLay()
    {
        // Q always cuts the strand. Joining another node stays on E.
        // Auto-join was grabbing nodes that had snapped onto a nearby platform, so Q looked like it did nothing.
        Vector2 end = grounded || state == SpiderState.Climbing ? SurfaceAnchor() : Center;
        bool pinEnd = grounded || state == SpiderState.Climbing || onWeb;
        web.EndLay(end, pinEnd);
    }

    Vector2 SurfaceAnchor()
    {
        Vector3 origin = new Vector3(Center.x, Center.y, 0f);
        if (grounded && Physics.Raycast(origin, Vector3.down, out RaycastHit groundHit, radius + 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (groundHit.normal.y > 0.5f)
                return new Vector2(groundHit.point.x, groundHit.point.y);
        }

        if (state == SpiderState.Climbing && wallSide != 0)
        {
            Vector3 towardWall = new Vector3(wallSide, 0f, 0f);
            if (Physics.Raycast(origin, towardWall, out RaycastHit wallHit, radius + 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(wallHit.normal.x) > 0.6f)
                return new Vector2(wallHit.point.x, wallHit.point.y);
        }

        return Center;
    }

    bool TryAttach()
    {
        if (web == null || !web.HasWeb || web.IsLaying || state == SpiderState.Rappelling)
            return false;

        float reach = radius + 0.28f;
        if (!web.TryGetClosest(Center, reach, out int segment, out float t))
            return false;

        onWeb = true;
        webSegment = segment;
        webT = t;
        state = SpiderState.OnWeb;
        velocity = Vector2.zero;
        grounded = false;
        wallSide = 0;
        return true;
    }

    void TryBeginRappel()
    {
        int node = -1;
        if (onWeb && web.HasWeb)
        {
            node = web.GetSegmentNode(webSegment, webT >= 0.5f);
        }
        else if (!web.TryGetClosestNode(Center, attachDistance + 0.3f, out node))
        {
            return;
        }

        ropeNode = node;
        ropeLength = Vector2.Distance(Center, web.GetNodePosition(ropeNode));
        ropeLength = Mathf.Max(0.55f, ropeLength);
        if (onWeb)
            velocity = web.GetPointVelocity(webSegment, webT, Time.fixedDeltaTime);

        onWeb = false;
        web.ClearRider();
        grounded = false;
        wallSide = 0;
        state = SpiderState.Rappelling;
    }

    void ReleaseWeb(bool jumpOff)
    {
        if (state == SpiderState.Rappelling)
        {
            web.ClearRopeLoad();
            if (jumpOff)
                velocity.y = Mathf.Max(velocity.y, jumpSpeed * 0.85f);
        }
        else if (onWeb)
        {
            velocity = web.GetPointVelocity(webSegment, webT, Time.fixedDeltaTime);
            if (velocity.magnitude > 12f)
                velocity = velocity.normalized * 12f;
            if (jumpOff)
                velocity.y = Mathf.Max(velocity.y, jumpSpeed * 0.85f);
            web.ClearRider();
        }

        onWeb = false;
        state = velocity.y > 0.1f ? SpiderState.Jumping : SpiderState.Falling;
        climbCooldown = 0.12f;
        webIgnore = 0.3f;
    }

    void ApplyRopeLoad(float dt)
    {
        if (ropeNode < 0)
            return;

        Vector2 anchor = web.GetNodePosition(ropeNode);
        Vector2 toSpider = Center - anchor;
        float distance = toSpider.magnitude;
        if (distance < ropeLength * 0.85f || distance < 0.001f)
        {
            web.ClearRopeLoad();
            return;
        }

        float dtSq = dt * dt;
        web.SetRopeLoad(ropeNode, toSpider / distance * (riderWeight * dtSq));
    }

    void SimulateRappel(float dt)
    {
        if (jumpPressed)
        {
            jumpPressed = false;
            ReleaseWeb(true);
            return;
        }

        if (inputY > 0.2f)
            ropeLength = Mathf.Max(0.55f, ropeLength - ropeClimbSpeed * dt);
        else if (inputY < -0.2f)
            ropeLength += ropeDropSpeed * dt;

        velocity.y -= gravity * dt;
        velocity.x += inputX * swingAccel * dt;
        velocity *= 1f - 0.4f * dt;

        Move(velocity * dt);

        Vector2 anchor = web.GetNodePosition(ropeNode);
        Vector2 offset = Center - anchor;
        float distance = offset.magnitude;
        if (distance > ropeLength && distance > 0.001f)
        {
            Vector2 direction = offset / distance;
            SetPosition(anchor + direction * ropeLength);
            float outward = Vector2.Dot(velocity, direction);
            if (outward > 0f)
                velocity -= direction * outward;
        }

        if (velocity.y <= 0.01f && ProbeGround(Center, 0.25f, out float groundY) && groundY > Center.y)
        {
            SetPosition(new Vector2(Center.x, groundY));
            velocity.y = 0f;
        }

        LiftOutOfGround();
    }

    void MoveOnWeb(float dt)
    {
        Vector2 a = web.GetPoint(webSegment, 0f);
        Vector2 b = web.GetPoint(webSegment, 1f);
        Vector2 direction = b - a;
        float length = direction.magnitude;
        if (length > 0.001f)
            direction /= length;

        float along = inputX * direction.x + inputY * direction.y;
        web.MoveAlong(ref webSegment, ref webT, along * webMoveSpeed * dt);

        if (jumpPressed)
        {
            jumpPressed = false;
            ReleaseWeb(true);
        }
    }

    void PlaceOnWeb()
    {
        if (!onWeb || web == null || !web.HasWeb)
            return;

        Vector2 point = web.GetPoint(webSegment, webT);
        point.y = Mathf.Max(point.y, FloorTop(point.x, point.y) + radius + skin);
        SetPosition(point);
    }

    void SimulateBody(float dt)
    {
        velocity.x = inputX * moveSpeed;

        if (grounded)
        {
            if (velocity.y < 0f)
                velocity.y = 0f;
            if (jumpPressed)
            {
                velocity.y = jumpSpeed;
                grounded = false;
                jumpPressed = false;
            }
        }
        else
        {
            velocity.y -= gravity * dt;
        }

        jumpPressed = false;
        Move(velocity * dt);
        LiftOutOfGround();

        bool pushingIntoWall = wallSide != 0 && inputX * wallSide > 0.5f && climbCooldown <= 0f;
        bool wantsClimb = pushingIntoWall && (!grounded || inputY > 0.5f);
        if (wantsClimb)
        {
            BeginClimb();
            return;
        }

        if (grounded)
            state = Mathf.Abs(inputX) > 0.01f ? SpiderState.Walking : SpiderState.Idle;
        else if (velocity.y > 0.1f)
            state = SpiderState.Jumping;
        else
            state = SpiderState.Falling;
    }

    void SimulateClimb(float dt)
    {
        Vector2 pos = Center;
        pos.x = climbX;

        float dy = inputY * climbSpeed * dt;
        pos = MoveVertical(pos, dy);
        SetPosition(pos);

        if (jumpPressed)
        {
            jumpPressed = false;
            velocity = new Vector2(-wallSide * moveSpeed * 0.45f, jumpSpeed);
            state = SpiderState.Jumping;
            grounded = false;
            climbCooldown = 0.15f;
            return;
        }

        if (inputX * wallSide < -0.5f)
        {
            velocity = new Vector2(-wallSide * 2.5f, 0f);
            state = SpiderState.Falling;
            grounded = false;
            climbCooldown = 0.12f;
            return;
        }

        if (inputY > 0.1f && TryMantle())
            return;

        grounded = ProbeGround(Center, 0.08f, out float groundY);
        if (grounded && inputY <= 0f)
        {
            SetPosition(new Vector2(Center.x, groundY));
            velocity = Vector2.zero;
            state = Mathf.Abs(inputX) > 0.01f ? SpiderState.Walking : SpiderState.Idle;
            climbCooldown = 0.08f;
            return;
        }

        if (!TouchingWall())
        {
            velocity = Vector2.zero;
            state = SpiderState.Falling;
            grounded = false;
        }
    }

    void BeginClimb()
    {
        state = SpiderState.Climbing;
        velocity = Vector2.zero;
        grounded = false;
        climbX = Center.x;

        if (Cast(Center, new Vector2(wallSide, 0f), 0.4f, out CastHit hit) && Mathf.Abs(hit.normal.x) > 0.6f)
        {
            float travel = Mathf.Max(0f, hit.distance - skin);
            climbX = Center.x + wallSide * travel;
        }

        SetPosition(new Vector2(climbX, Center.y));
    }

    bool TryMantle()
    {
        Vector2 probe = new Vector2(Center.x + wallSide * (radius + 0.08f), Center.y + 0.05f);
        if (!Cast(probe, Vector2.down, radius + 0.25f, out CastHit hit))
            return false;
        if (hit.normal.y < 0.5f)
            return false;

        float restCenterY = probe.y - hit.distance;
        if (restCenterY < Center.y - radius)
            return false;

        SetPosition(new Vector2(probe.x, restCenterY + skin));
        velocity = Vector2.zero;
        grounded = true;
        state = SpiderState.Idle;
        climbCooldown = 0.12f;
        return true;
    }

    bool TouchingWall()
    {
        return Cast(Center, new Vector2(wallSide, 0f), skin + 0.12f, out CastHit hit)
            && Mathf.Abs(hit.normal.x) > 0.6f;
    }

    void Move(Vector2 delta)
    {
        Vector2 pos = Center;
        wallSide = 0;

        if (Mathf.Abs(delta.x) > 0.0001f)
        {
            float dir = Mathf.Sign(delta.x);
            float distance = Mathf.Abs(delta.x);
            if (Cast(pos, new Vector2(dir, 0f), distance, out CastHit hit) && Mathf.Abs(hit.normal.x) > 0.6f)
            {
                float travel = Mathf.Max(0f, hit.distance - skin);
                pos.x += dir * Mathf.Min(distance, travel);
                velocity.x = 0f;
                wallSide = hit.normal.x > 0f ? -1 : 1;
            }
            else
            {
                pos.x += delta.x;
            }
        }

        pos = MoveVertical(pos, delta.y);
        SetPosition(pos);

        if (velocity.y <= 0.01f && ProbeGround(pos, 0.1f, out float groundY))
        {
            pos.y = groundY;
            SetPosition(pos);
            velocity.y = 0f;
            grounded = true;
        }
        else
        {
            grounded = false;
        }
    }

    Vector2 MoveVertical(Vector2 pos, float dy)
    {
        if (Mathf.Abs(dy) < 0.0001f)
            return pos;

        float dir = Mathf.Sign(dy);
        float distance = Mathf.Abs(dy);
        if (Cast(pos, new Vector2(0f, dir), distance, out CastHit hit) && hit.normal.y * dir < -0.5f)
        {
            float travel = Mathf.Max(0f, hit.distance - skin);
            pos.y += dir * Mathf.Min(distance, travel);
            if (dir > 0f)
                velocity.y = 0f;
        }
        else
        {
            pos.y += dy;
        }

        return pos;
    }

    bool ProbeGround(Vector2 pos, float distance, out float standY)
    {
        standY = pos.y;
        if (!Cast(pos, Vector2.down, distance, out CastHit hit))
            return false;
        if (hit.normal.y < 0.5f)
            return false;

        // Negative distance means the body is already inside the floor, so this pushes it back out.
        standY = pos.y - (hit.distance - skin);
        return true;
    }

    void LiftOutOfGround()
    {
        float stand = FloorTop(Center.x, Center.y) + radius + skin;
        if (Center.y >= stand)
            return;

        SetPosition(new Vector2(Center.x, stand));
        if (velocity.y < 0f)
            velocity.y = 0f;
        grounded = true;
    }

    float FloorTop(float x, float y)
    {
        float originY = Mathf.Max(y, 0.5f) + radius + 3f;
        var origin = new Vector3(x, originY, 0f);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, originY + 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return float.NegativeInfinity;
        if (hit.normal.y < 0.5f)
            return float.NegativeInfinity;
        return hit.point.y;
    }

    bool Cast(Vector2 origin, Vector2 direction, float distance, out CastHit hit)
    {
        hit = default;
        Vector3 start = new Vector3(origin.x, origin.y, 0f);
        Vector3 dir = new Vector3(direction.x, direction.y, 0f);
        if (!Physics.Raycast(start, dir, out RaycastHit rayHit, distance + radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        float centerTravel = rayHit.distance - radius;
        if (centerTravel > distance)
            return false;

        // Negative when the body already overlaps the surface, so ground snap can push back out.
        hit.distance = centerTravel;
        hit.normal = new Vector2(rayHit.normal.x, rayHit.normal.y);
        return true;
    }

    struct CastHit
    {
        public float distance;
        public Vector2 normal;
    }

    Vector2 Center => new Vector2(transform.position.x, transform.position.y);

    void SetPosition(Vector2 position)
    {
        transform.position = new Vector3(position.x, position.y, 0f);
    }

    void OnGUI()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.normal.textColor = Color.white;
            labelStyle.fontSize = 15;
        }

        GUI.Label(
            new Rect(12f, 12f, 720f, 170f),
            "A/D ou setas: andar e balançar\nEspaço: pular ou soltar a teia\nQ: começar ou parar a teia\nE: ligar a teia a outro nó    R: rapel\nW/S: escalar, subir ou descer a corda\nCírculo: inseto chegando, sem marcar o ponto exato\nEstado: " + state,
            labelStyle);
    }

    static GUIStyle labelStyle;
}

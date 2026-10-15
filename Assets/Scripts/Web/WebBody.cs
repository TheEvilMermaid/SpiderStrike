using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Web as nodes plus distance constraints. Strands share nodes when connected.
/// Gameplay lives in X/Y. Z is only used when drawing.
/// </summary>
public class WebBody : MonoBehaviour
{
    const int SpanSegments = 20;
    const float MinLength = 0.75f;
    const float PointSpacing = 0.45f;

    [SerializeField] float gravity = 16f;
    [SerializeField] float damping = 0.03f;
    [SerializeField] float slack = 1.15f;
    [SerializeField] int constraintIterations = 12;
    [SerializeField] float lineWidth = 0.04f;

    readonly List<WebNode> nodes = new List<WebNode>();
    readonly List<WebSegment> segments = new List<WebSegment>();
    readonly List<LineRenderer> segmentLines = new List<LineRenderer>();
    readonly List<StuckLoad> stuck = new List<StuckLoad>();
    static readonly Collider[] overlapBuffer = new Collider[8];

    LineRenderer previewLine;
    Material lineMaterial;
    bool placing;
    bool laying;
    bool laidSegment;
    int tipNode = -1;
    Vector2 anchorA;
    Vector2 previewEnd;
    bool riderActive;
    int riderSegment;
    float riderT;
    float riderWeight;
    int ropeNode = -1;
    Vector2 ropeLoad;
    int layOrigin = -1;
    int tipSegment = -1;
    bool createdAnchor;

    public bool HasWeb => segments.Count > 0;
    public bool IsPlacing => placing;
    public bool IsLaying => laying;
    public int SegmentCount => segments.Count;

    void Awake()
    {
        EnsurePreviewLine();
    }

    public void StartAnchor(Vector2 point)
    {
        ClearWeb();
        placing = true;
        anchorA = point;
        previewEnd = point;
        DrawPreview(anchorA, previewEnd);
    }

    public void ShowRope(Vector2 from, Vector2 to)
    {
        DrawPreview(from, to);
    }

    public void HideRope()
    {
        if (!laying && !placing)
            HidePreview();
    }

    public void Preview(Vector2 end)
    {
        if (laying && layOrigin >= 0)
        {
            // Once the strand exists, the segments themselves are the silk.
            if (tipSegment >= 0)
            {
                HidePreview();
                return;
            }

            DrawPreview(nodes[layOrigin].position, end);
            return;
        }

        if (!placing)
        {
            HidePreview();
            return;
        }

        previewEnd = end;
        DrawPreview(anchorA, previewEnd);
    }

    public bool FinishAnchor(Vector2 point)
    {
        if (!placing)
            return false;

        placing = false;
        HidePreview();
        Vector2 delta = point - anchorA;
        float length = delta.magnitude;
        if (length < MinLength)
        {
            ClearWeb();
            return false;
        }

        nodes.Clear();
        segments.Clear();

        for (int i = 0; i <= SpanSegments; i++)
        {
            float u = i / (float)SpanSegments;
            Vector2 position = Vector2.Lerp(anchorA, point, u);
            AddNode(position, i == 0 || i == SpanSegments);
        }

        float rest = (length / SpanSegments) * slack;
        for (int i = 0; i < SpanSegments; i++)
            AddSegment(i, i + 1, rest);

        DrawSegments();
        return true;
    }

    public bool BeginLay(Vector2 point, float connectRadius, bool allowNewAnchor)
    {
        if (laying)
            return false;

        int existing = FindNode(point, connectRadius, -1, false);
        if (existing >= 0)
        {
            layOrigin = existing;
            createdAnchor = false;
        }
        else if (allowNewAnchor)
        {
            layOrigin = AddNode(point, true);
            createdAnchor = true;
        }
        else
        {
            return false;
        }

        // The origin stays put. A separate tip is created on the spider and is the only locked end.
        tipNode = layOrigin;
        tipSegment = -1;
        laying = true;
        laidSegment = false;
        return true;
    }

    public void Lay(Vector2 point)
    {
        if (!laying || layOrigin < 0)
            return;

        bool fresh = tipSegment < 0;
        if (fresh)
        {
            float fromAnchor = Vector2.Distance(nodes[layOrigin].position, point);
            if (fromAnchor < 0.08f)
                return;

            tipNode = AddNode(point, true);
            tipSegment = AddSegment(layOrigin, tipNode, fromAnchor);
            laidSegment = true;
        }
        else
        {
            LeaveCorner(point);
        }

        // Split the silk that is already out. Do this before moving the hand,
        // so a sagging node is not retied in a straight line to the spider.
        SplitPaidSilk();
        if (tipSegment >= 0)
        {
            nodes[tipNode].position = point;
            nodes[tipNode].previous = point;
            nodes[tipNode].locked = true;
        }

        SplitPaidSilk();
        if (tipSegment >= 0)
        {
            int prev = OtherEnd(tipSegment, tipNode);
            float live = Vector2.Distance(nodes[prev].position, nodes[tipNode].position);
            // The piece in the spider's hand stays taut. Older pieces keep their slack and can hang.
            SetSegment(tipSegment, prev, tipNode, Mathf.Max(0.05f, live));
        }

        DrawSegments();
    }

    void SplitPaidSilk()
    {
        int guard = 0;
        while (tipSegment >= 0 && guard++ < 40)
        {
            int prev = OtherEnd(tipSegment, tipNode);
            Vector2 from = nodes[prev].position;
            Vector2 to = nodes[tipNode].position;
            Vector2 delta = to - from;
            float distance = delta.magnitude;
            if (distance < PointSpacing)
                return;

            Vector2 at = from + delta / distance * PointSpacing;
            if ((to - at).sqrMagnitude < 0.0025f)
                return;

            int mid = AddNode(at, false);
            nodes[mid].previous = at;
            SetSegment(tipSegment, prev, mid, PointSpacing * slack);
            tipSegment = AddSegment(mid, tipNode, Vector2.Distance(at, to));
            laidSegment = true;
        }
    }

    public void EndLay(Vector2 point, bool lockEnd)
    {
        if (!laying)
            return;

        Lay(point);
        if (tipSegment >= 0 && tipNode >= 0 && tipNode < nodes.Count)
        {
            int prev = OtherEnd(tipSegment, tipNode);
            nodes[tipNode].position = point;
            nodes[tipNode].previous = point;
            nodes[tipNode].locked = lockEnd && laidSegment;
            float length = Vector2.Distance(nodes[prev].position, point);
            SetSegment(tipSegment, prev, tipNode, Mathf.Max(0.05f, length * slack));
        }
        else if (!laidSegment && createdAnchor && layOrigin >= 0 && layOrigin < nodes.Count && Degree(layOrigin) == 0)
        {
            nodes.RemoveAt(layOrigin);
        }

        laying = false;
        tipNode = -1;
        tipSegment = -1;
        layOrigin = -1;
        HidePreview();
        DrawSegments();
    }

    public bool ConnectToNearest(Vector2 point, float radius)
    {
        if (!laying || tipNode < 0)
            return false;

        int other = FindNode(point, radius, tipNode, true);
        if (other < 0)
            return false;

        float distance = Vector2.Distance(nodes[tipNode].position, nodes[other].position);
        if (distance >= 0.08f && !HasSegment(tipNode, other))
            AddSegment(tipNode, other, distance * slack);

        if (tipNode != layOrigin && tipNode >= 0 && tipNode < nodes.Count)
        {
            nodes[tipNode].locked = false;
            nodes[tipNode].previous = nodes[tipNode].position;
        }

        laying = false;
        tipNode = -1;
        tipSegment = -1;
        layOrigin = -1;
        HidePreview();
        DrawSegments();
        return true;
    }

    void LeaveCorner(Vector2 point)
    {
        if (tipSegment < 0 || tipNode < 0)
            return;

        int prev = OtherEnd(tipSegment, tipNode);
        Vector2 oldHand = nodes[tipNode].position;
        Vector2 incoming = oldHand - nodes[prev].position;
        Vector2 turn = point - oldHand;
        if (incoming.sqrMagnitude < 0.04f || turn.sqrMagnitude < 0.04f)
            return;
        if (Vector2.Angle(incoming, turn) <= 32f)
            return;

        nodes[tipNode].locked = false;
        nodes[tipNode].previous = nodes[tipNode].position;
        SetSegment(tipSegment, prev, tipNode, Vector2.Distance(nodes[prev].position, oldHand) * slack);

        int committed = tipNode;
        tipNode = AddNode(point, true);
        tipSegment = AddSegment(committed, tipNode, Vector2.Distance(oldHand, point));
        laidSegment = true;
    }

    public void Simulate(float dt)
    {
        if (nodes.Count == 0)
            return;

        float dtSq = dt * dt;
        Vector2 gravityStep = Vector2.down * (gravity * dtSq);
        float keep = 1f - damping;

        for (int i = 0; i < nodes.Count; i++)
        {
            WebNode node = nodes[i];
            if (node.locked)
                continue;

            Vector2 inertia = (node.position - node.previous) * keep;
            node.previous = node.position;
            node.position += inertia + gravityStep;
        }

        if (ropeNode >= 0 && ropeNode < nodes.Count)
            PullNode(ropeNode, ropeLoad);

        if (riderActive && riderSegment >= 0 && riderSegment < segments.Count)
        {
            WebSegment ridden = segments[riderSegment];
            Vector2 pull = Vector2.down * (riderWeight * dtSq);
            PullNode(ridden.a, pull * (1f - riderT));
            PullNode(ridden.b, pull * riderT);
        }

        for (int i = 0; i < stuck.Count; i++)
        {
            StuckLoad load = stuck[i];
            if (load.segment < 0 || load.segment >= segments.Count)
                continue;

            WebSegment seg = segments[load.segment];
            Vector2 pull = Vector2.down * (load.weight * dtSq);
            PullNode(seg.a, pull * (1f - load.t));
            PullNode(seg.b, pull * load.t);
        }

        for (int iteration = 0; iteration < constraintIterations; iteration++)
        {
            for (int i = 0; i < segments.Count; i++)
                Solve(segments[i]);
        }

        for (int i = 0; i < nodes.Count; i++)
            KeepOutOfSolids(nodes[i]);

        DrawSegments();
    }

    public void SetRider(int segment, float t, float weight)
    {
        riderActive = true;
        riderSegment = segment;
        riderT = Mathf.Clamp01(t);
        riderWeight = weight;
    }

    public void ClearRider()
    {
        riderActive = false;
    }

    public void SetRopeLoad(int node, Vector2 load)
    {
        ropeNode = node;
        ropeLoad = load;
    }

    public void ClearRopeLoad()
    {
        ropeNode = -1;
        ropeLoad = Vector2.zero;
    }

    public void StickLoad(int segment, float t, float weight, Vector2 kick)
    {
        if (segment < 0 || segment >= segments.Count)
            return;

        t = Mathf.Clamp01(t);
        stuck.Add(new StuckLoad
        {
            segment = segment,
            t = t,
            weight = weight
        });

        WebSegment seg = segments[segment];
        PullNode(seg.a, kick * (1f - t));
        PullNode(seg.b, kick * t);
    }

    public bool TryGetClosest(Vector2 point, float maxDistance, out int segment, out float t)
    {
        segment = 0;
        t = 0f;
        float best = maxDistance * maxDistance;
        bool found = false;

        for (int i = 0; i < segments.Count; i++)
        {
            WebSegment seg = segments[i];
            Vector2 a = nodes[seg.a].position;
            Vector2 b = nodes[seg.b].position;
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            float u = lengthSq < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            Vector2 closest = a + ab * u;
            float distSq = (point - closest).sqrMagnitude;
            if (distSq > best)
                continue;

            best = distSq;
            segment = i;
            t = u;
            found = true;
        }

        return found;
    }

    public bool TryGetClosestNode(Vector2 point, float maxDistance, out int node)
    {
        node = FindNode(point, maxDistance, -1, false);
        return node >= 0;
    }

    public int GetSegmentNode(int segment, bool end)
    {
        WebSegment seg = segments[segment];
        return end ? seg.b : seg.a;
    }

    public Vector2 GetNodePosition(int index)
    {
        return nodes[index].position;
    }

    public Vector2 GetNodeVelocity(int index, float dt)
    {
        WebNode node = nodes[index];
        return (node.position - node.previous) / Mathf.Max(dt, 0.0001f);
    }

    public Vector2 GetPoint(int segment, float t)
    {
        WebSegment seg = segments[segment];
        return Vector2.Lerp(nodes[seg.a].position, nodes[seg.b].position, Mathf.Clamp01(t));
    }

    public Vector2 GetPointVelocity(int segment, float t, float dt)
    {
        WebSegment seg = segments[segment];
        WebNode a = nodes[seg.a];
        WebNode b = nodes[seg.b];
        Vector2 velocityA = a.position - a.previous;
        Vector2 velocityB = b.position - b.previous;
        return Vector2.Lerp(velocityA, velocityB, Mathf.Clamp01(t)) / Mathf.Max(dt, 0.0001f);
    }

    public void MoveAlong(ref int segment, ref float t, float delta)
    {
        if (segments.Count == 0 || Mathf.Abs(delta) < 0.0001f)
            return;

        float remaining = delta;
        for (int guard = 0; guard < 24; guard++)
        {
            if (segment < 0 || segment >= segments.Count)
                return;

            float length = Mathf.Max(SegmentLength(segment), 0.0001f);
            float next = t * length + remaining;
            if (next >= 0f && next <= length)
            {
                t = next / length;
                return;
            }

            WebSegment seg = segments[segment];
            bool throughEnd = next > length;
            int through = throughEnd ? seg.b : seg.a;
            Vector2 incoming = throughEnd
                ? nodes[seg.b].position - nodes[seg.a].position
                : nodes[seg.a].position - nodes[seg.b].position;
            float leftover = Mathf.Abs(throughEnd ? next - length : next);

            int nextSegment = FindContinuation(segment, through, incoming);
            if (nextSegment < 0)
            {
                t = throughEnd ? 1f : 0f;
                return;
            }

            WebSegment continuation = segments[nextSegment];
            bool startsHere = continuation.a == through;
            Vector2 outgoing = startsHere
                ? nodes[continuation.b].position - nodes[continuation.a].position
                : nodes[continuation.a].position - nodes[continuation.b].position;
            Vector2 inDir = incoming.sqrMagnitude > 0.0001f ? incoming.normalized : Vector2.right;
            float sign = outgoing.sqrMagnitude < 0.0001f || Vector2.Dot(outgoing.normalized, inDir) >= 0f ? 1f : -1f;

            segment = nextSegment;
            t = startsHere ? 0f : 1f;
            remaining = (startsHere ? sign : -sign) * leftover;
        }
    }

    int FindContinuation(int fromSegment, int throughNode, Vector2 incoming)
    {
        Vector2 inDir = incoming.sqrMagnitude > 0.0001f ? incoming.normalized : Vector2.right;
        int best = -1;
        float bestDot = -0.15f;

        for (int i = 0; i < segments.Count; i++)
        {
            if (i == fromSegment)
                continue;

            WebSegment seg = segments[i];
            if (seg.a != throughNode && seg.b != throughNode)
                continue;

            Vector2 away = seg.a == throughNode
                ? nodes[seg.b].position - nodes[seg.a].position
                : nodes[seg.a].position - nodes[seg.b].position;
            if (away.sqrMagnitude < 0.0001f)
                continue;

            float dot = Vector2.Dot(away.normalized, inDir);
            if (dot <= bestDot)
                continue;

            bestDot = dot;
            best = i;
        }

        return best;
    }

    int FindNode(Vector2 point, float radius, int exclude, bool skipNeighborsOfExclude)
    {
        float best = radius * radius;
        int found = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (i == exclude)
                continue;
            if (skipNeighborsOfExclude && exclude >= 0 && (IsNeighbor(exclude, i) || i == exclude))
                continue;

            float distSq = (nodes[i].position - point).sqrMagnitude;
            if (distSq > best)
                continue;

            best = distSq;
            found = i;
        }

        return found;
    }

    bool IsNeighbor(int a, int b)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            WebSegment seg = segments[i];
            if ((seg.a == a && seg.b == b) || (seg.a == b && seg.b == a))
                return true;
        }

        return false;
    }

    bool HasSegment(int a, int b)
    {
        return IsNeighbor(a, b);
    }

    bool TryIncoming(int node, out Vector2 direction)
    {
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            WebSegment seg = segments[i];
            if (seg.b == node)
            {
                direction = nodes[seg.b].position - nodes[seg.a].position;
                return direction.sqrMagnitude > 0.0001f;
            }

            if (seg.a == node)
            {
                direction = nodes[seg.a].position - nodes[seg.b].position;
                return direction.sqrMagnitude > 0.0001f;
            }
        }

        direction = Vector2.zero;
        return false;
    }

    int Degree(int node)
    {
        int count = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            WebSegment seg = segments[i];
            if (seg.a == node || seg.b == node)
                count++;
        }

        return count;
    }

    int AddNode(Vector2 point, bool locked)
    {
        nodes.Add(new WebNode
        {
            position = point,
            previous = point,
            locked = locked
        });
        return nodes.Count - 1;
    }

    int AddSegment(int a, int b, float restLength)
    {
        segments.Add(new WebSegment
        {
            a = a,
            b = b,
            restLength = Mathf.Max(0.05f, restLength)
        });
        return segments.Count - 1;
    }

    void SetSegment(int index, int a, int b, float restLength)
    {
        segments[index] = new WebSegment
        {
            a = a,
            b = b,
            restLength = Mathf.Max(0.05f, restLength)
        };
    }

    int OtherEnd(int segment, int node)
    {
        WebSegment seg = segments[segment];
        return seg.a == node ? seg.b : seg.a;
    }

    float SegmentLength(int segment)
    {
        WebSegment seg = segments[segment];
        return Vector2.Distance(nodes[seg.a].position, nodes[seg.b].position);
    }

    static void KeepOutOfSolids(WebNode node)
    {
        if (node.locked)
            return;

        var point = new Vector3(node.position.x, node.position.y, 0f);
        int count = Physics.OverlapSphereNonAlloc(point, 0.04f, overlapBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool pushed = false;
        float best = float.MaxValue;
        Vector2 exit = node.position;

        for (int i = 0; i < count; i++)
        {
            Collider solid = overlapBuffer[i];
            if (solid == null || !NearestSurface(solid, point, out Vector3 surface, out float distSq) || distSq >= best)
                continue;

            best = distSq;
            exit = new Vector2(surface.x, surface.y);
            pushed = true;
        }

        if (pushed)
        {
            node.position = exit;
            node.previous = exit;
            return;
        }

        // A constraint step can shove a node clear through the floor, outside the collider.
        if (node.position.y >= 0f)
            return;

        node.position.y = 0f;
        node.previous = node.position;
    }

    static bool NearestSurface(Collider solid, Vector3 point, out Vector3 surface, out float distSq)
    {
        surface = point;
        distSq = float.MaxValue;
        bool found = false;
        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right };

        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 direction = directions[i];
            var ray = new Ray(point - direction * 12f, direction);
            if (!solid.Raycast(ray, out RaycastHit hit, 24f))
                continue;

            float d = (hit.point - point).sqrMagnitude;
            if (d >= distSq)
                continue;

            distSq = d;
            surface = hit.point;
            found = true;
        }

        return found;
    }

    void PullNode(int index, Vector2 delta)
    {
        WebNode node = nodes[index];
        if (node.locked)
            return;
        node.position += delta;
    }

    void Solve(WebSegment seg)
    {
        WebNode a = nodes[seg.a];
        WebNode b = nodes[seg.b];
        Vector2 delta = b.position - a.position;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return;

        float error = (distance - seg.restLength) / distance;
        Vector2 correction = delta * error;

        if (!a.locked && !b.locked)
        {
            a.position += correction * 0.5f;
            b.position -= correction * 0.5f;
        }
        else if (!a.locked)
        {
            a.position += correction;
        }
        else if (!b.locked)
        {
            b.position -= correction;
        }
    }

    void ClearWeb()
    {
        nodes.Clear();
        segments.Clear();
        stuck.Clear();
        riderActive = false;
        laying = false;
        tipNode = -1;
        tipSegment = -1;
        layOrigin = -1;
        createdAnchor = false;
        ClearRopeLoad();
        HidePreview();
        DrawSegments();
    }

    void EnsurePreviewLine()
    {
        if (previewLine != null)
            return;

        previewLine = CreateLine("WebPreview");
        previewLine.enabled = false;
        previewLine.startColor = new Color(1f, 1f, 1f, 0.45f);
        previewLine.endColor = previewLine.startColor;
    }

    void DrawPreview(Vector2 from, Vector2 to)
    {
        EnsurePreviewLine();
        previewLine.enabled = true;
        previewLine.positionCount = 2;
        previewLine.SetPosition(0, ToWorld(from));
        previewLine.SetPosition(1, ToWorld(to));
    }

    void HidePreview()
    {
        if (previewLine != null)
            previewLine.enabled = false;
    }

    void DrawSegments()
    {
        while (segmentLines.Count < segments.Count)
            segmentLines.Add(CreateLine("WebSegment"));

        for (int i = 0; i < segments.Count; i++)
        {
            WebSegment seg = segments[i];
            LineRenderer line = segmentLines[i];
            line.enabled = true;
            line.positionCount = 2;
            line.SetPosition(0, ToWorld(nodes[seg.a].position));
            line.SetPosition(1, ToWorld(nodes[seg.b].position));
        }

        for (int i = segments.Count; i < segmentLines.Count; i++)
            segmentLines[i].enabled = false;
    }

    LineRenderer CreateLine(string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.positionCount = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = LineMaterial();
        line.startColor = new Color(0.92f, 0.95f, 1f, 1f);
        line.endColor = line.startColor;
        return line;
    }

    Material LineMaterial()
    {
        if (lineMaterial != null)
            return lineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        lineMaterial = new Material(shader);
        lineMaterial.color = new Color(0.9f, 0.93f, 1f, 1f);
        lineMaterial.hideFlags = HideFlags.HideAndDontSave;
        return lineMaterial;
    }

    static Vector3 ToWorld(Vector2 point)
    {
        return new Vector3(point.x, point.y, 0f);
    }

    class WebNode
    {
        public Vector2 position;
        public Vector2 previous;
        public bool locked;
    }

    struct WebSegment
    {
        public int a;
        public int b;
        public float restLength;
    }

    struct StuckLoad
    {
        public int segment;
        public float t;
        public float weight;
    }
}

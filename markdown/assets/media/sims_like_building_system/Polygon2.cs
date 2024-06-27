using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public struct Polygon2
{
    public struct Polygon2RaycastHit
    {
        public int indexA;
        public int indexB;
        public Vector2 point;
        public float angle;
    }

    public List<Vector2> points;

    private Rect bounds;

    public Rect Bounds
    {
        get
        {
            if (bounds == default)
            {
                Vector2 min = points[0];
                Vector2 max = points[0];

                foreach (Vector2 point in points)
                {
                    if (point.x < min.x) { min.x = point.x; }
                    if (point.y < min.y) { min.y = point.y; }
                    if (point.x > max.x) { max.x = point.x; }
                    if (point.y > max.y) { max.y = point.y; }
                }

                bounds = new Rect(min, max - min);
            }

            return bounds;
        }
    }

    public bool Valid
    {
        get
        {
            return points.Count > 2;
        }
    }

    public Polygon2(List<Vector2> points)
    {
        this.points = points;
        bounds = new Rect();
    }

    public static bool IsPointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        float d1, d2, d3;
        bool has_neg, has_pos;

        d1 = EdgeSign(pt, v1, v2);
        d2 = EdgeSign(pt, v2, v3);
        d3 = EdgeSign(pt, v3, v1);

        has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(has_neg && has_pos);
    }

    public static bool GetLineIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 intersectionPoint)
    {
        float tmp = (b2.x - b1.x) * (a2.y - a1.y) - (b2.y - b1.y) * (a2.x - a1.x);

        if (tmp == 0)
        {
            intersectionPoint = Vector2.zero;
            return false;
        }

        float mu = ((a1.x - b1.x) * (a2.y - a1.y) - (a1.y - b1.y) * (a2.x - a1.x)) / tmp;

        intersectionPoint = new Vector2(b1.x + (b2.x - b1.x) * mu, b1.y + (b2.y - b1.y) * mu);;

        bool directionalCheck = Vector2.Dot(a2 - a1, intersectionPoint - a1) >= 0 && Vector2.Dot(b2 - b1, intersectionPoint - b1) >= 0;
        bool distanceCheck = Vector2.Distance(a1, a2) >= Vector2.Distance(a1, intersectionPoint) && Vector2.Distance(b1, b2) >= Vector2.Distance(b1, intersectionPoint);

        return directionalCheck && distanceCheck;
    }

    public static Vector2 GetLineIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        float tmp = (b2.x - b1.x) * (a2.y - a1.y) - (b2.y - b1.y) * (a2.x - a1.x);

        if (tmp == 0)
        {
            return Vector2.zero;
        }

        float mu = ((a1.x - b1.x) * (a2.y - a1.y) - (a1.y - b1.y) * (a2.x - a1.x)) / tmp;

        return new Vector2(b1.x + (b2.x - b1.x) * mu, b1.y + (b2.y - b1.y) * mu);
    }

    public static int GetClosest(IList<Vector2> inVectors, IList<Vector2> toVectors)
    {
        int toClosest;
        return GetClosest(inVectors, toVectors, out toClosest);
    }

    public static int GetClosest(IList<Vector2> inVectors, IList<Vector2> toVectors, out int toClosest)
    {
        toClosest = 0;
        if (inVectors.Count == 0 || toVectors.Count == 0)
        {
            return 0;
        }

        int closest = 0;
        float closestDistance = Vector2.Distance(inVectors[0], toVectors[0]);

        for (int inI = 0; inI < inVectors.Count; inI++)
        {
            for (int toI = 0; toI < toVectors.Count; toI++)
            {
                float distance = Vector2.Distance(inVectors[inI], toVectors[toI]);
                if (distance < closestDistance)
                {
                    closest = inI;
                    toClosest = toI;
                    closestDistance = distance;
                }
            }
        }

        return closest;
    }

    public static int GetFarthest(IList<Vector2> inVectors, IList<Vector2> toVectors)
    {
        int toFarthest = 0;
        return GetFarthest(inVectors, toVectors, out toFarthest);
    }

    public static int GetFarthest(IList<Vector2> inVectors, IList<Vector2> toVectors, out int toFarthest)
    {
        toFarthest = 0;
        if (inVectors.Count == 0 || toVectors.Count == 0)
        {
            return 0;
        }

        int farthest = 0;
        float farthestDistance = Vector2.Distance(inVectors[0], toVectors[0]);

        for (int inI = 0; inI < inVectors.Count; inI++)
        {
            for (int toI = 0; toI < toVectors.Count; toI++)
            {
                float distance = Vector2.Distance(inVectors[inI], toVectors[toI]);
                if (distance > farthestDistance)
                {
                    farthest = inI;
                    toFarthest = toI;
                    farthestDistance = distance;
                }
            }
        }

        return farthest;
    }

    public List<Polygon2RaycastHit> Segmentcast(Vector2 a, Vector2 b, Color debugColor = default)
    {
        Dictionary<Vector2, Polygon2RaycastHit> hits = new Dictionary<Vector2, Polygon2RaycastHit>();

        for (int pointI = 0; pointI < points.Count; pointI++)
        {
            int pointBI = (pointI + 1) % points.Count;

            Vector2 pointA = points[pointI];
            Vector2 pointB = points[pointBI];

            Vector2 intersectionPoint;

            if (GetLineIntersection(a, b, pointA, pointB, out intersectionPoint))
            {
                float currentAngle = Mathf.Abs(Vector2.Dot((pointB - pointA).normalized, (b - a).normalized));

                if (hits.ContainsKey(intersectionPoint) && currentAngle <= hits[intersectionPoint].angle) // Check for touching points like corners and hole entrances.
                {
                    continue;
                }

                hits[intersectionPoint] = new Polygon2RaycastHit()
                {
                    indexA = pointI,
                    indexB = pointBI,
                    point = intersectionPoint,
                    angle = currentAngle
                };
            }
        }

        List<Polygon2RaycastHit> hitsList = new List<Polygon2RaycastHit>(hits.Values);

        hitsList.Sort((aHit, bHit) => Vector2.Distance(a, aHit.point).CompareTo(Vector2.Distance(a, bHit.point)));

        if (debugColor != default)
        {
            foreach (Polygon2RaycastHit hit in hitsList)
            {
                TimedDebug.ArrowSequence(new Vector3(points[hit.indexA].x, 0.3f, points[hit.indexA].y), new Vector3(points[hit.indexB].x, 0.3f, points[hit.indexB].y), debugColor);
            }
        }

        return hitsList;

    }

    public bool PointIsInside(Vector2 point, out float distance)
    {
        List<Polygon2RaycastHit> hits = Segmentcast(point + ((point - Bounds.center).normalized * (Bounds.size.magnitude)), point);
        Vector2 pointOffset = point + ((point - Bounds.center).normalized * (Bounds.size.magnitude));

        distance = -1f;
        if (hits.Count != 0)
        {
            int closest = 0;
            distance = Vector2.Distance(hits[0].point, point);
            for (int hitI = 0; hitI < hits.Count; hitI++)
            {
                float currentDistance = Vector2.Distance(hits[hitI].point, point);
                if (currentDistance < distance)
                {
                    distance = currentDistance;
                    closest = hitI;
                }
            }
        }

        return hits.Count % 2 != 0;
    }

    public bool PointIsInside(Vector2 point)
    {
        return PointIsInside(point, out float distance);
    }

    private static List<Polygon2> ShatterConnections(Dictionary<Vector2, HashSet<Vector2>> connections)
    {
        List<Polygon2> polygons = new List<Polygon2>();

        Polygon2 currentPolygon = new Polygon2();
        currentPolygon.points = new List<Vector2>();

        Dictionary<Vector2, HashSet<Vector2>> iteratedConnections = new Dictionary<Vector2, HashSet<Vector2>>();

        List<Vector2> possibleConnectionA = Enumerable.ToList(connections.Keys);

        bool foundAnotherLead = true;
        Vector2 leadA = possibleConnectionA[0];

        List<Vector2> possibleConnectionB = Enumerable.ToList(connections[leadA]);
        Vector2 leadB = possibleConnectionB[0];

        while (foundAnotherLead)
        {
            if (iteratedConnections.TryAdd(leadA, new HashSet<Vector2>()) || !iteratedConnections[leadA].Contains(leadB))
            {
                iteratedConnections[leadA].Add(leadB);

                // First, find the sharpest angle.

                Vector2 greatestAngleTarget = leadB;
                float greatestAngle = -181f;

                foreach (Vector2 nextSegment in connections[leadB])
                {
                    float currentAngle = Vector2.SignedAngle(leadB - leadA, nextSegment - leadB);

                    if (currentAngle > greatestAngle)
                    {
                        greatestAngle = currentAngle;
                        greatestAngleTarget = nextSegment;
                    }
                }

                // Then, iterate to that angle.

                currentPolygon.points.Add(leadA);

                leadA = leadB;
                leadB = greatestAngleTarget;
            }
            else // Otherwise, look through all possible direction combinations to find any that haven't been added yet.
            {
                polygons.Add(currentPolygon);
                currentPolygon = new Polygon2() { points = new List<Vector2>() };

                foundAnotherLead = false;

                foreach (Vector2 conA in connections.Keys)
                {
                    foreach (Vector2 conB in connections[conA])
                    {
                        if (!iteratedConnections.ContainsKey(conA) || !iteratedConnections[conA].Contains(conB))
                        {
                            leadA = conA;
                            leadB = conB;

                            foundAnotherLead = true;
                            break;
                        }
                    }
                    if (foundAnotherLead) { break; }
                }
            }
        }

        return polygons;
    }

    public static Polygon2 CutHole(ref Polygon2 what, ref Polygon2 intoWhat)
    {
        int whatClosestI;
        int intoWhatClosestI;

        whatClosestI = GetClosest(what.points, intoWhat.points, out intoWhatClosestI);

        List<Vector2> inserts = new List<Vector2>();

        for (int whatI = 0; whatI < what.points.Count; whatI++)
        {
            int reverseI = (-whatI + whatClosestI + what.points.Count) % what.points.Count;
            inserts.Add(what.points[reverseI]);
        }

        inserts.Add(what.points[whatClosestI]);
        inserts.Add(intoWhat.points[intoWhatClosestI]);

        Polygon2 polygon = new Polygon2(intoWhat.points);
        polygon.points.InsertRange(intoWhatClosestI + 1, inserts);

        return polygon;
    }

    public static List<Polygon2> OverlapBounds(ref Polygon2 above, ref Polygon2 below)
    {
        int pointAI = 0;
        int pointBI = 0;

        int belowOutside = 0;
        int aboveOutside = 0;
        bool containsIntersections = false;

        for (int pointI = 0; pointI < above.points.Count; pointI++)
        {
            if (!below.PointIsInside(above.points[pointI]))
            {
                aboveOutside++;
            }
        }

        Dictionary<Vector2, List<int>> leads = new Dictionary<Vector2, List<int>>();

        for (int pointI = 0; pointI < below.points.Count; pointI++)
        {
            int modOffset = 0;
            int pointTempBI = (pointI + 1) % below.points.Count;

            bool insideAbove = above.PointIsInside(below.points[pointI], out float distance);

            if (!insideAbove)
            {
                belowOutside++;

                pointAI = pointI;
                pointBI = pointTempBI;

                if (!leads.ContainsKey(below.points[pointI]))
                {
                    leads.Add(below.points[pointI], new List<int>());
                }
                leads[below.points[pointI]].Add(pointI);

                Debug.DrawLine(new Vector3(below.points[pointI].x, 0.3f, below.points[pointI].y), new Vector3(below.points[pointI].x, 1.3f, below.points[pointI].y) + new Vector3(Random.Range(-0.1f, 0.1f), 0f, 0f), Color.red, 20000f);

                modOffset = 1;

                //break;
            }

            List<Polygon2RaycastHit> hits = above.Segmentcast(below.points[pointI], below.points[pointTempBI]);
            for (int hitI = 0; hitI < hits.Count; hitI++)
            {
                if (hits[hitI].point == below.points[pointAI] || hits[hitI].point == below.points[pointBI]) { continue; }

                containsIntersections = true;
                if ((hitI + modOffset) % 2 == 0)
                {
                    Debug.DrawLine(new Vector3(hits[hitI].point.x, 0.3f, hits[hitI].point.y), new Vector3(hits[hitI].point.x, 1.3f, hits[hitI].point.y) + new Vector3(Random.Range(-0.1f, 0.1f), 0f, 0f), Color.red, 20000f);
                    if (!leads.ContainsKey(hits[hitI].point))
                    {
                        leads.Add(hits[hitI].point, new List<int>());
                    }
                    leads[hits[hitI].point].Add(pointI);
                }
            }
        }

        if (belowOutside == 0)
        {
            return new List<Polygon2>() { new Polygon2() { points = new List<Vector2>() } };
        }

        if (belowOutside == below.points.Count && !containsIntersections)
        {
            if (aboveOutside == 0)
            {
                Debug.Log("All above points are inside. Cutting hole in below.");
                return new List<Polygon2>() { CutHole(ref above, ref below) };
            }
            else if (aboveOutside == above.points.Count)
            {
                Debug.Log("Shapes are separated. No operations are needed.");
                return null;
            }
        }

        Debug.Log("Complex shape detected, beggining boolean process.");

        Polygon2 currentPolygon = below;
        Polygon2 otherPolygon = above;
        int step = 1;
        Dictionary<Vector2, HashSet<Vector2>> iteratedSegments = new Dictionary<Vector2, HashSet<Vector2>>();
        Dictionary<Vector2, HashSet<Vector2>> shapeSegments = new Dictionary<Vector2, HashSet<Vector2>>();

        Vector2 pointA = currentPolygon.points[pointAI];
        Vector2 pointB = currentPolygon.points[pointBI];

        while (true)
        {
            if (iteratedSegments.ContainsKey(pointA) && iteratedSegments[pointA].Contains(pointB))
            {
                bool found = false;
                foreach (Vector2 point in leads.Keys)
                {
                    if (leads[point].Count == 0) { continue; }

                    if (!iteratedSegments.ContainsKey(point) || !iteratedSegments[point].Contains(below.points[(leads[point][0] + 1) % below.points.Count]))
                    {
                        found = true;

                        pointAI = leads[point][0];
                        pointBI = (leads[point][0] + 1) % below.points.Count;

                        pointA = point;
                        pointB = below.points[pointBI];

                        currentPolygon = below;
                        otherPolygon = above;

                        step = 1;

                        leads[point].RemoveAt(0);

                        break;
                    }
                }

                if (found) { continue; }

                break;
            }

            if (!iteratedSegments.ContainsKey(pointA))
            {
                iteratedSegments[pointA] = new HashSet<Vector2>();
            }
            iteratedSegments[pointA].Add(pointB);

            List<Polygon2RaycastHit> hits = otherPolygon.Segmentcast(pointA, pointB);


            bool foundHit = false;
            for (int hitI = 0; hitI < hits.Count; hitI++)
            {
                Polygon2RaycastHit hit = hits[hitI];

                if (hit.point != pointA)
                {
                    Polygon2 polySwap = currentPolygon;
                    currentPolygon = otherPolygon;
                    otherPolygon = polySwap;

                    if (step == 1)
                    {

                        pointAI = hit.indexB;
                        pointBI = hit.indexA;
                    }
                    else
                    {
                        pointAI = hit.indexA;
                        pointBI = hit.indexB;
                    }

                    TimedDebug.ArrowSequence(new Vector3(pointA.x, 0.3f, pointA.y), new Vector3(hit.point.x, 0.3f, hit.point.y), step == 1 ? Color.green : Color.red);
                    if (shapeSegments.TryAdd(pointA, new HashSet<Vector2>()) || !shapeSegments[pointA].Contains(hit.point)) { shapeSegments[pointA].Add(hit.point); }

                    pointA = hit.point;
                    
                    pointB = currentPolygon.points[pointBI];

                    step *= -1;

                    foundHit = true;

                    break;
                }
            }

            if (foundHit) { continue; }

            TimedDebug.ArrowSequence(new Vector3(pointA.x, 0.3f, pointA.y), new Vector3(pointB.x, 0.3f, pointB.y), step == 1 ? Color.green : Color.red);
            if (shapeSegments.TryAdd(pointA, new HashSet<Vector2>()) || !shapeSegments[pointA].Contains(pointB)) { shapeSegments[pointA].Add(pointB); }

            pointAI = (pointAI + step + currentPolygon.points.Count) % currentPolygon.points.Count;
            pointBI = (pointBI + step + currentPolygon.points.Count) % currentPolygon.points.Count;

            pointA = currentPolygon.points[pointAI];
            pointB = currentPolygon.points[pointBI];
        }

        return ShatterConnections(shapeSegments);
    }

    public static float EdgeSign(Vector2 pt, Vector2 v1, Vector2 v2)
    {
        return (pt.x - v2.x) * (v1.y - v2.y) - (v1.x - v2.x) * (pt.y - v2.y);
    }
}

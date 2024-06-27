using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Triangulate
{
    public Polygon2 result;

    public Triangulate(Polygon2 polygon)
    {
        result = new Polygon2(new List<Vector2>());
        List<Vector2> targetVertices = new List<Vector2>(polygon.points);

        int iterations = 0;

        while (targetVertices.Count >= 3)
        {
            if (iterations >= 100)
            {
                Debug.Log("Overloaded");
                break;
            }

            iterations++;

            for (int vertI = 0; vertI < targetVertices.Count; vertI++)
            {
                int aI = (vertI + (targetVertices.Count - 1)) % targetVertices.Count;
                int bI = (vertI);
                int cI = (vertI + 1) % targetVertices.Count;

                Vector2 a = targetVertices[aI];
                Vector2 b = targetVertices[bI];
                Vector2 c = targetVertices[cI];
                
                float angle = Vector2.SignedAngle(a - b, c - b);

                bool angleCase = angle >= 0f;
                bool collisionCase = true;
                for (int colVertI = (vertI + 2) % targetVertices.Count; colVertI != aI; colVertI = (colVertI + 1) % targetVertices.Count)
                {
                    if (Polygon2.IsPointInTriangle(targetVertices[colVertI], a, b, c))
                    {
                        if ((targetVertices[colVertI] == a || targetVertices[colVertI] == b || targetVertices[colVertI] == c))
                        {

                        }
                        else
                        {
                            collisionCase = false;
                            break;
                        }
                    }
                }

                if (angleCase && collisionCase)
                {
                    result.points.Add(a);
                    result.points.Add(b);
                    result.points.Add(c);

                    targetVertices.RemoveAt(bI);

                    break;
                }
            }
        }
    }
}

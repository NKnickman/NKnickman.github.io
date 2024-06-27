using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using XNodeCore;
using Unity.Burst;
using Unity.Collections;

namespace ProceduralModelling.ModelNodes
{
    [CreateNodeMenu("Mesh/Operators/Extrude")]
    public class ExtrudeMeshOperatorNode : MeshOperatorNode<ExtrudeMeshOperatorJob>
    {
        public InMultiField<Vector3> offset = new InMultiField<Vector3>();
        public InSingleField<bool> flipFace = new InSingleField<bool>();

        protected override void ConfigureJob(ref ExtrudeMeshOperatorJob job)
        {
            job.offset = offset.ToNativeArray(job.Data.vertices.Length, Allocator.Persistent);
            job.flipFace = flipFace.Value;
        }

        protected override void DisposeJob(ref ExtrudeMeshOperatorJob job)
        {
            job.offset.Dispose();
        }
    }

    [BurstCompile]
    public struct ExtrudeMeshOperatorJob : IJob, IMeshOperatorJob
    {
        private MeshOperatorData data;
        public MeshOperatorData Data { get => data; set => data = value; }

        public NativeArray<Vector3> offset;
        public bool flipFace;

        public void Execute()
        {
            NativeList<int> edges = data.GetEdges();

            int originalVertexCount = data.vertices.Length;
            int maxVertexCount = originalVertexCount * 2;

            data.vertices.SetCapacity(maxVertexCount);
            data.normals.SetCapacity(maxVertexCount);
            data.uv.SetCapacity(maxVertexCount);
            
            for (int vertI = 0; vertI < originalVertexCount; vertI++)
            {
                data.vertices.AddNoResize(data.vertices[vertI] + offset[vertI]);
                data.normals.AddNoResize(data.normals[vertI]);
                data.uv.AddNoResize(data.uv[vertI]);
            }

            int originalTriangleCount = data.triangles.Length;
            int maxTriCount = (originalTriangleCount * 2) + (edges.Length * 6);

            data.triangles.SetCapacity(maxTriCount);

            for (int triI = 0; triI < originalTriangleCount; triI++)
            {
                data.triangles.AddNoResize(data.triangles[triI] + originalVertexCount);
            }

            if (flipFace)
            {
                for (int vertI = 0; vertI < originalVertexCount; vertI++)
                {
                    data.normals[vertI] = data.normals[vertI] * -1;
                }
            }

            foreach (int edgeI in edges)
            {
                int originA = edgeI;
                int originB = MeshUtilities.NextTriangleIndex(edgeI);
                int addedA = data.triangles[originA + originalTriangleCount];
                int addedB = data.triangles[originB + originalTriangleCount];
                originA = data.triangles[originA];
                originB = data.triangles[originB];

                Vector3 norm = Vector3.Cross(data.vertices[originB] - data.vertices[originA], data.vertices[addedA] - data.vertices[originA]);

                data.triangles.Add(originA);
                data.triangles.Add(originB);
                data.triangles.Add(addedA);

                data.normals[originA] += norm;
                data.normals[originB] += norm;
                data.normals[addedA] += norm;

                norm = Vector3.Cross(data.vertices[originB] - data.vertices[originA], data.vertices[addedB] - data.vertices[addedA]);

                data.triangles.Add(addedA);
                data.triangles.Add(originB);
                data.triangles.Add(addedB);

                data.normals[addedA] += norm;
                data.normals[originB] += norm;
                data.normals[addedB] += norm;
            }

            foreach (int edgeI in edges)
            {
                data.normals[data.triangles[edgeI]] = data.normals[data.triangles[edgeI]].normalized;
                data.normals[data.triangles[edgeI + originalTriangleCount]] = data.normals[data.triangles[edgeI + originalTriangleCount]].normalized;
            }

            if (flipFace)
            {
                for (int triI = 0; triI < originalTriangleCount; triI += 3)
                {
                    int a = data.triangles[triI];
                    int b = data.triangles[triI + 1];
                    int c = data.triangles[triI + 2];

                    data.triangles[triI] = c;
                    data.triangles[triI + 1] = b;
                    data.triangles[triI + 2] = a;
                }
            }

            edges.Dispose();
        }
    }
}
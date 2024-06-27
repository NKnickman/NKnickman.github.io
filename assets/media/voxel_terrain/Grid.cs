using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.Mesh;
using static Grids.MarchingCubes;
using UnityEngine.Jobs;
using System.Linq;

namespace Grids
{
    public class Grid : MonoBehaviour
    {
        private struct ChunkComponents
        {
            public MeshFilter filter;
            public MeshRenderer renderer;
            public MeshCollider collider;
        }

        private class TerrainGenerationData
        {
            //public TestOperatorJob operatorJob;
            public JobHandle operatorJobHandle;
            public VoxelizeJob<OctifyLOD> voxelizeJob;
            public JobHandle voxelizeJobHandle;
        }

        // Voxel Terrain
        /// <summary> The size of terrain cells represented as a floating-point value. </summary>
        public const float tScale = 1.05f;
        /// <summary> The number of terrain cells that can fit within a single terrain chunk. </summary>
        public const int tCapacity = 16;
        public const int halfTCapacity = tCapacity / 2;
        public const int tCapacitySquared = tCapacity * tCapacity;
        public const int tCapacityCubed = tCapacity * tCapacity * tCapacity;
        public const float tChunkScale = tScale * tCapacity;

        //public const int regionCapacity = 2048;
        public const int regionCapacity = 256;
        //public const int regionCapacity = 16;

        public const int regionCellCapacity = tCapacity * regionCapacity;

        public const int LODs = 17;

        /// <summary> The number of terrain chunks represented around the viewer. </summary>
        public const int renderDistance = 8 + (56 * (LODs - 2));
        /// <summary> The viewer position converted into terrain chunk space. </summary>
        private static int3 renderPosition;
        /// <summary> The render position on the last fixed update. </summary>
        private static int3 lastRenderPosition;

        private ChunkComponents[] chunkComponents;
        private Mesh[] chunkMeshes;
        private TerrainGenerationData currentTGeneration;

        public Transform viewer;
        public Material material;

        private int3 offset;
        private NativeGrid<float, OctifyLOD> terrainWeightGrid;

        private void OnDestroy()
        {
            terrainWeightGrid.Dispose();
            //terrainMaterialGrid.Dispose();
            currentTGeneration.voxelizeJob.meshes.Dispose();
        }

        public void AddMeshCell<T>(T cell, Vector3 position) where T : struct, IMeshCell, IJob
        {
            MeshDataArray meshArray = AllocateWritableMeshData(1);
            MeshData meshData = meshArray[0];
            cell.Mesh = meshData;
            cell.Position = position;
            cell.Schedule().Complete();

            Mesh mesh = new Mesh();
            ApplyAndDisposeWritableMeshData(meshArray, mesh);
            mesh.RecalculateBounds();

            GameObject gO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gO.GetComponent<MeshFilter>().sharedMesh = mesh;
            gO.AddComponent<MeshCollider>();
        }

        void Start()
        {
            chunkComponents = new ChunkComponents[renderDistance];
            chunkMeshes = new Mesh[renderDistance];

            material.SetVector("_ChunkSize", new Vector3(tCapacity, tCapacity, tCapacity));

            for (int i = 0; i < renderDistance; i++)
            {
                GameObject gO = new GameObject();

                chunkComponents[i] = new ChunkComponents()
                {
                    filter = gO.AddComponent<MeshFilter>(),
                    renderer = gO.AddComponent<MeshRenderer>(),
                    collider = gO.AddComponent<MeshCollider>(),
                };

                chunkComponents[i].renderer.sharedMaterial = material;
                chunkMeshes[i] = new Mesh();
                chunkComponents[i].filter.sharedMesh = chunkMeshes[i];
            }

            terrainWeightGrid = new NativeGrid<float, OctifyLOD>(LODs);

            ForceRegenerate();
            InvokeRepeating("TryRegenerate", 0f, 1f);
        }

        private void ForceRegenerate()
        {
            renderPosition = (int3)math.floor(((float3)viewer.position + (float3)halfTCapacity) / tCapacity);
            Regenerate(true);
            TryCompleteJobs();
        }

        private void TryRegenerate()
        {
            renderPosition = (int3)math.floor(((float3)viewer.position + (float3)halfTCapacity) / tCapacity);
            if (!lastRenderPosition.Equals(renderPosition) && TryCompleteJobs())
            {
                Regenerate();
            }
        }

        private bool TryCompleteJobs()
        {
            if (currentTGeneration != null)
            {
                if ((!currentTGeneration.operatorJobHandle.IsCompleted) || (!currentTGeneration.voxelizeJobHandle.IsCompleted)) return false;

                currentTGeneration.operatorJobHandle.Complete();
                currentTGeneration.voxelizeJobHandle.Complete();

                ApplyAndDisposeWritableMeshData(currentTGeneration.voxelizeJob.meshes, chunkMeshes);

                for (int i = 0; i < chunkMeshes.Length; i++)
                {
                    chunkMeshes[i].RecalculateBounds();
                    if (chunkMeshes[i].vertexCount > 0 && i < 64)
                    {
                        chunkComponents[i].collider.sharedMesh = chunkMeshes[i];
                    }
                }

                currentTGeneration = null;

                return true;
            }
            return true;
        }

        private void Regenerate(bool await = false)
        {
            offset = renderPosition * tCapacity;
            lastRenderPosition = renderPosition;

            StaticGridBaseGenerator baseGen = new StaticGridBaseGenerator { offset = offset };

            BiomeGeneratorOperator<Desert> dJob = new BiomeGeneratorOperator<Desert>(offset);
            JobHandle jobHandle = terrainWeightGrid.Operate(dJob, 64, terrainWeightGrid.Operate(baseGen, 64));

            BiomeGeneratorOperator<Tundra> tJob = new BiomeGeneratorOperator<Tundra>(offset);
            jobHandle = terrainWeightGrid.Operate(tJob, 64, jobHandle);

            VoxelizeJob<OctifyLOD> voxelJob = new VoxelizeJob<OctifyLOD>()
            {
                LODProcessor = terrainWeightGrid.LODProcessor,
                Values = terrainWeightGrid.values,
                meshes = AllocateWritableMeshData(renderDistance),
                offset = offset
            };
            JobHandle voxelJobHandle = voxelJob.Schedule(voxelJob.meshes.Length, 1, jobHandle);

            if (await) voxelJobHandle.Complete();

            currentTGeneration = new TerrainGenerationData()
            {
                operatorJobHandle = jobHandle,
                voxelizeJob = voxelJob,
                voxelizeJobHandle = voxelJobHandle
            };
        }

        private struct Tundra : IBiomeGenerator
        {
            private static readonly DynamicNoise noise = new DynamicNoise()
            {
                Seed = 1,
                Frequency = 12.727f,
                SamplerType = DynamicNoise.Sampler.Perlin,
                FractalType = DynamicNoise.Fractal.FBM,
                FractalOctaves = 1,
                FractalGain = 0.7126499f,
                FractalLacunarity = 2f,
                FractalWeightedStrength = 0.3472509f,
                Period = (regionCellCapacity / 20)
            };

            public Climate MinimumClimateRequirement => new Climate() { altitude = 0f, precipitation = 0f, temperature = 0f };

            public Climate MaximumClimateRequirement => new Climate() { altitude = 1f, precipitation = 1f, temperature = .5f };

            public float Sample(int3 cellPos)
            {
                return (noise.DynamicSample(cellPos.x, cellPos.z) * 1000f);
            }
        }

        private struct Desert : IBiomeGenerator
        {
            private static readonly DynamicNoise noise = new DynamicNoise()
            {
                Seed = 0,
                Frequency = 12.727f,
                SamplerType = DynamicNoise.Sampler.Perlin,
                FractalType = DynamicNoise.Fractal.FBM,
                FractalOctaves = 1,
                FractalGain = 0.7126499f,
                FractalLacunarity = 2f,
                FractalWeightedStrength = 0.3472509f,
                Period = (regionCellCapacity / 3)
            };

            public Climate MinimumClimateRequirement => new Climate() { altitude = .5f, precipitation = .5f, temperature = .5f };

            public Climate MaximumClimateRequirement => new Climate() { altitude = 1f, precipitation = 1f, temperature = 1f };

            public float Sample(int3 cellPos)
            {
                return noise.DynamicSample(cellPos.x, cellPos.y, cellPos.z) * 10000;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct StaticGridBaseGenerator : IJobParallelFor, NativeGrid<float, OctifyLOD>.IOperatorJobParallelFor
        {
            private NativeArray<float> values;
            public NativeArray<float> Values { get => values; set => values = value; }
            public OctifyLOD LODProcessor { get; set; }
            public int3 offset;

            public void Execute(int index)
            {
                int3 cellPos = LODProcessor.IndexToCellPosition(index) + offset;
                values[index] = -cellPos.y;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct VoxelizeJob<T> : IJobParallelFor where T : struct, ILODProcessor
        {
            [ReadOnly]
            private NativeArray<float> values;
            public NativeArray<float> Values { get => values; set => values = value; }
            public T LODProcessor { get; set; }
            public int3 offset;

            public MeshDataArray meshes;

            private float3 CreateVertex(float3 cornerA, float3 cornerB, float weightA, float weightB/*, out float3 norm*/)
            {
                float t = -weightA / (weightB - weightA);
                return cornerA + t * (cornerB - cornerA) + offset;
            }

            public void Execute(int index)
            {
                MeshData mesh = meshes[index];
                int vertexCount = 0;
                NativeArray<int> configurations = new NativeArray<int>(tCapacityCubed, Allocator.Temp);
                NativeArray<float> weights = new NativeArray<float>(tCapacityCubed * 8, Allocator.Temp);
                NativeArray<int3> cellPositions = new NativeArray<int3>(tCapacityCubed, Allocator.Temp);

                int indexOffset = index * tCapacityCubed;
                int3 chunkStart = LODProcessor.IndexToCellPosition(indexOffset);
                int sizeMultiplier = LODProcessor.GetSizeMultiplier(LODProcessor.CellPositionToIndex(chunkStart));
                int scaledChunkSize = sizeMultiplier * tCapacity;
                int3 chunkEnd = chunkStart + scaledChunkSize;
                int chunkOffset = tCapacity * sizeMultiplier;
                int positiveSizeMultiplier = LODProcessor.GetSizeMultiplier(LODProcessor.CellPositionToIndex(chunkStart + chunkOffset));
                int negativeSizeMultiplier = LODProcessor.GetSizeMultiplier(LODProcessor.CellPositionToIndex(chunkStart - 1));
                int posChunkAxis = math.max(math.max(chunkEnd.x, chunkEnd.y), chunkEnd.z);
                int negChunkAxis = math.min(math.min(chunkStart.x, chunkStart.y), chunkStart.z);

                // Precalculate Vertex Count;
                for (int i = 0; i < tCapacityCubed; i++)
                {
                    int config = 0;
                    int3 cellPos = LODProcessor.IndexToCellPosition(i + indexOffset);
                    cellPositions[i] = cellPos;

                    for (int cI = 0; cI < 8; cI++)
                    {
                        int3 cornerPos = cellPos + (corners[cI] * sizeMultiplier);
                        int3 posVector = new int3(cornerPos == posChunkAxis);
                        int posFactor = math.max(posVector.x, math.max(posVector.y, posVector.z));
                        int3 negVector = new int3(cornerPos == negChunkAxis);
                        int negFactor = math.max(negVector.x, math.max(negVector.y, negVector.z));
                        int cornerSizeMultiplier = math.max(math.max(posFactor * positiveSizeMultiplier, negFactor * negativeSizeMultiplier), sizeMultiplier);

                        int3 fromDimensions = (int3)math.floor((float3)cornerPos / cornerSizeMultiplier) * cornerSizeMultiplier;
                        int3 toDimensions = (int3)math.ceil((float3)cornerPos / cornerSizeMultiplier) * cornerSizeMultiplier;

                        float weight = 0f;
                        int weightContributions = 0;
                        for (int z = fromDimensions.z; z <= toDimensions.z; z += cornerSizeMultiplier)
                        {
                            for (int y = fromDimensions.y; y <= toDimensions.y; y += cornerSizeMultiplier)
                            {
                                for (int x = fromDimensions.x; x <= toDimensions.x; x += cornerSizeMultiplier)
                                {
                                    weight += values[LODProcessor.CellPositionToIndex(new int3(x, y, z))];
                                    weightContributions++;
                                }
                            }
                        }

                        weight /= weightContributions;
                        weights[(i * 8) + cI] = weight;
                        config |= ((int)math.saturate(math.ceil(weight)) << cI);
                    }
                    config *= 16;

                    configurations[i] = config;
                    vertexCount += triangulation[config + 15];
                }

                // Initialize Mesh Data
                NativeArray<UnityEngine.Rendering.VertexAttributeDescriptor> descriptors = new NativeArray<UnityEngine.Rendering.VertexAttributeDescriptor>(2, Allocator.Temp);
                descriptors[0] = new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position);
                descriptors[1] = new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Normal, stream: 1);
                mesh.SetVertexBufferParams(vertexCount, descriptors);
                mesh.SetIndexBufferParams(vertexCount, UnityEngine.Rendering.IndexFormat.UInt16);
                NativeArray<Vector3> vertices = mesh.GetVertexData<Vector3>();
                NativeArray<Vector3> normals = mesh.GetVertexData<Vector3>(1);
                NativeArray<ushort> triangles = mesh.GetIndexData<ushort>();

                // Construct The Actual Mesh
                int currentVertIndex = 0;
                for (int cI = 0; cI < configurations.Length; cI++)
                {
                    int config = configurations[cI];
                    int configurationVertCount = triangulation[config + 15];
                    int3 pos = cellPositions[cI];
                    int weightStartIndex = cI * 8;

                    int vI1 = currentVertIndex;
                    for (int vI = config; vI < config + configurationVertCount; vI += 3)
                    {
                        int vI2 = vI1 + 1;
                        int vI3 = vI1 + 2;

                        int edgeIndexA = triangulation[vI];
                        int a0 = cornerIndexAFromEdge[edgeIndexA];
                        int a1 = cornerIndexBFromEdge[edgeIndexA];

                        int edgeIndexB = triangulation[vI + 1];
                        int b0 = cornerIndexAFromEdge[edgeIndexB];
                        int b1 = cornerIndexBFromEdge[edgeIndexB];

                        int edgeIndexC = triangulation[vI + 2];
                        int c0 = cornerIndexAFromEdge[edgeIndexC];
                        int c1 = cornerIndexBFromEdge[edgeIndexC];

                        int3 cA0 = (corners[a0] * sizeMultiplier) + pos;
                        int3 cA1 = (corners[a1] * sizeMultiplier) + pos;
                        int3 cB0 = (corners[b0] * sizeMultiplier) + pos;
                        int3 cB1 = (corners[b1] * sizeMultiplier) + pos;
                        int3 cC0 = (corners[c0] * sizeMultiplier) + pos;
                        int3 cC1 = (corners[c1] * sizeMultiplier) + pos;

                        float cIA0 = weights[a0 + weightStartIndex];
                        float cIA1 = weights[a1 + weightStartIndex];
                        float cIB0 = weights[b0 + weightStartIndex];
                        float cIB1 = weights[b1 + weightStartIndex];
                        float cIC0 = weights[c0 + weightStartIndex];
                        float cIC1 = weights[c1 + weightStartIndex];

                        vertices[vI1] = CreateVertex(cA0, cA1, cIA0, cIA1);
                        vertices[vI2] = CreateVertex(cB0, cB1, cIB0, cIB1);
                        vertices[vI3] = CreateVertex(cC0, cC1, cIC0, cIC1);

                        float3 normal = math.normalize(math.cross(vertices[vI2] - vertices[vI1], vertices[vI3] - vertices[vI1]));
                        normals[vI1] = normal;
                        normals[vI2] = normal;
                        normals[vI3] = normal;

                        triangles[vI1] = (ushort)vI1;
                        triangles[vI2] = (ushort)vI2;
                        triangles[vI3] = (ushort)vI3;

                        vI1 += 3;
                    }

                    currentVertIndex += configurationVertCount;
                }

                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new UnityEngine.Rendering.SubMeshDescriptor(0, vertexCount));

                configurations.Dispose();
                weights.Dispose();
                cellPositions.Dispose();
            }
        }
    }
}
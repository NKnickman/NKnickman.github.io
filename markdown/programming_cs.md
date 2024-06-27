---
title: C#
---

# C\#

## Marching Cubes Voxel Terrain Prototype Using DOTS
=== "LOD 16 Test"
    ![type:video](assets/media/voxel_terrain/slow_test.mp4)

    !!! quote "About"
    
        A prototype for performant voxel terrain generation using Unity's burst compiler and job systems. This video showcases a slower test where around 16 levels of detail are generated around the viewer. Each level of detail beyond the second is about twice the size as the last, this exponential growth allows for very far render distance.

=== "LOD 13 Test"
    ![type:video](assets/media/voxel_terrain/fast_test.mp4)

    !!! quote "About"
    
        Here's a faster test generating around 13 levels of detail. It's important to note that the generation makes heavy use of multi-threading. Voxel weight generation is executed per voxel as an IJobParallelFor, whilst the meshing process is done per chunk in an IJobParallelFor.

=== "Code Samples"
    === "Grid"

        ``` cs title="grid.cs" linenums="1"
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
        ```

    === "Octify LOD"
    
        ``` cs title="octify_lod.cs" linenums="1"
        using Unity.Mathematics;
        using static Grids.Grid;

        namespace Grids
        {
            public struct OctifyLOD : ILODProcessor
            {
                public int3 IndexToCellPosition(int index)
                {
                    int chunkIndex = index / tCapacityCubed;
                    int3 chunkPosition;
                    int multiplier = 1;
                    if (chunkIndex < 8) chunkPosition = innerChunks[chunkIndex];
                    else
                    {
                        int outerIUnwrapped = chunkIndex - 8;
                        int layer = (outerIUnwrapped / outerChunks.Length);
                        multiplier = (int)math.pow(2, layer);
                        chunkPosition = outerChunks[outerIUnwrapped % outerChunks.Length] * multiplier;
                    }

                    int3 chunkStart = chunkPosition * tCapacity;
                    int remainder = index % tCapacityCubed;
                    int3 localCellPosition = new int3(remainder % tCapacity, (remainder % tCapacitySquared) / tCapacity, remainder / tCapacitySquared) * multiplier;

                    return chunkStart + localCellPosition;
                }

                public int CellPositionToIndex(int3 cellPosition)
                {
                    int3 signCorrective = math.min((int3)math.sign(cellPosition), 0);
                    int3 absoluteCellPosition = math.abs(cellPosition) + signCorrective;
                    int multiplier = math.max(math.max(absoluteCellPosition.x, absoluteCellPosition.y), absoluteCellPosition.z);
                    int chunkIndex;
                    if (multiplier < tCapacity) // Inner Chunk
                    {
                        int3 chunkPos = (int3)math.floor((float3)cellPosition / tCapacity);
                        int3 innerChunkPos = chunkPos + 1;
                        chunkPos *= tCapacity;
                        int innerChunkIndex = innerChunkPos.x + (innerChunkPos.y * 2) + (innerChunkPos.z * 4);
                        chunkIndex = innerChunkIndex * tCapacityCubed;
                        int3 localChunkPos = cellPosition - chunkPos;
                        int localIndex = localChunkPos.x + (localChunkPos.y * tCapacity) + (localChunkPos.z * tCapacitySquared);
                        int globalIndex = chunkIndex + localIndex;
                        return globalIndex;
                    }
                    else // Outer Chunk
                    {
                        int layer = (math.max((int)math.log2(multiplier / tCapacity) + 1, 1));
                        int exponent = (int)math.pow(2, layer);
                        multiplier = exponent * tCapacity;
                        int halfExponent = exponent / 2;
                        int halfMultiplier = multiplier / 2;
                        int3 outerChunkPos = (int3)math.floor((float3)cellPosition / halfMultiplier);
                        int3 outerChunkPosOffset = outerChunkPos + 2;
                        int outerChunkIndex = outerChunkPosOffset.x + (outerChunkPosOffset.y * outerChunkCount) + (outerChunkPosOffset.z * outerChunkCountSquared);
                        chunkIndex = 8 + ((layer - 1) * outerChunks.Length) + outerChunkIndices[outerChunkIndex];
                        outerChunkPos *= halfMultiplier;
                        int3 localChunkPos = cellPosition - outerChunkPos;
                        localChunkPos /= halfExponent;
                        int localIndex = localChunkPos.x + (localChunkPos.y * tCapacity) + (localChunkPos.z * tCapacitySquared);
                        int globalIndex = (chunkIndex * tCapacityCubed) + localIndex;
                        return globalIndex;
                    }
                }

                public int GetCellCount(int LODs)
                {
                    int innerContribution = innerChunks.Length * tCapacityCubed;
                    int outerContribution = (LODs - 1) * outerChunks.Length * tCapacityCubed;
                    return innerContribution + outerContribution;
                }

                public int GetSizeMultiplier(int index)
                {
                    int chunkIndex = index / tCapacityCubed;
                    int multiplier = 1;
                    if (chunkIndex >= 8)
                    {
                        int outerIUnwrapped = chunkIndex - 8;
                        int layer = (outerIUnwrapped / outerChunks.Length);
                        multiplier = (int)math.pow(2, layer);
                    }

                    return multiplier;
                }

                private static readonly int3[] innerChunks = new int3[]
        {
                        new int3(-1, -1, -1),
                        new int3(0, -1, -1),
                        new int3(-1, 0, -1),
                        new int3(0, 0, -1),
                        new int3(-1, -1, 0),
                        new int3(0, -1, 0),
                        new int3(-1, 0, 0),
                        new int3(0, 0, 0)
        };

                private const int outerChunkCount = 4;
                private const int outerChunkCountSquared = outerChunkCount * outerChunkCount;

                private static readonly int[] outerChunkIndices = new int[]
                {
                        0,
                        1,
                        2,
                        3,
                        4,
                        5,
                        6,
                        7,
                        8,
                        9,
                        10,
                        11,
                        12,
                        13,
                        14,
                        15,
                        16,
                        17,
                        18,
                        19,
                        20,
                        -1,
                        -1,
                        21,
                        22,
                        -1,
                        -1,
                        23,
                        24,
                        25,
                        26,
                        27,
                        28,
                        29,
                        30,
                        31,
                        32,
                        -1,
                        -1,
                        33,
                        34,
                        -1,
                        -1,
                        35,
                        36,
                        37,
                        38,
                        39,
                        40,
                        41,
                        42,
                        43,
                        44,
                        45,
                        46,
                        47,
                        48,
                        49,
                        50,
                        51,
                        52,
                        53,
                        54,
                        55
                };

                private static readonly int3[] outerChunks = new int3[]
                {
                        new int3(-2, -2, -2),
                        new int3(-1, -2, -2),
                        new int3(0, -2, -2),
                        new int3(1, -2, -2),
                        new int3(-2, -1, -2),
                        new int3(-1, -1, -2),
                        new int3(0, -1, -2),
                        new int3(1, -1, -2),
                        new int3(-2, 0, -2),
                        new int3(-1, 0, -2),
                        new int3(0, 0, -2),
                        new int3(1, 0, -2),
                        new int3(-2, 1, -2),
                        new int3(-1, 1, -2),
                        new int3(0, 1, -2),
                        new int3(1, 1, -2),
                        new int3(-2, -2, -1),
                        new int3(-1, -2, -1),
                        new int3(0, -2, -1),
                        new int3(1, -2, -1),
                        new int3(-2, -1, -1),
                        new int3(1, -1, -1),
                        new int3(-2, 0, -1),
                        new int3(1, 0, -1),
                        new int3(-2, 1, -1),
                        new int3(-1, 1, -1),
                        new int3(0, 1, -1),
                        new int3(1, 1, -1),
                        new int3(-2, -2, 0),
                        new int3(-1, -2, 0),
                        new int3(0, -2, 0),
                        new int3(1, -2, 0),
                        new int3(-2, -1, 0),
                        new int3(1, -1, 0),
                        new int3(-2, 0, 0),
                        new int3(1, 0, 0),
                        new int3(-2, 1, 0),
                        new int3(-1, 1, 0),
                        new int3(0, 1, 0),
                        new int3(1, 1, 0),
                        new int3(-2, -2, 1),
                        new int3(-1, -2, 1),
                        new int3(0, -2, 1),
                        new int3(1, -2, 1),
                        new int3(-2, -1, 1),
                        new int3(-1, -1, 1),
                        new int3(0, -1, 1),
                        new int3(1, -1, 1),
                        new int3(-2, 0, 1),
                        new int3(-1, 0, 1),
                        new int3(0, 0, 1),
                        new int3(1, 0, 1),
                        new int3(-2, 1, 1),
                        new int3(-1, 1, 1),
                        new int3(0, 1, 1),
                        new int3(1, 1, 1)
                };
            }
        }

        ```

## Sims-Like Building System Using Ear Clipping Triangulation
=== "Video"
    ![type:video](assets/media/sims_like_building_system/general_showcase.mp4)

    !!! quote "About"
    
        This system was originally intended for a restaurant management game. Essentially what's happening is the wall segments are being used as sides to solve the ear clipping algorithim. This lets you generate floors for any room shape. I've also incorporated systems for other meshes to be inserted along the wall segments, this allows for placement of doors and windows.

=== "Code Samples"
    !!! quote "About"

        Showcased here are the two main classes used in generating floor meshes from wall segments. Essentially the 3D mesh information is converted to 2D where the ear clipping algorithim is then applied and converted back to 3D to serve as generated mesh data.

    === "Polygon2"

        ``` cs title="grid.cs" linenums="1"
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

        ```

    === "Triangulate"
    
        ``` cs title="register_types.cs" linenums="1"
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

        ```

## Processor Nodes & Procedural Modelling Nodes Using XNode and DOTS
=== "Video"
    ![type:video](assets/media/model_graph/showcase.mp4)

    !!! quote "About"
    
        The goal of this project was the ability to prototpe and implement procedural geometry "rulesets" at high speeds. I have a particular affinity for procedural generation in games, and as such it's required me to develop tools to structure procedural systems faster. With the procedural modelling nodes, the process of developing procedural rules becomes more akin to asset development. This tool was built using XNode libraries, which I've used to implement directional evaluation of nodes, aswell as value type and collection convertability between nodes (e.g. a float output could be converted to a vector input or a collection of vectors could be converted to a single value and vice versa.) The tool was inspired by Blender's geometry nodes, with the hopes that it could integrate into the Unity workflow just as seamlessly.
        
=== "Code Samples"
    !!! quote "About"
        The modelling nodes were designed with a flexible inheritence hiearchy in mind. All modelling nodes inherit from another type of node called a "ProcessorNode". Essentially what this node does is provide basic functionality for processing mutable input and output values in a sequential fashion. If you wanted to create a different type of ProcessorNode, perhaps for defining AI behaviors or animation states, you could easily use the ProcessorNode as a base class.

    === "ProcessorGraph"

        ``` cs title="ProcessorGraph.cs" linenums="1"
        using System.Collections;
        using System.Collections.Generic;
        using UnityEngine;
        using XNode;
        using System;
        using System.Linq;
        using Unity.Collections;

        #if UNITY_EDITOR
        using XNodeEditor;
        #endif

        namespace XNodeCore
        {
            ///<summary>A graph whose nodes can process ports before passing them. This is useful for caching large calculations and re-using them.</summary>
            [CreateAssetMenu]
            public class ProcessorGraph : NodeGraph
            {
                public static ProcessorGraph currentGraph;

                public StringList<Parameter> parameters = new StringList<Parameter>();
                public ProcessorGraph inheritParameters;

                public virtual Type[] IncludeNodeTypes { get; } = new Type[] { typeof(ProcessorNode) };
                public virtual Type[] ExcludeNodeTypes { get; } = new Type[0];

                private HashSet<Tuple<int, int>> processedFields = new HashSet<Tuple<int, int>>();
                private HashSet<ProcessorNode> processedNodes = new HashSet<ProcessorNode>();
                public void Process()
                {
                    Field.converter.cachedConversions.Clear();
                    processedFields.Clear();
                    processedNodes.Clear();
                    foreach (ProcessorNode capNode in nodes.FindAll((n) =>
                    {
                        ProcessorNode pN = n as ProcessorNode;
                        return pN != null && !pN.outFields.Any((oF) => oF.Connected == true);
                    }))
                    {
                        ProcessRecursive(capNode);
                    }
                }

                public ProcessorGraph() => currentGraph = this;

                private void ProcessBranch(Field a, Field b)
                {
                    Tuple<int, int> pair = new Tuple<int, int>(a.GetHashCode(), b.GetHashCode());

                    if (!processedFields.Contains(pair))
                    {
                        processedFields.Add(pair);
                        ProcessRecursive(b.node);
                    }
                }

                private void ProcessRecursive(ProcessorNode node)
                {
                    bool processed = processedNodes.Add(node);
                    if (processed) node.ConfigureInFields();

                    // Process All Needed Inputs
                    foreach (InField inField in node.inFields)
                    {
                        if (inField.Connected)
                        {
                            ProcessBranch(inField, inField.connectedOutField);
                        }
                    }

                    if (processed)
                    {
                        node.GraphProcess();
                        node.ConfigureOutFields();
                    }
                }

                private static readonly Type[] validTypes = new Type[]
                {
                    typeof(InSingleField<>),
                    typeof(InMultiField<>),
                    typeof(OutSingleField<>),
                    typeof(OutMultiField<>)
                };

                private static readonly Type[] inputTypes = new Type[]
                {
                    typeof(InSingleField<>),
                    typeof(InMultiField<>)
                };

                private static readonly Type[] outputTypes = new Type[]
                {
                    typeof(OutSingleField<>),
                    typeof(OutMultiField<>)
                };

                private static readonly Type[] multiTypes = new Type[]
                {
                    typeof(InMultiField<>),
                    typeof(OutMultiField<>)
                };

                public static bool IsValid(Type type) => validTypes.Contains(type.GetGenericTypeDefinition());
                public static bool IsInput(Type type) => inputTypes.Contains(type.GetGenericTypeDefinition());
                public static bool IsOutput(Type type) => outputTypes.Contains(type.GetGenericTypeDefinition());
                public static bool IsMulti(Type type) => multiTypes.Contains(type.GetGenericTypeDefinition());
                public static bool IsSingle(Type type) => !IsMulti(type);

                #if UNITY_EDITOR

                public static void SetCircleTexture(NodePort port)
                {
                    NodeEditorWindow.current.graphEditor.GetPortStyle(port).normal.background = NodeEditorResources.dotOuter;
                    NodeEditorWindow.current.graphEditor.GetPortStyle(port).active.background = NodeEditorResources.dot;
                }

                public static void SetTriangleTexture(NodePort port)
                {
                    NodeEditorWindow.current.graphEditor.GetPortStyle(port).normal.background = Resources.Load<Texture2D>("xnodecore_tri_outer");
                    NodeEditorWindow.current.graphEditor.GetPortStyle(port).active.background = Resources.Load<Texture2D>("xnodecore_tri");
                }

                public static void SetHollowTriangleTexture(NodePort port)
                {
                    NodeEditorWindow.current.graphEditor.GetPortStyle(port).normal.background = Resources.Load<Texture2D>("xnodecore_tri_outer");
                    NodeEditorWindow.current.graphEditor.GetPortStyle(port).active.background = Resources.Load<Texture2D>("xnodecore_tri_hollow");
                }

                public static void SetPortTexture(NodePort port)
                {
                    //Debug.Log(port.ValueType);
                    if (port.ValueType.IsGenericType && IsMulti(port.ValueType))
                    {
                        if (port.IsConnected && IsSingle(port.GetConnection(0).ValueType)) SetHollowTriangleTexture(port);
                        else if (port.IsConnected || port.IsOutput) SetTriangleTexture(port);
                        else SetHollowTriangleTexture(port);
                        return;
                    }
                    else SetCircleTexture(port);
                }

                #endif
            }
        }
        ```

    === "ProcessorNode"
    
        ``` cs title="ProcessorNode.cs" linenums="1"
        using System.Collections;
        using System.Collections.Generic;
        using UnityEngine;
        using XNode;
        using System;
        using System.Linq;

        namespace XNodeCore
        {
            ///<summary>A node which can process ports before passing them. This is useful for caching large calculations and re-using them.</summary>
            public abstract class ProcessorNode : Node
            {
                public ProcessorGraph pGraph;

                public static List<InField> currentInFields = new List<InField>();
                public static List<OutField> currentOutFields = new List<OutField>();
                [NonSerialized] public List<InField> inFields = new List<InField>();
                [NonSerialized] public List<OutField> outFields = new List<OutField>();

                public ProcessorNode()
                {
                    inFields = currentInFields;
                    currentInFields = new List<InField>();

                    outFields = currentOutFields;
                    currentOutFields = new List<OutField>();
                }

                public new void OnEnable()
                {
                    base.OnEnable();
                    pGraph = ProcessorGraph.currentGraph;
                    CreateDynamicFields();
                }

                public override object GetValue(NodePort port) => null;

                public void ConfigureInFields()
                {
                    foreach (InField inField in inFields)
                    {
                        NodePort port = GetInputPort(inField.name);
                        if (port != null && port.IsConnected) inField.connectedOutField = ((ProcessorNode)port.GetConnection(0).node).outFields.Find((o) => o.name == port.GetConnection(0).fieldName);
                    }
                }

                public void GraphProcess()
                {
                    foreach (InField inField in inFields)
                    {
                        inField.Configure();
                    }

                    Process();
                }

                public void ConfigureOutFields()
                {
                    foreach (OutField outField in outFields)
                    {
                        NodePort port = GetOutputPort(outField.name);
                        if (port != null && port.IsConnected) outField.connectedInField = ((ProcessorNode)port.GetConnection(0).node).inFields.Find((i) => i.name == port.GetConnection(0).fieldName);

                        outField.ClearCache();
                    }
                }

                public void CreateDynamicFields()
                {
                    DynamicFields();

                    foreach (InField field in currentInFields)
                    {
                        field.isDynamic = true;
                        field.node = this;
                        NodePort port = GetInputPort(field.name);
                        if (port == null) port = AddDynamicInput(field.ValueType, fieldName: field.name);
                    }
                    foreach (OutField field in currentOutFields)
                    {
                        field.isDynamic = true;
                        field.node = this;
                        NodePort port = GetOutputPort(field.name);
                        if (port == null) port = AddDynamicOutput(field.ValueType, fieldName: field.name);
                    }

                    inFields.AddRange(currentInFields);
                    outFields.AddRange(currentOutFields);
                    currentInFields.Clear();
                    currentOutFields.Clear();
                }

                protected virtual void DynamicFields() { }

                #if UNITY_EDITOR
                public virtual Type DynamicType() => GetType();
                public virtual void DynamicCopy(ProcessorNode target) { }

                public virtual InField InputPortToField(string fieldName) => (InField)GetType().GetField(fieldName).GetValue(this);
                public InField InputPortToField(NodePort port) => InputPortToField(port.fieldName);

                public virtual OutField OutputPortToField(string fieldName) => (OutField)GetType().GetField(fieldName).GetValue(this);
                public OutField OutputPortToField(NodePort port) => OutputPortToField(port.fieldName);
                #endif

                ///<summary>Receive and process port information before passing it.</summary>
                public virtual void Process() { }
            }
        }
        ```

    === "LogNode"

        ``` cs title="LogNode.cs" linenums="1"
        using System.Collections;
        using System.Collections.Generic;
        using UnityEngine;
        using XNode;

        namespace XNodeCore
        {
            [CreateNodeMenu("Debug/Log")]
            public class LogNode : CoreNode
            {
                public InSingleField<object> input = new InSingleField<object>();

                public override void Process()
                {
                    Debug.Log(input.Value);
                }
            }
        }
        ```

    === "OperatorNode"
    
        ``` cs title="ExtrudeMeshOperatorNode.cs" linenums="1"
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
        ```
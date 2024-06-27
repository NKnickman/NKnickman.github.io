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

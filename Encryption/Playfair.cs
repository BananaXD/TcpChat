using System;
using System.Collections.Generic;
using System.Linq;

namespace EncryptionLibrary {
    public class PlayfairEncryption {
        private byte[,] _grid;
        private Dictionary<byte, (int row, int col)> _positions;
        private const int GRID_SIZE = 16; // 16x16 = 256 bytes
        private const byte DUPLICATE_MARKER = 0x1F; // Special marker for duplicates

        public PlayfairEncryption(string key) {
            GenerateGrid(key);
        }

        private void GenerateGrid(string key) {
            _grid = new byte[GRID_SIZE, GRID_SIZE];
            _positions = new Dictionary<byte, (int, int)>();

            // Convert key to bytes and remove duplicates while preserving order
            var keyBytes = System.Text.Encoding.UTF8.GetBytes(key)
                .Distinct()
                .ToList();

            // Create list of all possible bytes (0-255)
            var allBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToList();

            // Remove key bytes from all bytes list
            foreach (var keyByte in keyBytes) {
                allBytes.Remove(keyByte);
            }

            // Combine key bytes with remaining bytes
            var gridBytes = keyBytes.Concat(allBytes).ToArray();

            // Fill the 16x16 grid
            int index = 0;
            for (int row = 0; row < GRID_SIZE; row++) {
                for (int col = 0; col < GRID_SIZE; col++) {
                    _grid[row, col] = gridBytes[index];
                    _positions[gridBytes[index]] = (row, col);
                    index++;
                }
            }
        }

        public byte[] Encrypt(byte[] data) {
            var processedData = HandleDuplicates(data);
            var result = new List<byte>();

            // Process pairs
            for (int i = 0; i < processedData.Count; i += 2) {
                byte first = processedData[i];
                byte second = i + 1 < processedData.Count ? processedData[i + 1] : (byte)0;

                var encryptedPair = EncryptPair(first, second);
                result.AddRange(encryptedPair);
            }

            return result.ToArray();
        }

        public byte[] Decrypt(byte[] encryptedData) {
            var result = new List<byte>();

            // Process pairs
            for (int i = 0; i < encryptedData.Length; i += 2) {
                byte first = encryptedData[i];
                byte second = i + 1 < encryptedData.Length ? encryptedData[i + 1] : (byte)0;

                var decryptedPair = DecryptPair(first, second);
                result.AddRange(decryptedPair);
            }

            return RestoreDuplicates(result).ToArray();
        }

        private List<byte> HandleDuplicates(byte[] data) {
            var result = new List<byte>();

            for (int i = 0; i < data.Length; i++) {
                result.Add(data[i]);

                // If current byte equals next byte, insert duplicate marker
                if (i + 1 < data.Length && data[i] == data[i + 1]) {
                    result.Add(DUPLICATE_MARKER);
                }
            }

            return result;
        }

        private List<byte> RestoreDuplicates(List<byte> data) {
            var result = new List<byte>();

            for (int i = 0; i < data.Count; i++) {
                if (data[i] == DUPLICATE_MARKER) {
                    // Skip duplicate marker, the duplicate will be restored naturally
                    continue;
                }
                result.Add(data[i]);
            }

            return result;
        }

        private byte[] EncryptPair(byte first, byte second) {
            var pos1 = _positions[first];
            var pos2 = _positions[second];

            // Same row - move right
            if (pos1.row == pos2.row) {
                int newCol1 = (pos1.col + 1) % GRID_SIZE;
                int newCol2 = (pos2.col + 1) % GRID_SIZE;
                return new byte[] { _grid[pos1.row, newCol1], _grid[pos2.row, newCol2] };
            }

            // Same column - move down
            if (pos1.col == pos2.col) {
                int newRow1 = (pos1.row + 1) % GRID_SIZE;
                int newRow2 = (pos2.row + 1) % GRID_SIZE;
                return new byte[] { _grid[newRow1, pos1.col], _grid[newRow2, pos2.col] };
            }

            // Rectangle - swap columns
            return new byte[] { _grid[pos1.row, pos2.col], _grid[pos2.row, pos1.col] };
        }

        private byte[] DecryptPair(byte first, byte second) {
            var pos1 = _positions[first];
            var pos2 = _positions[second];

            // Same row - move left
            if (pos1.row == pos2.row) {
                int newCol1 = (pos1.col - 1 + GRID_SIZE) % GRID_SIZE;
                int newCol2 = (pos2.col - 1 + GRID_SIZE) % GRID_SIZE;
                return new byte[] { _grid[pos1.row, newCol1], _grid[pos2.row, newCol2] };
            }

            // Same column - move up
            if (pos1.col == pos2.col) {
                int newRow1 = (pos1.row - 1 + GRID_SIZE) % GRID_SIZE;
                int newRow2 = (pos2.row - 1 + GRID_SIZE) % GRID_SIZE;
                return new byte[] { _grid[newRow1, pos1.col], _grid[newRow2, pos2.col] };
            }

            // Rectangle - swap columns
            return new byte[] { _grid[pos1.row, pos2.col], _grid[pos2.row, pos1.col] };
        }
    }
}
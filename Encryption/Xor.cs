using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EncryptionLibrary {
    public static class XorEncryption {
        /// <summary>
        /// Encrypts or decrypts data using a repeating XOR key.
        /// </summary>
        /// <param name="data">The byte array to process.</param>
        /// <param name="key">The string key.</param>
        /// <returns>The processed byte array.</returns>
        private static byte[] Process(byte[] data, string key) {
            if (data == null || data.Length == 0) {
                return data;
            }

            if (string.IsNullOrEmpty(key)) {
                // Returning original data if key is invalid.
                // For real applications, throwing an exception is often better.
                return data;
            }

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] result = new byte[data.Length];

            for (int i = 0; i < data.Length; i++) {
                result[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
            }

            return result;
        }

        /// <summary>
        /// Encrypts a byte array using a string key.
        /// </summary>
        public static byte[] Encrypt(byte[] data, string key) {
            return Process(data, key);
        }

        /// <summary>
        /// Decrypts a byte array using a string key.
        /// </summary>
        public static byte[] Decrypt(byte[] encryptedData, string key) {
            // XOR encryption is symmetrical; the same operation decrypts the data.
            return Process(encryptedData, key);
        }
    }
}
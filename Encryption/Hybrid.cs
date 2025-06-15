using System;
using System.Security.Cryptography;
using System.Text;

namespace EncryptionLibrary {
    public static class HybridEncryption {
        private static string GenerateRandomKey() {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
            var random = new Random();
            var sessionKey = new StringBuilder();

            for (int i = 0; i < 32; i++) // 32 character session key
            {
                sessionKey.Append(chars[random.Next(chars.Length)]);
            }

            return sessionKey.ToString();
        }
        
        // Static functions to encrypt data or text using the hybrid encryption
        public static (string content, string key) Encrypt(string plaintext, RSAKeyPair.PublicKey publicKey) {
            var key = GenerateRandomKey();
            var playfair = new PlayfairEncryption(key);
            if (playfair == null)
                throw new InvalidOperationException("Client encryption not initialized");

            return (playfair.Encrypt(plaintext), RSAEncryption.Encrypt(key, publicKey));
        }
        public static string Decrypt(string encryptedContent, string encryptedKey, RSAKeyPair.PrivateKey privateKey) {
            var decryptedKey = RSAEncryption.Decrypt(encryptedKey, privateKey);
            return new PlayfairEncryption(decryptedKey).Decrypt(encryptedContent);
        }

        // for bytes -- genarally will be used for files or binary data. to make sure it is safely encrypted/decrypted, we use a different encryption.
        public static (byte[] content, string key) Encrypt(byte[] data, RSAKeyPair.PublicKey publicKey) {
            var key = Guid.NewGuid().ToString();
            var encryptedKey = RSAEncryption.Encrypt(key, publicKey);

            return (XorEncryption.Encrypt(data, key), key);
        }
        public static byte[] Decrypt(byte[] encryptedData, string encryptedKey, RSAKeyPair.PrivateKey privateKey) {
            var decryptedKey = RSAEncryption.Decrypt(encryptedKey, privateKey);
            return XorEncryption.Decrypt(encryptedData, decryptedKey);
        }
    }
}
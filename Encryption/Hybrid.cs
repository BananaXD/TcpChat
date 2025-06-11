using System;
using System.Security.Cryptography;
using System.Text;

namespace EncryptionLibrary {
    public class HybridEncryption {
        private PlayfairEncryption _playfairCipher;
        private string _sessionKey;
        public string SessionKeyEncrypted(RSAKeyPair.PublicKey publicKey) => RSAEncryption.Encrypt(_sessionKey, publicKey);

        public HybridEncryption() {
            GenerateSessionKey();
            _playfairCipher = new PlayfairEncryption(_sessionKey!);
        }

        public HybridEncryption(string? sessionKey = null) {
            _sessionKey = sessionKey ?? GenerateRandomSessionKey();
            _playfairCipher = new PlayfairEncryption(_sessionKey);
        }

        private void GenerateSessionKey() {
            _sessionKey = GenerateRandomSessionKey();
        }

        private string GenerateRandomSessionKey() {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
            var random = new Random();
            var sessionKey = new StringBuilder();

            for (int i = 0; i < 32; i++) // 32 character session key
            {
                sessionKey.Append(chars[random.Next(chars.Length)]);
            }

            return sessionKey.ToString();
        }

        public byte[] EncryptSessionKey(RSAKeyPair.PublicKey recipientPublicKey) {
            byte[] sessionKeyBytes = Encoding.UTF8.GetBytes(_sessionKey);
            return RSAEncryption.Encrypt(sessionKeyBytes, recipientPublicKey);
        }

        public byte[] Encrypt(byte[] data) {
            // Use Playfair for fast encryption of actual data
            return _playfairCipher.Encrypt(data);
        }
        public byte[] Decrypt(byte[] encryptedData) {
            // Use Playfair for fast decryption of actual data
            return _playfairCipher.Decrypt(encryptedData);
        }

        public string EncryptText(string plaintext) {
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] encryptedBytes = Encrypt(plaintextBytes);
            return Convert.ToBase64String(encryptedBytes);
        }
        public string DecryptText(string encryptedText) {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
            byte[] decryptedBytes = Decrypt(encryptedBytes);
            return Encoding.UTF8.GetString(decryptedBytes);
        }

        public void RotateSessionKey() {
            GenerateSessionKey();
            _playfairCipher = new PlayfairEncryption(_sessionKey);
        }

        public string GetSessionKey() {
            return _sessionKey;
        }

        // Static functions to encrypt data or text using the hybrid encryption
        public static (string content, string key) Encrypt(string plaintext, RSAKeyPair.PublicKey publicKey) {
            var hybridEncryption = new HybridEncryption();
            if (hybridEncryption == null)
                throw new InvalidOperationException("Client encryption not initialized");

            return (hybridEncryption.EncryptText(plaintext), hybridEncryption.SessionKeyEncrypted(publicKey));
        }

        public static (byte[] content, string key) Encrypt(byte[] data, RSAKeyPair.PublicKey publicKey) {
            var hybridEncryption = new HybridEncryption();
            if (hybridEncryption == null)
                throw new InvalidOperationException("Client encryption not initialized");

            return (hybridEncryption.Encrypt(data), hybridEncryption.SessionKeyEncrypted(publicKey));
        }

        public static string Decrypt(string encryptedContent, string encryptedKey, RSAKeyPair.PrivateKey privateKey) {
            var decryptedKey = RSAEncryption.Decrypt(encryptedKey, privateKey);
            return new HybridEncryption(decryptedKey).DecryptText(encryptedContent);
        }

        public static byte[] Decrypt(byte[] encryptedData, string encryptedKey, RSAKeyPair.PrivateKey privateKey) {
            var decryptedKey = RSAEncryption.Decrypt(encryptedKey, privateKey);
            return new HybridEncryption(decryptedKey).Decrypt(encryptedData);
        }
    }
}
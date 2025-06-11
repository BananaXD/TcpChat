using System;
using System.Numerics;
using System.Text;

namespace EncryptionLibrary {
    public class RSAKeyPair {
        public class PublicKey {
            public BigInteger E { get; set; }
            public BigInteger N { get; set; }

            public PublicKey(BigInteger e, BigInteger n) {
                E = e;
                N = n;
            }
        }

        public class PrivateKey {
            public BigInteger D { get; set; }
            public BigInteger N { get; set; }

            public PrivateKey(BigInteger d, BigInteger n) {
                D = d;
                N = n;
            }
        }

        public PublicKey Public { get; set; }
        public PrivateKey Private { get; set; }

        public RSAKeyPair(PublicKey publicKey, PrivateKey privateKey) {
            Public = publicKey;
            Private = privateKey;
        }
    }

    public class RSAEncryption {
        private static readonly Random _random = new Random();

        public static BigInteger GeneratePrime(int bitLength) {
            BigInteger candidate;
            do {
                candidate = GenerateRandomBigInteger(bitLength);
                if (candidate % 2 == 0) candidate++;
            }
            while (!MillerRabin.IsProbablePrime(candidate, 20));

            return candidate;
        }

        private static BigInteger GenerateRandomBigInteger(int bitLength) {
            byte[] bytes = new byte[bitLength / 8];
            _random.NextBytes(bytes);
            bytes[bytes.Length - 1] &= 0x7F; // Ensure high bit is set
            return new BigInteger(bytes);
        }

        private static BigInteger GenerateRandomBigInteger(BigInteger min, BigInteger max) {
            byte[] maxBytes = max.ToByteArray();
            BigInteger result;
            do {
                byte[] bytes = new byte[maxBytes.Length];
                _random.NextBytes(bytes);
                bytes[bytes.Length - 1] &= 0x7F; // Ensure positive
                result = new BigInteger(bytes);
            }
            while (result < min || result >= max);

            return result;
        }

        public static RSAKeyPair GenerateKeyPair(int keySize = 1024) {
            BigInteger p = GeneratePrime(keySize / 2);
            BigInteger q = GeneratePrime(keySize / 2);
            BigInteger n = p * q;
            BigInteger phi = (p - 1) * (q - 1);

            BigInteger e = 65537; // Common choice
            BigInteger d = ModInverse(e, phi);

            return new RSAKeyPair(
                new RSAKeyPair.PublicKey(e, n),
                new RSAKeyPair.PrivateKey(d, n)
            );
        }

        private static BigInteger ModInverse(BigInteger a, BigInteger m) {
            BigInteger m0 = m, x0 = 0, x1 = 1;

            if (m == 1) return 0;

            while (a > 1) {
                BigInteger q = a / m;
                BigInteger t = m;
                m = a % m;
                a = t;
                t = x0;
                x0 = x1 - q * x0;
                x1 = t;
            }

            if (x1 < 0) x1 += m0;
            return x1;
        }

        public static byte[] Encrypt(byte[] data, RSAKeyPair.PublicKey publicKey) {
            BigInteger message = new BigInteger(data);
            BigInteger encrypted = BigInteger.ModPow(message, publicKey.E, publicKey.N);
            return encrypted.ToByteArray();
        }
        public static byte[] Decrypt(byte[] encryptedData, RSAKeyPair.PrivateKey privateKey) {
            BigInteger encrypted = new BigInteger(encryptedData);
            BigInteger decrypted = BigInteger.ModPow(encrypted, privateKey.D, privateKey.N);
            return decrypted.ToByteArray();
        }
        
        public static string Encrypt(string plainText, RSAKeyPair.PublicKey publicKey) {
            // 1. Convert the string to bytes using UTF-8 encoding
            byte[] dataToEncrypt = Encoding.UTF8.GetBytes(plainText);

            // 2. Encrypt the byte array
            byte[] encryptedData = Encrypt(dataToEncrypt, publicKey);

            // 3. Convert the encrypted bytes to a Base64 string for safe transport
            return Convert.ToBase64String(encryptedData);
        }
        public static string Decrypt(string encryptedText, RSAKeyPair.PrivateKey privateKey) {
            // 1. Convert the Base64 string back to a byte array
            byte[] dataToDecrypt = Convert.FromBase64String(encryptedText);

            // 2. Decrypt the byte array
            byte[] decryptedData = Decrypt(dataToDecrypt, privateKey);

            // 3. Convert the decrypted bytes back to a string using UTF-8
            return Encoding.UTF8.GetString(decryptedData);
        }
    }
}

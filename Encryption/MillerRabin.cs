using System.Numerics;
using System.Security.Cryptography;

namespace EncryptionLibrary;
public static class MillerRabin {
    /// <summary>
    /// Performs the Miller-Rabin primality test on a BigInteger.
    /// </summary>
    /// <param name="n">The number to test for primality.</param>
    /// <param name="k">The number of iterations (witnesses) to test.
    /// A higher value decreases the probability of a composite number being
    /// declared prime.</param>
    /// <returns>True if n is probably prime, false if n is definitely composite.</returns>
    public static bool IsProbablePrime(BigInteger n, int k) {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n % 2 == 0) return false; // Handle even numbers greater than 2

        // Write n as (2^s) * d + 1
        BigInteger d = n - 1;
        int s = 0;
        while (d % 2 == 0) {
            d /= 2;
            s += 1;
        }

        // Witness loop
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) {
            byte[] bytes = new byte[(n.ToByteArray().Length + 1) / 2]; // Roughly half the bytes for a BigInteger
            for (int i = 0; i < k; i++) {
                BigInteger a;
                do {
                    rng.GetBytes(bytes);
                    a = new BigInteger(bytes);
                    a = BigInteger.Abs(a); // Ensure positive
                    // a must be in the range [2, n-2]
                } while (a < 2 || a >= n - 1);

                if (!Witness(a, n, d, s)) {
                    return false; // n is composite
                }
            }
        }

        return true; // n is probably prime
    }

    /// <summary>
    /// Helper function for the Miller-Rabin test.
    /// Checks if 'a' is a strong witness for the compositeness of 'n'.
    /// </summary>
    /// <param name="a">The base for the test.</param>
    /// <param name="n">The number being tested.</param>
    /// <param name="d">n-1 written as 2^s * d, where d is odd.</param>
    /// <param name="s">n-1 written as 2^s * d, where d is odd.</param>
    /// <returns>True if a is not a witness (n might be prime), false if a is a witness (n is composite).</returns>
    private static bool Witness(BigInteger a, BigInteger n, BigInteger d, int s) {
        BigInteger x = BigInteger.ModPow(a, d, n);

        if (x == 1 || x == n - 1) {
            return true; // a is not a witness
        }

        for (int r = 1; r < s; r++) {
            x = BigInteger.ModPow(x, 2, n);
            if (x == n - 1) {
                return true; // a is not a witness
            }
        }

        return false; // a is a witness, n is composite
    }
}
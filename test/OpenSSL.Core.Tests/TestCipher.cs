using System;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

using Xunit;
using Xunit.Abstractions;

using OpenSSL.Core.ASN1;
using OpenSSL.Core.Keys;
using OpenSSL.Core.Ciphers;
using OpenSSL.Core.Interop;

namespace OpenSSL.Core.Tests
{
	public class TestCipher : TestBase
	{
        private static List<object[]> lstCiphers;
        public static IEnumerable<object[]> GetCiphers() => lstCiphers;

        static TestCipher()
        {
            lstCiphers = new List<object[]>();
            Type type = typeof(CipherType);
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (FieldInfo field in fields.Where(x => x.Name != "NONE").Where(x => x.Name != "DES_EDE3_CFB1"))
                lstCiphers.Add(new object[] { field.GetValue(null) });
        }

        public TestCipher(ITestOutputHelper outputHelper)
            : base(outputHelper) { }

        protected override void Dispose(bool disposing) { }

        [Fact]
        public void TestCipherList()
        {
            HashSet<string> lstCiphers = Cipher.SupportedCiphers;
            Assert.NotNull(lstCiphers);
            Assert.NotEmpty(lstCiphers);
        }

        [Theory]
		[MemberData(nameof(GetCiphers))]
		public void TestEncryptDecrypt(CipherType cipherType)
		{
			string inputMsg = "This is a message";
			byte[] input = Encoding.ASCII.GetBytes(inputMsg);
			byte[] iv = Encoding.ASCII.GetBytes("12345678");
			byte[] key = Encoding.ASCII.GetBytes("This is the key");

            this.OutputHelper.WriteLine("Using cipher {0}: ", cipherType.LongName);

            byte[] encrypted;
            int encryptedLength, finalEncryptedLength;
            using (CipherEncryption cipherEncryption = new CipherEncryption(cipherType, key, iv))
            {
                byte[] tempBuf = new byte[cipherEncryption.GetMaximumOutputLength(input.Length)];
                byte[] finalBuf = new byte[cipherEncryption.GetCipherBlockSize()];

                Span<byte> inputSpan = new Span<byte>(input);
                Span<byte> outputSpan = new Span<byte>(tempBuf);

                encryptedLength = cipherEncryption.Update(inputSpan, ref outputSpan);

                outputSpan = new Span<byte>(finalBuf);
                finalEncryptedLength = cipherEncryption.Finalize(ref outputSpan);

                encrypted = new byte[encryptedLength + finalEncryptedLength];
                Buffer.BlockCopy(tempBuf, 0, encrypted, 0, encryptedLength);
                Buffer.BlockCopy(finalBuf, 0, encrypted, encryptedLength, finalEncryptedLength);
            }

            byte[] decrypted;
            int decryptedLength, finalDecryptedLength;
            using (CipherDecryption cipherDecryption = new CipherDecryption(cipherType, key, iv))
            {
                byte[] tempBuf = new byte[cipherDecryption.GetMaximumOutputLength(input.Length)];
                byte[] finalBuf = new byte[cipherDecryption.GetCipherBlockSize()];

                Span<byte> inputSpan = new Span<byte>(encrypted);
                Span<byte> outputSpan = new Span<byte>(tempBuf);

                decryptedLength = cipherDecryption.Update(inputSpan, ref outputSpan);

                outputSpan = new Span<byte>(finalBuf);
                finalDecryptedLength = cipherDecryption.Finalize(ref outputSpan);

                decrypted = new byte[decryptedLength + finalDecryptedLength];
                Buffer.BlockCopy(tempBuf, 0, decrypted, 0, decryptedLength);
                Buffer.BlockCopy(finalBuf, 0, decrypted, decryptedLength, finalDecryptedLength);
            }

            string outputMsg = Encoding.ASCII.GetString(decrypted, 0, decryptedLength + finalDecryptedLength);
            Assert.Equal(inputMsg, outputMsg);
		}

		[Theory]
        [MemberData(nameof(GetCiphers))]
        public void TestEncryptDecryptWithSalt(CipherType cipherType)
		{
			string inputMsg = "This is a message";
			byte[] input = Encoding.ASCII.GetBytes(inputMsg);
			byte[] salt = Encoding.ASCII.GetBytes("salt");
			byte[] secret = Encoding.ASCII.GetBytes("Password!");

            this.OutputHelper.WriteLine("Using cipher {0}: ", cipherType.LongName);

            byte[] encrypted;
            int encryptedLength, finalEncryptedLength;
            using (CipherEncryption cipherEncryption = new CipherEncryption(cipherType, DigestType.SHA1, salt, secret))
            {
                byte[] tempBuf = new byte[cipherEncryption.GetMaximumOutputLength(input.Length)];
                byte[] finalBuf = new byte[cipherEncryption.GetCipherBlockSize()];

                Span<byte> inputSpan = new Span<byte>(input);
                Span<byte> outputSpan = new Span<byte>(tempBuf);

                encryptedLength = cipherEncryption.Update(inputSpan, ref outputSpan);

                outputSpan = new Span<byte>(finalBuf);
                finalEncryptedLength = cipherEncryption.Finalize(ref outputSpan);

                encrypted = new byte[encryptedLength + finalEncryptedLength];
                Buffer.BlockCopy(tempBuf, 0, encrypted, 0, encryptedLength);
                Buffer.BlockCopy(finalBuf, 0, encrypted, encryptedLength, finalEncryptedLength);
            }

            byte[] decrypted;
            int decryptedLength, finalDecryptedLength;
            using (CipherDecryption cipherDecryption = new CipherDecryption(cipherType, DigestType.SHA1, salt, secret))
            {
                byte[] tempBuf = new byte[cipherDecryption.GetMaximumOutputLength(input.Length)];
                byte[] finalBuf = new byte[cipherDecryption.GetCipherBlockSize()];

                Span<byte> inputSpan = new Span<byte>(encrypted);
                Span<byte> outputSpan = new Span<byte>(tempBuf);

                decryptedLength = cipherDecryption.Update(inputSpan, ref outputSpan);

                outputSpan = new Span<byte>(finalBuf);
                finalDecryptedLength = cipherDecryption.Finalize(ref outputSpan);

                decrypted = new byte[decryptedLength + finalDecryptedLength];
                Buffer.BlockCopy(tempBuf, 0, decrypted, 0, decryptedLength);
                Buffer.BlockCopy(finalBuf, 0, decrypted, decryptedLength, finalDecryptedLength);
            }

            string outputMsg = Encoding.ASCII.GetString(decrypted);
            Assert.Equal(inputMsg, outputMsg);
        }

        [Fact]
        public void TestSealOpen()
		{
            CipherType cipherType = CipherType.AES_128_CBC;

            RSAKey[] keys = new RSAKey[10];
            for (int i = 0; i < 10; i++)
            {
                keys[i] = new RSAKey(1024);
                keys[i].GenerateKey();
            }

            string inputMsg = "This is a message";
            byte[] input = Encoding.ASCII.GetBytes(inputMsg);
            byte[][] encryptionKeys;
            byte[] iv;

            byte[] encrypted;
            int encryptedLength, finalEncryptedLength;
            using (EnvelopeEncryption envelopeSeal = new EnvelopeEncryption(cipherType, keys))
            {
                byte[] tempBuf = new byte[envelopeSeal.GetMaximumOutputLength(input.Length)];
                byte[] finalBuf = new byte[envelopeSeal.GetCipherBlockSize()];

                Span<byte> inputSpan = new Span<byte>(input);
                Span<byte> outputSpan = new Span<byte>(tempBuf);

                encryptedLength = envelopeSeal.Update(inputSpan, ref outputSpan);

                outputSpan = new Span<byte>(finalBuf);
                finalEncryptedLength = envelopeSeal.Finalize(ref outputSpan);

                encrypted = new byte[encryptedLength + finalEncryptedLength];
                Buffer.BlockCopy(tempBuf, 0, encrypted, 0, encryptedLength);
                Buffer.BlockCopy(finalBuf, 0, encrypted, encryptedLength, finalEncryptedLength);

                encryptionKeys = envelopeSeal.EncryptionKeys;
                iv = envelopeSeal.IV;
            }

            EnvelopeDecryption envelopeOpen;
            string outputMsg;
            byte[] decrypted;
            int decryptedLength, finalDecryptedLength;
            for (int i = 0; i < keys.Length; i++)
            {
                using (envelopeOpen = new EnvelopeDecryption(cipherType, keys[i], encryptionKeys[i], iv))
                {
                    byte[] tempBuf = new byte[envelopeOpen.GetMaximumOutputLength(input.Length)];
                    byte[] finalBuf = new byte[envelopeOpen.GetCipherBlockSize()];

                    Span<byte> inputSpan = new Span<byte>(encrypted);
                    Span<byte> outputSpan = new Span<byte>(tempBuf);

                    decryptedLength = envelopeOpen.Update(inputSpan, ref outputSpan);

                    outputSpan = new Span<byte>(finalBuf);
                    finalDecryptedLength = envelopeOpen.Finalize(ref outputSpan);

                    decrypted = new byte[decryptedLength + finalDecryptedLength];
                    Buffer.BlockCopy(tempBuf, 0, decrypted, 0, decryptedLength);
                    Buffer.BlockCopy(finalBuf, 0, decrypted, decryptedLength, finalDecryptedLength);
                }

                outputMsg = Encoding.ASCII.GetString(decrypted);
                Assert.Equal(inputMsg, outputMsg);
            }

            foreach (PrivateKey key in keys)
                key.Dispose();
        }
	}
}

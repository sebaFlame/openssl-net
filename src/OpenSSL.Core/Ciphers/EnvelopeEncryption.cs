﻿using System;
using System.Collections.Generic;
using System.Text;

using OpenSSL.Core.ASN1;
using OpenSSL.Core.Keys;
using OpenSSL.Core.Interop.SafeHandles.Crypto;
using System.Runtime.InteropServices;

namespace OpenSSL.Core.Ciphers
{
    public class EnvelopeEncryption : Cipher
    {
        /// <summary>
        /// The public keys used to encrypt
        /// </summary>
        public IPublicKey[] publicKeys { get; private set; }

        /// <summary>
        /// The generated keys to decrypt per public key
        /// </summary>
        public byte[][] EncryptionKeys { get; private set; }

        /// <summary>
        /// The IV used to encrypt (can be empty if the cipher doesn't support it)
        /// </summary>
        public byte[] IV { get; private set; }

        private int ivLength;

        public EnvelopeEncryption(CipherType cipherType, params IPublicKey[] publicKeys)
            : base(cipherType)
        {
            this.publicKeys = publicKeys;

            if ((this.ivLength = this.GetIVLength()) > 0)
                this.IV = Interop.Random.Bytes(this.GetIVLength());
            else
                this.IV = Array.Empty<byte>();

            this.Initialize();
        }

        public EnvelopeEncryption(CipherType cipherType, byte[] iv, params IPublicKey[] publicKeys)
            : base(cipherType)
        {
            this.publicKeys = publicKeys;

            if((this.ivLength = this.GetIVLength()) > 0 && !(iv is null))
                this.IV = iv;
            else
                this.IV = Array.Empty<byte>();

            this.Initialize();
        }

        private unsafe void Initialize()
        {
            this.EncryptionKeys = new byte[this.publicKeys.Length][];
            int[] keyLengths = new int[this.publicKeys.Length];

            IntPtr[] ek = new IntPtr[this.publicKeys.Length];
            IntPtr[] keyHandles = new IntPtr[this.publicKeys.Length];

            for (int i = 0; i < this.publicKeys.Length; i++)
            {
                keyHandles[i] = (this.publicKeys[i] as Key).KeyHandle.DangerousGetHandle();
                ek[i] = Marshal.AllocHGlobal(this.CryptoWrapper.EVP_PKEY_size((this.publicKeys[i] as Key).KeyHandle));
            }

            Span<int> lenSpan = new Span<int>(keyLengths);
            try
            {
                if (this.ivLength > 0 && !(this.IV is null))
                {
                    Span<byte> ivSpan = new Span<byte>(this.IV);
                    this.CryptoWrapper.EVP_SealInit(this.CipherContextHandle, this.CipherHandle,
                        ek,
                        lenSpan.GetPinnableReference(),
                        ivSpan.GetPinnableReference(),
                        keyHandles,
                        this.publicKeys.Length);
                }
                else
                    this.CryptoWrapper.EVP_SealInit(this.CipherContextHandle, this.CipherHandle,
                        ek,
                        lenSpan.GetPinnableReference(),
                        IntPtr.Zero,
                        keyHandles,
                        this.publicKeys.Length);

                byte[] newArr;
                for (int i = 0; i < this.publicKeys.Length; i++)
                {
                    newArr = new byte[keyLengths[i]];
                    Marshal.Copy(ek[i], newArr, 0, newArr.Length);
                    this.EncryptionKeys[i] = newArr;
                }
            }
            finally
            {
                for (int i = 0; i < ek.Length; i++)
                    Marshal.FreeHGlobal(ek[i]);
            }
        }

        protected override int UpdateInternal(in Span<byte> inputBuffer, ref Span<byte> outputBuffer)
        {
            this.CryptoWrapper.EVP_EncryptUpdate(this.CipherContextHandle, ref outputBuffer.GetPinnableReference(), out int outl, inputBuffer.GetPinnableReference(), inputBuffer.Length);
            return outl;
        }

        protected override int FinalizeInternal(ref Span<byte> outputBuffer)
        {
            this.CryptoWrapper.EVP_SealFinal(this.CipherContextHandle, ref outputBuffer.GetPinnableReference(), out int outl);
            return outl;
        }
    }
}

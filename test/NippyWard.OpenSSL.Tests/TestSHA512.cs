﻿// Copyright (c) 2006-2007 Frank Laub
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
// 1. Redistributions of source code must retain the above copyright
//    notice, this list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright
//    notice, this list of conditions and the following disclaimer in the
//    documentation and/or other materials provided with the distribution.
// 3. The name of the author may not be used to endorse or promote products
//    derived from this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
// IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
// IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
// NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
// THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Text;

using Xunit;
using Xunit.Abstractions;

using NippyWard.OpenSSL.ASN1;
using NippyWard.OpenSSL.Digests;

namespace NippyWard.OpenSSL.Tests
{
	public class TestSHA512 : TestBase
	{
		private static readonly byte[][] _App = {
			new byte[] {
				0xdd,0xaf,0x35,0xa1,0x93,0x61,0x7a,0xba,
				0xcc,0x41,0x73,0x49,0xae,0x20,0x41,0x31,
				0x12,0xe6,0xfa,0x4e,0x89,0xa9,0x7e,0xa2,
				0x0a,0x9e,0xee,0xe6,0x4b,0x55,0xd3,0x9a,
				0x21,0x92,0x99,0x2a,0x27,0x4f,0xc1,0xa8,
				0x36,0xba,0x3c,0x23,0xa3,0xfe,0xeb,0xbd,
				0x45,0x4d,0x44,0x23,0x64,0x3c,0xe8,0x0e,
				0x2a,0x9a,0xc9,0x4f,0xa5,0x4c,0xa4,0x9f
			},
			new byte[] {
				0x8e,0x95,0x9b,0x75,0xda,0xe3,0x13,0xda,
				0x8c,0xf4,0xf7,0x28,0x14,0xfc,0x14,0x3f,
				0x8f,0x77,0x79,0xc6,0xeb,0x9f,0x7f,0xa1,
				0x72,0x99,0xae,0xad,0xb6,0x88,0x90,0x18,
				0x50,0x1d,0x28,0x9e,0x49,0x00,0xf7,0xe4,
				0x33,0x1b,0x99,0xde,0xc4,0xb5,0x43,0x3a,
				0xc7,0xd3,0x29,0xee,0xb6,0xdd,0x26,0x54,
				0x5e,0x96,0xe5,0x5b,0x87,0x4b,0xe9,0x09
			},
			new byte[] {
				0xe7,0x18,0x48,0x3d,0x0c,0xe7,0x69,0x64,
				0x4e,0x2e,0x42,0xc7,0xbc,0x15,0xb4,0x63,
				0x8e,0x1f,0x98,0xb1,0x3b,0x20,0x44,0x28,
				0x56,0x32,0xa8,0x03,0xaf,0xa9,0x73,0xeb,
				0xde,0x0f,0xf2,0x44,0x87,0x7e,0xa6,0x0a,
				0x4c,0xb0,0x43,0x2c,0xe5,0x77,0xc3,0x1b,
				0xeb,0x00,0x9c,0x5c,0x2c,0x49,0xaa,0x2e,
				0x4e,0xad,0xb2,0x17,0xad,0x8c,0xc0,0x9b
			},
		};

        private static readonly byte[][] _Addenum = {
			new byte[] {
				0xcb,0x00,0x75,0x3f,0x45,0xa3,0x5e,0x8b,
				0xb5,0xa0,0x3d,0x69,0x9a,0xc6,0x50,0x07,
				0x27,0x2c,0x32,0xab,0x0e,0xde,0xd1,0x63,
				0x1a,0x8b,0x60,0x5a,0x43,0xff,0x5b,0xed,
				0x80,0x86,0x07,0x2b,0xa1,0xe7,0xcc,0x23,
				0x58,0xba,0xec,0xa1,0x34,0xc8,0x25,0xa7
			},
			new byte[] {
				0x09,0x33,0x0c,0x33,0xf7,0x11,0x47,0xe8,
				0x3d,0x19,0x2f,0xc7,0x82,0xcd,0x1b,0x47,
				0x53,0x11,0x1b,0x17,0x3b,0x3b,0x05,0xd2,
				0x2f,0xa0,0x80,0x86,0xe3,0xb0,0xf7,0x12,
				0xfc,0xc7,0xc7,0x1a,0x55,0x7e,0x2d,0xb9,
				0x66,0xc3,0xe9,0xfa,0x91,0x74,0x60,0x39
			},
			new byte[] {
				0x9d,0x0e,0x18,0x09,0x71,0x64,0x74,0xcb,
				0x08,0x6e,0x83,0x4e,0x31,0x0a,0x4a,0x1c,
				0xed,0x14,0x9e,0x9c,0x00,0xf2,0x48,0x52,
				0x79,0x72,0xce,0xc5,0x70,0x4c,0x2a,0x5b,
				0x07,0xb8,0xb3,0xdc,0x38,0xec,0xc4,0xeb,
				0xae,0x97,0xdd,0xd8,0x7f,0x3d,0x89,0x85
			},
		};

        public static IEnumerable<object[]> GetDigestVerification =>
            new List<object[]>
            {
                        new object[]{ DigestType.SHA512, _App, 288 },
                        new object[]{ DigestType.SHA384, _Addenum, 64 }
            };

        public TestSHA512(ITestOutputHelper outputHelper)
            : base(outputHelper) { }

        protected override void Dispose(bool disposing) { }

        [Theory]
        [MemberData(nameof(GetDigestVerification))]
#pragma warning disable xUnit1026, IDE0060 // Theory methods should use all of their parameters
        public void TestSingleUpdate(DigestType digestType, byte[][] results, int alen)
        {
            string str1, str2;
            using (Digest ctx = new Digest(digestType))
            {
                ctx.Update(new Span<byte>(Encoding.ASCII.GetBytes("abc")));
                ctx.Finalize(out Span<byte> digestSpan);

                byte[] digest = digestSpan.ToArray();
                str1 = BitConverter.ToString(digest);
                str2 = BitConverter.ToString(results[0]);
            }

            Assert.Equal(str2, str1);
        }

        [Theory]
        [MemberData(nameof(GetDigestVerification))]
        public void TestSingleUpdate_2(DigestType digestType, byte[][] results, int alen)
#pragma warning restore xUnit1026, IDE0060 // Remove unused parameter
        {
            string str1, str2;
            using (Digest ctx = new Digest(digestType))
            {
                ctx.Update(new Span<byte>(Encoding.ASCII.GetBytes(
                    "abcdefgh" + "bcdefghi" + "cdefghij" + "defghijk" +
                    "efghijkl" + "fghijklm" + "ghijklmn" + "hijklmno" +
                    "ijklmnop" + "jklmnopq" + "klmnopqr" + "lmnopqrs" +
                    "mnopqrst" + "nopqrstu")));
                ctx.Finalize(out Span<byte> digestSpan);

                byte[] digest = digestSpan.ToArray();
                str1 = BitConverter.ToString(digest);
                str2 = BitConverter.ToString(results[1]);
            }

            Assert.Equal(str2, str1);
        }

        [Theory]
        [MemberData(nameof(GetDigestVerification))]
        public void TestMultipleUpdate(DigestType digestType, byte[][] results, int alen)
        {
            byte[] msg = Encoding.ASCII.GetBytes(new string('a', alen));
            string str1, str2;

            using (Digest ctx = new Digest(digestType))
            {
                int len;
                byte[] tmp;
                for (int i = 0; i < 1000000; i += alen)
                {
                    len = (1000000 - i) < alen ? 1000000 - i : alen;
                    tmp = new byte[len];
                    System.Buffer.BlockCopy(msg, 0, tmp, 0, len);
                    ctx.Update(tmp);
                }
                ctx.Finalize(out Span<byte> digestSpan);

                byte[] digest = digestSpan.ToArray();
                str1 = BitConverter.ToString(digest);
                str2 = BitConverter.ToString(results[2]);
            }

            Assert.Equal(str2, str1);
        }
	}
}

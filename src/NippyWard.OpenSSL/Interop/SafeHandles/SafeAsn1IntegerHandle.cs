﻿// Copyright (c) 2009 Frank Laub
// All rights reserved.

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
using System.Runtime.InteropServices;
using NippyWard.OpenSSL.Interop.Wrappers;

namespace NippyWard.OpenSSL.Interop.SafeHandles
{
	internal abstract class SafeAsn1IntegerHandle : SafeAsn1StringHandle
    {
        public new static SafeAsn1IntegerHandle Zero
            => Native.SafeHandleFactory.CreateWrapperSafeHandle<SafeAsn1IntegerHandle>(IntPtr.Zero);

        internal override OPENSSL_sk_freefunc FreeFunc => _FreeFunc;

        private static readonly OPENSSL_sk_freefunc _FreeFunc;

        static SafeAsn1IntegerHandle()
        {
            _FreeFunc = new OPENSSL_sk_freefunc(CryptoWrapper.ASN1_INTEGER_free);
        }

        internal SafeAsn1IntegerHandle(bool takeOwnership)
            : base(takeOwnership)
        { }

        internal SafeAsn1IntegerHandle(IntPtr ptr, bool takeOwnership)
            : base(ptr, takeOwnership)
        { }

		public int Value
		{
			get { return CryptoWrapper.ASN1_INTEGER_get(this); }
			set { CryptoWrapper.ASN1_INTEGER_set(this, value); }
		}
    }
}

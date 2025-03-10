// Copyright (c) 2012 Frank Laub
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
using System.Linq;
using System.Runtime.InteropServices;

using NippyWard.OpenSSL.Interop;
using NippyWard.OpenSSL.ASN1;

namespace NippyWard.OpenSSL.Interop.SafeHandles.Crypto.EC
{
	/// <summary>
	/// Wraps EC_builtin_curve
	/// </summary>
	internal class BuiltinCurves
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct EC_builtin_curve
		{
			public int nid;
			public IntPtr comment;
		}

        private readonly List<string> _list;
        private readonly List<ECCurveType> _curves;

        public List<string> Result { get { return _list; } }

        public BuiltinCurves()
        {
            this._curves = this.Get();
            this._list = this._curves.Select(x => x.ShortName).ToList();
        }

        /// <summary>
        /// Calls EC_get_builtin_curves()
        /// </summary>
        /// <returns></returns>
        private List<ECCurveType> Get()
		{
            nuint count = Native.CryptoWrapper.EC_get_builtin_curves(IntPtr.Zero, 0);
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<EC_builtin_curve>() * (int)count);
            List<ECCurveType> curves = new List<ECCurveType>();

            try
            {
                Native.CryptoWrapper.EC_get_builtin_curves(ptr, count);
                var pItem = ptr;

                for (int i = 0; i < (int)count; i++)
                {
                    var raw = Marshal.PtrToStructure<EC_builtin_curve>(pItem);
                    _curves.Add(new ECCurveType(Marshal.ReadInt32(pItem)));
                    pItem = new IntPtr(pItem.ToInt64() + Marshal.SizeOf<EC_builtin_curve>());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return curves;
        }
	}
}


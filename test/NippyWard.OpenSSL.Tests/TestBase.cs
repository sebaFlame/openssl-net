﻿// Copyright (c) 2009 Frank Laub
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
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Xunit;
using Xunit.Abstractions;

using NippyWard.OpenSSL.Interop;
using NippyWard.OpenSSL.Error;
using NippyWard.OpenSSL.SSL;

namespace NippyWard.OpenSSL.Tests
{
	public abstract class TestBase : IDisposable
	{
        protected readonly ITestOutputHelper OutputHelper;

        protected TestBase(ITestOutputHelper outputHelper)
        {
            this.OutputHelper = outputHelper;
#if ENABLE_MEMORYTRACKER
            MemoryTracker.Start();
#endif
        }

        protected abstract void Dispose(bool disposing);

		public void Dispose()
		{
            this.Dispose(true);

            List<string> errors = OpenSslError.GetErrors();
            foreach (var err in errors)
            {
                this.OutputHelper.WriteLine("ERROR: {0}", err);
            }
            Assert.Empty(errors);

#if ENABLE_MEMORYTRACKER
            //de-allocate per thread allocations for the current thread
            //ensure no async code gets run!!!
            Native.CryptoWrapper.OPENSSL_thread_stop();

            List<MemoryProblem> lstMemoryProblem = MemoryTracker.Finish();

            Assert.Empty(lstMemoryProblem);
#endif
        }
	}
}

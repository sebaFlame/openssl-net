﻿using System;
using System.Collections.Generic;
using System.Text;

using OpenSSL.Core.Interop;
using OpenSSL.Core.X509;

namespace OpenSSL.Core.Error
{
    public class VerifyError : BaseOpenSslError
    {
        private string message;

        public VerifyError(VerifyResult verifyResult)
            : base((ulong)verifyResult) { }

        public override string Message => message ?? (message = Native.PtrToStringAnsi(Native.CryptoWrapper.X509_verify_cert_error_string((int)this.ErrorCode), false));
    }
}

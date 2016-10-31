//   OData .NET Libraries ver. 5.6.3
//   Copyright (c) Microsoft Corporation
//   All rights reserved. 
//   MIT License
//   Permission is hereby granted, free of charge, to any person obtaining a copy of
//   this software and associated documentation files (the "Software"), to deal in
//   the Software without restriction, including without limitation the rights to use,
//   copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the
//   Software, and to permit persons to whom the Software is furnished to do so,
//   subject to the following conditions:

//   The above copyright notice and this permission notice shall be included in all
//   copies or substantial portions of the Software.

//   THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
//   FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
//   COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
//   IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
//   CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Collections.Generic;

namespace Microsoft.Data.Edm.Csdl.Internal.Parsing.Ast
{
    /// <summary>
    /// Represents a CSDL enumeration type.
    /// </summary>
    internal class CsdlEnumType : CsdlNamedElement
    {
        private readonly string underlyingTypeName;
        private readonly bool isFlags;
        private readonly List<CsdlEnumMember> members;

        public CsdlEnumType(string name, string underlyingTypeName, bool isFlags, IEnumerable<CsdlEnumMember> members, CsdlDocumentation documentation, CsdlLocation location)
            : base(name, documentation, location)
        {
            this.underlyingTypeName = underlyingTypeName;
            this.isFlags = isFlags;
            this.members = new List<CsdlEnumMember>(members);
        }

        public string UnderlyingTypeName
        {
            get { return this.underlyingTypeName; }
        }

        public bool IsFlags
        {
            get { return this.isFlags; }
        }

        public IEnumerable<CsdlEnumMember> Members
        {
            get { return this.members; }
        }
    }
}

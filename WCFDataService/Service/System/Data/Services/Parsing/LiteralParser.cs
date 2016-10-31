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

#if ODATALIB
namespace Microsoft.Data.OData.Query
#else
namespace System.Data.Services.Parsing
#endif
{
    using System.Collections.Generic;
#if !ODATALIB
    using System.Data.Linq;
#endif
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Xml;
    using System.Globalization;
#if !ODATALIB
    using System.Xml.Linq;
    using Microsoft.Data.OData;

#else
    using WebConvert = UriPrimitiveTypeParser;
    using XmlConstants = ExpressionConstants;
#endif

    /// <summary>Use this class to parse literals from keys, etags, skiptokens, and filter/orderby expression constants.</summary>
    internal abstract class LiteralParser
    {
        /// <summary>
        /// Default singleton instance of the literal parser.
        /// </summary>
        private static readonly LiteralParser DefaultInstance = new DefaultLiteralParser();
        
#if !ODATALIB
        /// <summary>
        /// Singleton instance of the literal parser to use for filters and operation parameters.
        /// </summary>
        private static readonly LiteralParser ExpressionInstance = new LiteralParserWithSpatialSupport();
#endif

        /// <summary>
        /// Singleton instance of the literal parser for when keys-as-segments is turned on, which does not wrap the formatted strings in any quotes or type-markers.
        /// </summary>
        private static readonly LiteralParser KeysAsSegmentsInstance = new KeysAsSegmentsLiteralParser();

        /// <summary>
        /// Mapping between primitive CLR types and lightweight parser classes for that type.
        /// </summary>
        private static readonly IDictionary<Type, PrimitiveParser> Parsers = new Dictionary<Type, PrimitiveParser>(ReferenceEqualityComparer<Type>.Instance)
        {
            // Type-specific parsers.
            { typeof(byte[]), new BinaryPrimitiveParser() },
            { typeof(String), new StringPrimitiveParser() },
            { typeof(Decimal), new DecimalPrimitiveParser() },

            // Types without single-quotes or type markers
            { typeof(Boolean), DelegatingPrimitiveParser<bool>.WithoutMarkup(XmlConvert.ToBoolean) },
            { typeof(Byte), DelegatingPrimitiveParser<byte>.WithoutMarkup(XmlConvert.ToByte) },
            { typeof(SByte), DelegatingPrimitiveParser<sbyte>.WithoutMarkup(XmlConvert.ToSByte) },
            { typeof(Int16), DelegatingPrimitiveParser<short>.WithoutMarkup(XmlConvert.ToInt16) },
            { typeof(Int32), DelegatingPrimitiveParser<int>.WithoutMarkup(XmlConvert.ToInt32) },

            // Types with prefixes and single-quotes.
            { typeof(Guid), DelegatingPrimitiveParser<Guid>.WithPrefix(XmlConvert.ToGuid, XmlConstants.LiteralPrefixGuid) },
            { typeof(DateTime), DelegatingPrimitiveParser<DateTime>.WithPrefix(s => XmlConvert.ToDateTime(s, XmlDateTimeSerializationMode.RoundtripKind), XmlConstants.LiteralPrefixDateTime) },
            { typeof(DateTimeOffset), DelegatingPrimitiveParser<DateTimeOffset>.WithPrefix(XmlConvert.ToDateTimeOffset, XmlConstants.LiteralPrefixDateTimeOffset) },
            { typeof(TimeSpan), DelegatingPrimitiveParser<TimeSpan>.WithPrefix(XmlConvert.ToTimeSpan, XmlConstants.LiteralPrefixTime) },

            // Types with suffixes.
            { typeof(Int64), DelegatingPrimitiveParser<long>.WithSuffix(XmlConvert.ToInt64, XmlConstants.LiteralSuffixInt64) },
            { typeof(Single), DelegatingPrimitiveParser<float>.WithSuffix(XmlConvert.ToSingle, XmlConstants.LiteralSuffixSingle) },
            { typeof(Double), DelegatingPrimitiveParser<double>.WithSuffix(XmlConvert.ToDouble, XmlConstants.LiteralSuffixDouble, /*required*/ false) },
            
#if !ODATALIB
            // Special types.
            { typeof(XElement), DelegatingPrimitiveParser<XElement>.WithoutMarkup(t => XElement.Parse(t, LoadOptions.PreserveWhitespace)) },
#endif
        };

#if !ODATALIB
        /// <summary>
        /// Gets the literal parser to use for constants in filter/orderby expressions and operation parameters.
        /// </summary>
        internal static LiteralParser ForExpressions
        {
            get
            {
                DebugUtils.CheckNoExternalCallers();
                return ExpressionInstance;
            }
        }
#endif

        /// <summary>
        /// Gets the literal parser to use for ETags.
        /// </summary>
        internal static LiteralParser ForETags
        {
            get
            {
                DebugUtils.CheckNoExternalCallers();
                return DefaultInstance;
            }
        }

        /// <summary>
        /// Gets the literal parser for keys, based on whether the keys are formatted as segments.
        /// </summary>
        /// <param name="keyAsSegment">Whether or not the keys is formatted as a segment.</param>
        /// <returns>The literal parser to use.</returns>
        internal static LiteralParser ForKeys(bool keyAsSegment)
        {
            DebugUtils.CheckNoExternalCallers();
            return keyAsSegment ? KeysAsSegmentsInstance : DefaultInstance;
        }

        /// <summary>Converts a string to a primitive value.</summary>
        /// <param name="targetType">Type to convert string to.</param>
        /// <param name="text">String text to convert.</param>
        /// <param name="result">After invocation, converted value.</param>
        /// <returns>true if the value was converted; false otherwise.</returns>
        internal abstract bool TryParseLiteral(Type targetType, string text, out object result);

        /// <summary>
        /// Default literal parser which has type-markers and single-quotes. Also supports arbitrary literals being re-encoded in binary form.
        /// </summary>
#if ODATALIB
        private sealed class DefaultLiteralParser : LiteralParser
#else
        private class DefaultLiteralParser : LiteralParser
#endif
        {
            /// <summary>Converts a string to a primitive value.</summary>
            /// <param name="targetType">Type to convert string to.</param>
            /// <param name="text">String text to convert.</param>
            /// <param name="result">After invocation, converted value.</param>
            /// <returns>true if the value was converted; false otherwise.</returns>
            internal override bool TryParseLiteral(Type targetType, string text, out object result)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(text != null, "text != null");
                Debug.Assert(targetType != null, "expectedType != null");
                Debug.Assert(!targetType.IsSpatial(), "Not supported for spatial types, as they cannot be part of a key, etag, or skiptoken");

                targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                bool binaryResult = TryRemoveFormattingAndConvert(text, typeof(byte[]), out result);
                if (binaryResult)
                {
                    byte[] byteArrayValue = (byte[])result;
                    if (targetType == typeof(byte[]))
                    {
                        result = byteArrayValue;
                        return true;
                    }

#if !ODATALIB
                    if (targetType == typeof(Binary))
                    {
                        result = new Binary(byteArrayValue);
                        return true;
                    }
#endif

                    // we allow arbitary values to be encoded as a base 64 array, so we may have 
                    // found a binary value in place of another type. If so, convert it to a UTF-8
                    // string and interpret it normally.
                    string keyValue = Encoding.UTF8.GetString(byteArrayValue, 0, byteArrayValue.Length);
                    return TryRemoveFormattingAndConvert(keyValue, targetType, out result);
                }

#if ODATALIB
                if (targetType == typeof(byte[]))
#else
                if (targetType == typeof(byte[]) || targetType == typeof(Binary))
#endif
                {
                    // if we got here, then the value was not binary.
                    result = null;
                    return false;
                }

                return TryRemoveFormattingAndConvert(text, targetType, out result);
            }

            /// <summary>
            /// Tries to parse the literal by first removing required formatting for the expected type, then converting the resulting string.
            /// </summary>
            /// <param name="text">String text to convert.</param>
            /// <param name="targetType">Type to convert string to.</param>
            /// <param name="targetValue">After invocation, converted value.</param>
            /// <returns>true if the value was converted; false otherwise.</returns>
            private static bool TryRemoveFormattingAndConvert(string text, Type targetType, out object targetValue)
            {
                Debug.Assert(text != null, "text != null");
                Debug.Assert(targetType != null, "expectedType != null");
                Debug.Assert(!targetType.IsSpatial(), "Not supported for spatial types, as they cannot be part of a key, etag, or skiptoken");

                Debug.Assert(Parsers.ContainsKey(targetType), "Unexpected type: " + targetType);
                PrimitiveParser parser = Parsers[targetType];
                if (!parser.TryRemoveFormatting(ref text))
                {
                    targetValue = null;
                    return false;
                }

                return parser.TryConvert(text, out targetValue);
            }
        }

#if !ODATALIB
        /// <summary>
        /// Literal parser which supports spatial values, but is otherwise equivalent to the default parser.
        /// </summary>
        private sealed class LiteralParserWithSpatialSupport : DefaultLiteralParser
        {
            /// <summary>Converts a string to a primitive value.</summary>
            /// <param name="targetType">Type to convert string to.</param>
            /// <param name="text">String text to convert.</param>
            /// <param name="result">After invocation, converted value.</param>
            /// <returns>true if the value was converted; false otherwise.</returns>
            internal override bool TryParseLiteral(Type targetType, string text, out object result)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(targetType != null, "expectedType != null");
                if (targetType.IsSpatial())
                {
                    return WellKnownTextParser.TryParseSpatialLiteral(targetType, text, out result);
                }

                return base.TryParseLiteral(targetType, text, out result);
            }
        }
#endif

        /// <summary>
        /// Simplified literal parser for keys-as-segments which does not expect type-markers, single-quotes, etc. Does not support re-encoding literals as binary.
        /// </summary>
        private sealed class KeysAsSegmentsLiteralParser : LiteralParser
        {
            /// <summary>Converts a string to a primitive value.</summary>
            /// <param name="targetType">Type to convert string to.</param>
            /// <param name="text">String text to convert.</param>
            /// <param name="result">After invocation, converted value.</param>
            /// <returns>true if the value was converted; false otherwise.</returns>
            internal override bool TryParseLiteral(Type targetType, string text, out object result)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(text != null, "text != null");
                Debug.Assert(targetType != null, "expectedType != null");

                text = UnescapeLeadingDollarSign(text);

                targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                Debug.Assert(!targetType.IsSpatial(), "Not supported for spatial types, as they cannot be part of a key, etag, or skiptoken");

#if !ODATALIB
                if (targetType == typeof(Binary))
                {
                    if (Parsers[typeof(byte[])].TryConvert(text, out result))
                    {
                        byte[] byteArrayValue = (byte[])result;
                        result = new Binary(byteArrayValue);
                        return true;
                    }
                }
#endif

                Debug.Assert(Parsers.ContainsKey(targetType), "Unexpected type: " + targetType);
                return Parsers[targetType].TryConvert(text, out result);
            }

            /// <summary>
            /// If the string starts with '$', removes it.
            /// Also asserts that the 2nd character is also '$', as otherwise the string would be treated as a system segment.
            /// </summary>
            /// <param name="text">The text.</param>
            /// <returns>The string value with a leading '$' removed, if the string started with one.</returns>
            private static string UnescapeLeadingDollarSign(string text)
            {
                Debug.Assert(text != null, "text != null");
                if (text.Length > 1 && text[0] == '$')
                {
                    Debug.Assert(text.Length > 0 && text[1] == '$', "2nd character should also be '$', otherwise it would have been treated as a system segment.");
                    text = text.Substring(1);
                }

                return text;
            }
        }

        /// <summary>
        /// Helper class for parsing a specific type of primitive literal.
        /// </summary>
        private abstract class PrimitiveParser
        {
            /// <summary>XML whitespace characters to trim around literals.</summary>
            private static readonly char[] XmlWhitespaceChars = new char[] { ' ', '\t', '\n', '\r' };

            /// <summary>
            /// The expected prefix for the literal. Null indicates no prefix is expected.
            /// </summary>
            private readonly string prefix;

            /// <summary>
            /// The expected suffix for the literal. Null indicates that no suffix is expected.
            /// </summary>
            private readonly string suffix;

            /// <summary>
            /// Whether or not the suffix is required.
            /// </summary>
            private readonly bool suffixRequired;

            /// <summary>
            /// The expected type for this parser.
            /// </summary>
            private readonly Type expectedType;

            /// <summary>
            /// Initializes a new instance of the <see cref="PrimitiveParser"/> class.
            /// </summary>
            /// <param name="expectedType">The expected type for this parser.</param>
            /// <param name="suffix">The expected suffix for the literal. Null indicates that no suffix is expected.</param>
            /// <param name="suffixRequired">Whether or not the suffix is required.</param>
            protected PrimitiveParser(Type expectedType, string suffix, bool suffixRequired)
                : this(expectedType)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(suffix != null, "suffix != null");
                this.prefix = null;
                this.suffix = suffix;
                this.suffixRequired = suffixRequired;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="PrimitiveParser"/> class.
            /// </summary>
            /// <param name="expectedType">The expected type for this parser.</param>
            /// <param name="prefix">The expected prefix for the literal.</param>
            protected PrimitiveParser(Type expectedType, string prefix)
                : this(expectedType)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(prefix != null, "prefix != null");
                this.prefix = prefix;
                this.suffix = null;
                this.suffixRequired = false;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="PrimitiveParser"/> class.
            /// </summary>
            /// <param name="expectedType">The expected type for this parser.</param>
            protected PrimitiveParser(Type expectedType)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(expectedType != null, "expectedType != null");
                this.expectedType = expectedType;
            }

            /// <summary>
            /// Tries to convert the given text into this parser's expected type. Conversion only, formatting should already have been removed.
            /// </summary>
            /// <param name="text">The text to convert.</param>
            /// <param name="targetValue">The target value.</param>
            /// <returns>Whether or not conversion was successful.</returns>
            internal abstract bool TryConvert(string text, out object targetValue);

            /// <summary>
            /// Tries to remove formatting specific to this parser's expected type.
            /// </summary>
            /// <param name="text">The text to remove formatting from.</param>
            /// <returns>Whether or not the expected formatting was found and succesfully removed.</returns>
            internal virtual bool TryRemoveFormatting(ref string text)
            {
                DebugUtils.CheckNoExternalCallers();
                if (this.prefix != null)
                {
                    if (!WebConvert.TryRemovePrefix(this.prefix, ref text))
                    {
                        return false;
                    }
                }

                bool shouldBeQuoted = this.prefix != null || ValueOfTypeCanContainQuotes(this.expectedType);
                if (shouldBeQuoted && !WebConvert.TryRemoveQuotes(ref text))
                {
                    return false;
                }

                if (this.suffix != null)
                {
                    // we need to try to remove the literal even if it isn't required.
                    if (!TryRemoveLiteralSuffix(this.suffix, ref text) && this.suffixRequired)
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Determines whether the values for the specified types should be 
            /// quoted in URI keys.
            /// </summary>
            /// <param name='type'>Type to check.</param>
            /// <returns>
            /// true if values of <paramref name='type' /> require quotes; false otherwise.
            /// </returns>
            private static bool ValueOfTypeCanContainQuotes(Type type)
            {
                Debug.Assert(type != null, "type != null");
#if ODATALIB
                return type == typeof(string);
#else
                return type == typeof(XElement) || type == typeof(string);
#endif
            }

            /// <summary>
            /// Check and strip the input <paramref name="text"/> for literal <paramref name="suffix"/>
            /// </summary>
            /// <param name="suffix">The suffix value</param>
            /// <param name="text">The string to check</param>
            /// <returns>A string that has been striped of the suffix</returns>
            private static bool TryRemoveLiteralSuffix(string suffix, ref string text)
            {
                Debug.Assert(text != null, "text != null");
                Debug.Assert(suffix != null, "suffix != null");

                text = text.Trim(XmlWhitespaceChars);
                if (text.Length <= suffix.Length || !text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                text = text.Substring(0, text.Length - suffix.Length);
                return true;
            }
        }

        /// <summary>
        /// Primitive parser which uses a delegate for conversion.
        /// </summary>
        /// <typeparam name="T">The expected CLR type when parsing.</typeparam>
        private class DelegatingPrimitiveParser<T> : PrimitiveParser
        {
            /// <summary>
            /// The delegate to use for conversion.
            /// </summary>
            private readonly Func<string, T> convertMethod;

            /// <summary>
            /// Initializes a new instance of the <see cref="DelegatingPrimitiveParser&lt;T&gt;"/> class.
            /// </summary>
            /// <param name="convertMethod">The delegate to use for conversion.</param>
            /// <param name="suffix">The expected suffix for the literal. Null indicates that no suffix is expected.</param>
            /// <param name="suffixRequired">Whether or not the suffix is required.</param>
            protected DelegatingPrimitiveParser(Func<string, T> convertMethod, string suffix, bool suffixRequired)
                : base(typeof(T), suffix, suffixRequired)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(convertMethod != null, "convertMethod != null");
                this.convertMethod = convertMethod;
            }

            /// <summary>
            /// Prevents a default instance of the <see cref="DelegatingPrimitiveParser&lt;T&gt;"/> class from being created.
            /// </summary>
            /// <param name="convertMethod">The delegate to use for conversion.</param>
            private DelegatingPrimitiveParser(Func<string, T> convertMethod) 
                : base(typeof(T))
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(convertMethod != null, "convertMethod != null");
                this.convertMethod = convertMethod;
            }

            /// <summary>
            /// Prevents a default instance of the <see cref="DelegatingPrimitiveParser&lt;T&gt;"/> class from being created.
            /// </summary>
            /// <param name="convertMethod">The delegate to use for conversion.</param>
            /// <param name="prefix">The expected prefix for the literal.</param>
            private DelegatingPrimitiveParser(Func<string, T> convertMethod, string prefix)
                : base(typeof(T), prefix)
            {
                Debug.Assert(convertMethod != null, "convertMethod != null");
                this.convertMethod = convertMethod;
            }

            /// <summary>
            /// Creates a primitive parser which wraps the given delegate and does not expect any extra markup in serialized literal.
            /// </summary>
            /// <param name="convertMethod">The delegate to use for conversion.</param>
            /// <returns>A new primitive parser.</returns>
            internal static DelegatingPrimitiveParser<T> WithoutMarkup(Func<string, T> convertMethod)
            {
                DebugUtils.CheckNoExternalCallers();
                return new DelegatingPrimitiveParser<T>(convertMethod);
            }

            /// <summary>
            /// Creates a primitive parser which wraps the given delegate and expects serialized literals to start with one of the given prefixes.
            /// </summary>
            /// <param name="convertMethod">The delegate to use for conversion.</param>
            /// <param name="prefix">The expected prefix for the literal.</param>
            /// <returns>A new primitive parser.</returns>
            internal static DelegatingPrimitiveParser<T> WithPrefix(Func<string, T> convertMethod, string prefix)
            {
                DebugUtils.CheckNoExternalCallers();
                return new DelegatingPrimitiveParser<T>(convertMethod, prefix);
            }

            /// <summary>
            /// Creates a primitive parser which wraps the given delegate and expects serialized literals to end with the given suffix.
            /// </summary>
            /// <param name="convertMethod">The delegate to use for conversion.</param>
            /// <param name="suffix">The expected suffix for the literal. Null indicates that no suffix is expected.</param>
            /// <returns>A new primitive parser.</returns>
            internal static DelegatingPrimitiveParser<T> WithSuffix(Func<string, T> convertMethod, string suffix)
            {
                DebugUtils.CheckNoExternalCallers();
                return WithSuffix(convertMethod, suffix, /*required*/ true);
            }

            /// <summary>
            /// Creates a primitive parser which wraps the given delegate and expects serialized literals to end with the given suffix.
            /// </summary>
            /// <param name="convertMethod">The delegate to use for conversion.</param>
            /// <param name="suffix">The expected suffix for the literal. Null indicates that no suffix is expected.</param>
            /// <param name="required">Whether or not the suffix is required.</param>
            /// <returns>A new primitive parser.</returns>
            internal static DelegatingPrimitiveParser<T> WithSuffix(Func<string, T> convertMethod, string suffix, bool required)
            {
                DebugUtils.CheckNoExternalCallers();
                return new DelegatingPrimitiveParser<T>(convertMethod, suffix, required);
            }

            /// <summary>
            /// Tries to convert the given text into this parser's expected type. Conversion only, formatting should already have been removed.
            /// </summary>
            /// <param name="text">The text to convert.</param>
            /// <param name="targetValue">The target value.</param>
            /// <returns>
            /// Whether or not conversion was successful.
            /// </returns>
            internal override bool TryConvert(string text, out object targetValue)
            {
                DebugUtils.CheckNoExternalCallers();
                try
                {   
                    targetValue = this.convertMethod(text);
                    return true;
                }
                catch (FormatException)
                {
                    targetValue = default(T);
                    return false;
                }
            }
        }

        /// <summary>
        /// Parser specific to the Edm.Decimal type.
        /// </summary>
        private sealed class DecimalPrimitiveParser : DelegatingPrimitiveParser<decimal>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="DecimalPrimitiveParser"/> class.
            /// </summary>
            internal DecimalPrimitiveParser()
                : base(ConvertDecimal, XmlConstants.LiteralSuffixDecimal, true)
            {
                DebugUtils.CheckNoExternalCallers();
            }

            /// <summary>
            /// Special helper to convert a string to a decimal that will allow more than what XmlConvert.ToDecimal supports by default.
            /// </summary>
            /// <param name="text">The text to convert.</param>
            /// <returns>The converted decimal value.</returns>
            private static Decimal ConvertDecimal(string text)
            {
                try
                {
                    return XmlConvert.ToDecimal(text);
                }
                catch (FormatException)
                {
                    // we need to support exponential format for decimals since we used to support them in V1
                    decimal result;
                    if (Decimal.TryParse(text, NumberStyles.Float, NumberFormatInfo.InvariantInfo, out result))
                    {
                        return result;
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// Parser specific to the Edm.Binary type.
        /// </summary>
        private sealed class BinaryPrimitiveParser : PrimitiveParser
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="BinaryPrimitiveParser"/> class.
            /// </summary>
            internal BinaryPrimitiveParser()
                : base(typeof(byte[]))
            {
                DebugUtils.CheckNoExternalCallers();
            }

            /// <summary>
            /// Tries to convert the given text into this parser's expected type. Conversion only, formatting should already have been removed.
            /// </summary>
            /// <param name="text">The text to convert.</param>
            /// <param name="targetValue">The target value.</param>
            /// <returns>
            /// Whether or not conversion was successful.
            /// </returns>
            internal override bool TryConvert(string text, out object targetValue)
            {
                DebugUtils.CheckNoExternalCallers();

                // must be of even length.
                if ((text.Length % 2) != 0)
                {
                    targetValue = null;
                    return false;
                }

                byte[] result = new byte[text.Length / 2];
                int resultIndex = 0;
                int textIndex = 0;
                while (resultIndex < result.Length)
                {
                    char ch0 = text[textIndex];
                    char ch1 = text[textIndex + 1];
                    if (!WebConvert.IsCharHexDigit(ch0) || !WebConvert.IsCharHexDigit(ch1))
                    {
                        targetValue = null;
                        return false;
                    }

                    result[resultIndex] = (byte)((byte)(HexCharToNibble(ch0) << 4) + HexCharToNibble(ch1));
                    textIndex += 2;
                    resultIndex++;
                }

                targetValue = result;
                return true;
            }

            /// <summary>
            /// Tries to remove formatting specific to this parser's expected type.
            /// </summary>
            /// <param name="text">The text to remove formatting from.</param>
            /// <returns>
            /// Whether or not the expected formatting was found and succesfully removed.
            /// </returns>
            internal override bool TryRemoveFormatting(ref string text)
            {
                DebugUtils.CheckNoExternalCallers();
                if (!WebConvert.TryRemovePrefix(XmlConstants.LiteralPrefixBinary, ref text) 
                    && !WebConvert.TryRemovePrefix(XmlConstants.LiteralPrefixShortBinary, ref text))
                {
                    return false;
                }
                
                if (!WebConvert.TryRemoveQuotes(ref text))
                {
                    return false;
                }
                
                return true;
            }

            /// <summary>Returns the 4 bits that correspond to the specified character.</summary>
            /// <param name="c">Character in the 0-F range to be converted.</param>
            /// <returns>The 4 bits that correspond to the specified character.</returns>
            /// <exception cref="FormatException">Thrown when 'c' is not in the '0'-'9','a'-'f' range.</exception>
            private static byte HexCharToNibble(char c)
            {
                Debug.Assert(WebConvert.IsCharHexDigit(c), String.Format(CultureInfo.InvariantCulture, "{0} is not a hex digit.", c));
                switch (c)
                {
                    case '0':
                        return 0;
                    case '1':
                        return 1;
                    case '2':
                        return 2;
                    case '3':
                        return 3;
                    case '4':
                        return 4;
                    case '5':
                        return 5;
                    case '6':
                        return 6;
                    case '7':
                        return 7;
                    case '8':
                        return 8;
                    case '9':
                        return 9;
                    case 'a':
                    case 'A':
                        return 10;
                    case 'b':
                    case 'B':
                        return 11;
                    case 'c':
                    case 'C':
                        return 12;
                    case 'd':
                    case 'D':
                        return 13;
                    case 'e':
                    case 'E':
                        return 14;
                    case 'f':
                    case 'F':
                        return 15;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        /// <summary>
        /// Parser specific to the Edm.String type.
        /// </summary>
        private sealed class StringPrimitiveParser : PrimitiveParser
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="StringPrimitiveParser"/> class.
            /// </summary>
            public StringPrimitiveParser() 
                : base(typeof(string))
            {
                DebugUtils.CheckNoExternalCallers();
            }

            /// <summary>
            /// Tries to convert the given text into this parser's expected type. Conversion only, formatting should already have been removed.
            /// </summary>
            /// <param name="text">The text to convert.</param>
            /// <param name="targetValue">The target value.</param>
            /// <returns>
            /// Whether or not conversion was successful.
            /// </returns>
            internal override bool TryConvert(string text, out object targetValue)
            {
                DebugUtils.CheckNoExternalCallers();
                targetValue = text;
                return true;
            }

            /// <summary>
            /// Tries to remove formatting specific to this parser's expected type.
            /// </summary>
            /// <param name="text">The text to remove formatting from.</param>
            /// <returns>
            /// Whether or not the expected formatting was found and succesfully removed.
            /// </returns>
            internal override bool TryRemoveFormatting(ref string text)
            {
                DebugUtils.CheckNoExternalCallers();
                return WebConvert.TryRemoveQuotes(ref text);
            }
        }
    }
}

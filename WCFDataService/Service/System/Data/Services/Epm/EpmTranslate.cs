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

#if ASTORIA_SERVER
namespace System.Data.Services.Providers
{
#else
namespace System.Data.EntityModel.Emitters
{
    using System.Data.Services;
    using System.Data.Services.Design;
#endif
    using System.Collections.Generic;
    using System.Data.Services.Common;
    using System.Diagnostics;

    /// <summary>
    /// Helper class for translating Epm enum values to strings and strings to Epm enum values.
    /// </summary>
    internal static class EpmTranslate
    {
        /// <summary>SyndicationItemProperty enum to FC_TargetPath mapping.</summary>
        private static readonly string[] syndicationItemToTargetPath = new string[] 
        {
            String.Empty, // SyndicationItemProperty.Custom
            XmlConstants.SyndAuthorEmail,
            XmlConstants.SyndAuthorName,
            XmlConstants.SyndAuthorUri,
            XmlConstants.SyndContributorEmail,
            XmlConstants.SyndContributorName,
            XmlConstants.SyndContributorUri,
            XmlConstants.SyndUpdated,
            XmlConstants.SyndPublished,
            XmlConstants.SyndRights,
            XmlConstants.SyndSummary,
            XmlConstants.SyndTitle,
        };

        /// <summary>FC_TargetPath to SyndicationItemProperty enum mapping.</summary>
        private static readonly Dictionary<string, SyndicationItemProperty> targetPathToSyndicationItem;
      
        /// <summary>Initialize mappings</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline", Justification = "Cleaner code")]
        static EpmTranslate()
        {
            Debug.Assert(typeof(SyndicationItemProperty).GetEnumValues().Length == syndicationItemToTargetPath.Length, "Any addition to SyndicationItemPropery enum requires updating syndicationItemToTargetPath.");

            targetPathToSyndicationItem = new Dictionary<string, SyndicationItemProperty>(EqualityComparer<string>.Default);
            foreach (var syndicationItem in typeof(SyndicationItemProperty).GetEnumValues())
            {
                targetPathToSyndicationItem.Add(syndicationItemToTargetPath[(int)syndicationItem], (SyndicationItemProperty)syndicationItem);
            }
        }

#if ASTORIA_SERVER
        /// <summary>
        /// Translates syndication item property to string
        /// </summary>
        /// <param name="property">Syndication property to translate</param>
        /// <returns>TargetPath corresponding to SyndicationItemProperty</returns>
        internal static string MapSyndicationPropertyToEpmTargetPath(SyndicationItemProperty property)
        {
            Debug.Assert(property >= SyndicationItemProperty.CustomProperty && property <= SyndicationItemProperty.Title, "property is not a valid SyndicationItemProperty enumeration");
            return syndicationItemToTargetPath[(int)property];
        }
#endif

        /// <summary>
        /// Given a <paramref name="targetPath"/> gets the corresponding syndication property.
        /// </summary>
        /// <param name="targetPath">Target path in the form of syndication property name</param>
        /// <returns>
        /// Enumerated value of a SyndicationItemProperty or SyndicationItemProperty.CustomProperty if the <paramref name="targetPath"/>
        /// does not map to any syndication property name.
        /// </returns>
        internal static SyndicationItemProperty MapEpmTargetPathToSyndicationProperty(String targetPath)
        {
            SyndicationItemProperty targetSyndicationItem;
            if (targetPathToSyndicationItem.TryGetValue(targetPath, out targetSyndicationItem))
            {
                return targetSyndicationItem;
            }

            return SyndicationItemProperty.CustomProperty;
        }

#if ASTORIA_SERVER
        /// <summary>
        /// Translates content kind to string for csdl
        /// </summary>
        /// <param name="contentKind">ContentKind</param>
        /// <returns>String corresponding to contentKind</returns>
        internal static String MapSyndicationTextContentKindToEpmContentKind(SyndicationTextContentKind contentKind)
        {
            switch (contentKind)
            {
                case SyndicationTextContentKind.Plaintext:
                    return XmlConstants.SyndContentKindPlaintext;
                case SyndicationTextContentKind.Html:
                    return XmlConstants.SyndContentKindHtml;
                default:
                    Debug.Assert(contentKind == SyndicationTextContentKind.Xhtml, "Unexpected syndication text content kind");
                    return XmlConstants.SyndContentKindXHtml;
            }
        }

#endif

        /// <summary>
        /// Given the string representation in <paramref name="strContentKind"/> gets back the corresponding enumerated value
        /// </summary>
        /// <param name="strContentKind">String representation of syndication content kind e.g. plaintext, html or xhtml</param>
        /// <param name="typeName">Type to which the property belongs</param>
        /// <param name="memberName">Name of the member whose extended properties we are searching from</param>
        /// <returns>Enumerated value of SyndicationTextContentKind</returns>
        internal static SyndicationTextContentKind MapEpmContentKindToSyndicationTextContentKind(String strContentKind, String typeName, String memberName)
        {
            SyndicationTextContentKind contentKind;

            switch (strContentKind)
            {
                case XmlConstants.SyndContentKindPlaintext:
                    contentKind = SyndicationTextContentKind.Plaintext;
                    break;
                case XmlConstants.SyndContentKindHtml:
                    contentKind = SyndicationTextContentKind.Html;
                    break;
                case XmlConstants.SyndContentKindXHtml:
                    contentKind = SyndicationTextContentKind.Xhtml;
                    break;
                default:
                    throw new InvalidOperationException(memberName == null ?
                                Strings.ObjectContext_InvalidValueForTargetTextContentKindPropertyType(strContentKind, typeName) :
                                Strings.ObjectContext_InvalidValueForTargetTextContentKindPropertyMember(strContentKind, memberName, typeName));
            }

            return contentKind;
        }
    }
}

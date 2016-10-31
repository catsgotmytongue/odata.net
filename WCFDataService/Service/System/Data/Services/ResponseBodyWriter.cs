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

namespace System.Data.Services
{
    #region Namespaces
    using System;
    using System.Data.Services.Providers;
    using System.Data.Services.Serializers;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using Microsoft.Data.Edm;
    using Microsoft.Data.OData;
    #endregion Namespaces

    /// <summary>
    /// Use this class to encapsulate writing the body of the outgoing response 
    /// for a data request.
    /// </summary>
    internal class ResponseBodyWriter
    {
        /// <summary>Encoding, if available.</summary>
        private readonly Encoding encoding;

        /// <summary>RequestMessage for the request being processed.</summary>
        private readonly IDataService service;

        /// <summary>Enumerator for results.</summary>
        private readonly QueryResultInfo queryResults;

        /// <summary>Description of request made to the system.</summary>
        private readonly RequestDescription requestDescription;

        /// <summary>ODataMessageWriter using which the response needs to be written.</summary>
        private readonly ODataMessageWriter messageWriter;

        /// <summary>IODataResponseMessage containing all the response headers. For an inner batch message, note that this
        /// is the actual ODataLib message, and it's headers will be overridden when after SerializeResponseBody finishes.
        /// So, if using this to set headers, BE VERY CAREFUL.</summary>
        private readonly IODataResponseMessage actualResponseMessageWhoseHeadersMayBeOverridden;

        /// <summary>The content format.</summary>
        private readonly ODataFormat contentFormat;

        /// <summary>If the target is a Media Resource, this holds the read stream for the Media Resource.</summary>
        private readonly Stream mediaResourceStream;

        /// <summary>
        /// Object to create a message writer.
        /// </summary>
        private readonly MessageWriterBuilder messageWriterBuilder;

        /// <summary>Initializes a new <see cref="ResponseBodyWriter"/> that can write the body of a response.</summary>
        /// <param name="service">Service for the request being processed.</param>
        /// <param name="queryResults">Enumerator for results.</param>
        /// <param name="requestDescription">Description of request made to the system.</param>        
        /// <param name="actualResponseMessageWhoseHeadersMayBeOverridden">IODataResponseMessage instance for the response.</param>
        internal ResponseBodyWriter(
            IDataService service,
            QueryResultInfo queryResults,
            RequestDescription requestDescription, 
            IODataResponseMessage actualResponseMessageWhoseHeadersMayBeOverridden)
        {
            Debug.Assert(service != null, "service != null");
            Debug.Assert(requestDescription != null, "requestDescription != null");
            Debug.Assert(actualResponseMessageWhoseHeadersMayBeOverridden != null, "actualResponseMessageWhoseHeadersMayBeOverridden != null");

            this.service = service;
            this.queryResults = queryResults;
            this.requestDescription = requestDescription;
            this.actualResponseMessageWhoseHeadersMayBeOverridden = actualResponseMessageWhoseHeadersMayBeOverridden;

            Debug.Assert(this.PayloadKind != ODataPayloadKind.Unsupported, "payloadKind != ODataPayloadKind.Unsupported");

            this.encoding = ContentTypeUtil.EncodingFromAcceptCharset(this.service.OperationContext.RequestMessage.GetRequestAcceptCharsetHeader());

            if (this.PayloadKind == ODataPayloadKind.Entry ||
                this.PayloadKind == ODataPayloadKind.Feed ||
                this.PayloadKind == ODataPayloadKind.Property ||
                this.PayloadKind == ODataPayloadKind.Collection ||
                this.PayloadKind == ODataPayloadKind.EntityReferenceLink ||
                this.PayloadKind == ODataPayloadKind.EntityReferenceLinks ||
                this.PayloadKind == ODataPayloadKind.Error ||
                this.PayloadKind == ODataPayloadKind.ServiceDocument ||
                this.PayloadKind == ODataPayloadKind.Parameter)
            {
                AstoriaRequestMessage requestMessage = service.OperationContext.RequestMessage;
                IODataResponseMessage responseMessageOnOperationContext = service.OperationContext.ResponseMessage;

                Version effectiveMaxResponseVersion = VersionUtil.GetEffectiveMaxResponseVersion(service.Configuration.DataServiceBehavior.MaxProtocolVersion.ToVersion(), requestMessage.RequestMaxVersion);
                bool isEntityOrFeed = this.PayloadKind == ODataPayloadKind.Entry || this.PayloadKind == ODataPayloadKind.Feed;
                if (ContentTypeUtil.IsResponseMediaTypeJsonLight(requestMessage.GetAcceptableContentTypes(), isEntityOrFeed, effectiveMaxResponseVersion))
                {
                    // If JSON light 'wins', then bump the version to V3.
                    requestDescription.VerifyAndRaiseResponseVersion(VersionUtil.Version3Dot0, service);
                    responseMessageOnOperationContext.SetHeader(XmlConstants.HttpDataServiceVersion, XmlConstants.DataServiceVersion3Dot0 + ";");
                }
            }

            if (this.requestDescription.TargetKind == RequestTargetKind.MediaResource)
            {
                Debug.Assert(this.PayloadKind == ODataPayloadKind.BinaryValue, "payloadKind == ODataPayloadKind.BinaryValue");

                // Note that GetReadStream will set the ResponseETag before it returns
                this.mediaResourceStream = service.StreamProvider.GetReadStream(
                    this.queryResults.Current,
                    this.requestDescription.StreamProperty,
                    this.service.OperationContext);
            }
            else if (this.PayloadKind != ODataPayloadKind.BinaryValue)
            {
                IEdmModel model;
                if (this.PayloadKind == ODataPayloadKind.MetadataDocument)
                {
                    model = MetadataSerializer.PrepareModelForSerialization(this.service.Provider, this.service.Configuration);
                }
                else
                {
                    model = this.GetModelFromService();
                }

                // Create the message writer using which the response needs to be written.
                this.messageWriterBuilder = MessageWriterBuilder.ForNormalRequest(
                    this.service,
                    this.requestDescription,
                    this.actualResponseMessageWhoseHeadersMayBeOverridden,
                    model);
                
                this.messageWriter = this.messageWriterBuilder.CreateWriter();

                try
                {
                    // Make sure all the headers are written before the method returns.
                    this.contentFormat = ODataUtils.SetHeadersForPayload(this.messageWriter, this.PayloadKind);
                }
                catch (ODataContentTypeException contentTypeException)
                {
                    throw new DataServiceException(415, null, Strings.DataServiceException_UnsupportedMediaType, null, contentTypeException);
                }

                Debug.Assert(requestDescription.ResponseFormat != null, "Response format should already have been determined.");
                Debug.Assert(ReferenceEquals(this.contentFormat, requestDescription.ResponseFormat.Format), "Response format in request description did not match format when writing.");

                if (this.PayloadKind == ODataPayloadKind.Value && !String.IsNullOrEmpty(this.requestDescription.MimeType))
                {
                    this.actualResponseMessageWhoseHeadersMayBeOverridden.SetHeader(XmlConstants.HttpContentType, this.requestDescription.MimeType);
                }
                
                // In astoria, there is a bug in V1/V2 that while computing response version, we did not take
                // epm into account. Hence while creating the writer, we need to pass the RequestDescription.ActualResponseVersion
                // so that ODataLib can do the correct payload validation. But we need to write the response version without
                // the epm into the response headers because of backward-compat issue. Hence over-writing the response version
                // header with the wrong version value.
                string responseVersion = this.requestDescription.ResponseVersion.ToString() + ";";
                this.actualResponseMessageWhoseHeadersMayBeOverridden.SetHeader(XmlConstants.HttpDataServiceVersion, responseVersion);
            }
        }

        /// <summary>Gets the absolute URI to the service.</summary>
        internal Uri AbsoluteServiceUri
        {
            get { return this.service.OperationContext.AbsoluteServiceUri; }
        }

        /// <summary>Gets the <see cref="DataServiceProviderWrapper"/> for this response.</summary>
        internal DataServiceProviderWrapper Provider
        {
            get { return this.service.Provider; }
        }

        /// <summary>Content format for response.</summary>
        private ODataPayloadKind PayloadKind
        {
            get { return this.requestDescription.ResponsePayloadKind; }
        }

        /// <summary>Writes the request body to the specified <see cref="Stream"/>.</summary>
        /// <param name="stream">Stream to write to.</param>
        internal void Write(Stream stream)
        {
            Stream responseMessageStream = null;
            Serializer responseSerializer = null;
            this.actualResponseMessageWhoseHeadersMayBeOverridden.SetStream(stream);

            try
            {
                switch (this.PayloadKind)
                {
                    case ODataPayloadKind.BinaryValue:
                        Debug.Assert(
                            this.requestDescription.TargetKind == RequestTargetKind.OpenPropertyValue ||
                            this.requestDescription.TargetKind == RequestTargetKind.PrimitiveValue ||
                            this.requestDescription.TargetKind == RequestTargetKind.MediaResource,
                            this.requestDescription.TargetKind + " is PrimitiveValue or OpenPropertyValue or StreamPropertyValue");
                        Debug.Assert(this.messageWriter == null, "No Message writer required for writing binary payload");

                        responseMessageStream = this.actualResponseMessageWhoseHeadersMayBeOverridden.GetStream();
                        BinarySerializer binarySerializer = new BinarySerializer(responseMessageStream);
                        if (this.requestDescription.TargetKind == RequestTargetKind.MediaResource)
                        {
                            // If this.mediaResourceStream is null, we will set the status code to 204.
                            // We would not get here on a WCF host, but on other hosts we would.
                            Debug.Assert(this.requestDescription.IsNamedStream || this.mediaResourceStream != null, "this.mediaResourceStream cannot be null for MR request.");
                            if (this.mediaResourceStream != null)
                            {
                                binarySerializer.WriteRequest(this.mediaResourceStream, this.service.StreamProvider.StreamBufferSize);
                            }
                        }
                        else
                        {
                            binarySerializer.WriteRequest(this.queryResults.Current);
                        }

                        break;

                    case ODataPayloadKind.Value:
                        Debug.Assert(
                            this.requestDescription.TargetKind == RequestTargetKind.OpenPropertyValue ||
                            this.requestDescription.TargetKind == RequestTargetKind.PrimitiveValue,
                            this.requestDescription.TargetKind + " is PrimitiveValue or OpenPropertyValue");

                        TextSerializer textSerializer = new TextSerializer(this.messageWriter);
                        textSerializer.WriteRequest(this.queryResults.Current);
                        break;

                    case ODataPayloadKind.ServiceDocument:
                        Debug.Assert(this.requestDescription.TargetKind == RequestTargetKind.ServiceDirectory, "this.requestDescription.TargetKind == RequestTargetKind.ServiceDirectory");
                        ServiceDocumentSerializer serializer = new ServiceDocumentSerializer(this.messageWriter);
                        serializer.WriteServiceDocument(this.service.Provider);
                        break;

                    case ODataPayloadKind.EntityReferenceLink:
                    case ODataPayloadKind.EntityReferenceLinks:
                    case ODataPayloadKind.Collection:
                    case ODataPayloadKind.Property:
                        Debug.Assert(this.requestDescription.TargetKind != RequestTargetKind.Resource || this.requestDescription.LinkUri, "this.requestDescription.TargetKind != RequestTargetKind.Resource || this.requestDescription.LinkUri");
                        NonEntitySerializer nonEntitySerializer = new NonEntitySerializer(this.requestDescription, this.AbsoluteServiceUri, this.service, this.messageWriter);
                        responseSerializer = nonEntitySerializer;
                        nonEntitySerializer.WriteRequest(this.queryResults);
                        break;

                    case ODataPayloadKind.Entry:
                    case ODataPayloadKind.Feed:
                        Debug.Assert(this.requestDescription.TargetKind == RequestTargetKind.Resource, "TargetKind " + this.requestDescription.TargetKind + " == Resource");
                        EntitySerializer entitySerializer = new EntitySerializer(
                               this.requestDescription,
                               this.AbsoluteServiceUri,
                               this.service,
                               this.service.OperationContext.ResponseMessage.GetHeader(XmlConstants.HttpResponseETag),
                               this.messageWriter,
                               this.contentFormat);

                        responseSerializer = entitySerializer;
                        entitySerializer.WriteRequest(this.queryResults);
                        break;

                    default:
                        Debug.Assert(this.PayloadKind == ODataPayloadKind.MetadataDocument, "this.payloadKind == ODataPayloadKind.MetadataDocument");
                        Debug.Assert(this.requestDescription.TargetKind == RequestTargetKind.Metadata, "this.requestDescription.TargetKind == RequestTargetKind.Metadata");
                        
                        this.messageWriter.WriteMetadataDocument();
                        break;
                }
            }
            catch (Exception exception)
            {
                if (!CommonUtil.IsCatchableExceptionType(exception))
                {
                    throw;
                }

                if (responseSerializer != null)
                {
                    responseSerializer.Flush();
                }

                ErrorHandler.HandleExceptionWhileWriting(this.service, exception, this.actualResponseMessageWhoseHeadersMayBeOverridden, this.messageWriter, this.encoding, stream, this.messageWriterBuilder);
            }
            finally
            {
                WebUtil.Dispose(this.messageWriter);
                WebUtil.Dispose(this.queryResults);
                WebUtil.Dispose(this.mediaResourceStream);

                // For batch operations, we need to dispose the stream obtained from calling 
                // IODataResponseMessage.GetStream to signal that we are done with the message.
                if (responseMessageStream != null && this.actualResponseMessageWhoseHeadersMayBeOverridden is ODataBatchOperationResponseMessage)
                {
                    responseMessageStream.Dispose();
                }
            }
        }

        /// <summary>
        /// Gets the EDM model from the service.
        /// </summary>
        /// <returns>The EDM model or null.</returns>
        private IEdmModel GetModelFromService()
        {
            Debug.Assert(this.service != null, "this.service != null");

            // DEVNOTE: Its unclear why this check for OperationContext being non-null is needed,
            // or what it has to do with the model. It was refactored from another place, and more
            // investigation is needed.
            if (this.service.OperationContext != null)
            {
                Debug.Assert(this.requestDescription != null, "this.requestDescription != null");
                bool isEntryOrFeed = this.requestDescription.TargetKind == RequestTargetKind.Resource;
                if (!ContentTypeUtil.IsResponseMediaTypeJsonLight(this.service, isEntryOrFeed))
                {
                    Debug.Assert(this.service.Provider != null, "this.service.Provider != null");
                    MetadataProviderEdmModel metadataProviderEdmModel = this.service.Provider.GetMetadataProviderEdmModel();
                    Debug.Assert(metadataProviderEdmModel.Mode == MetadataProviderEdmModelMode.Serialization, "Model expected to be in serialization mode.");
                    return metadataProviderEdmModel;
                }
            }

            return null;
        }
    }
}

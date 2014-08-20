﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Newtonsoft.Json;
using SODA.Utilities;

namespace SODA
{
    /// <summary>
    /// A class for interacting with Socrata Data Portals using the Socrata Open Data API.
    /// </summary>
    public class SodaClient
    {
        #region data

        /// <summary>
        /// The Socrata Open Data Portal that this client will target.
        /// </summary>
        public readonly string Host;

        /// <summary>
        /// The Socrata application token that this client will use for all requests.
        /// </summary>
        /// <remarks>
        /// Since SodaClient uses Basic Authentication, the application token is only used as a means to reduce API throttling on the part of Socrata.
        /// See http://dev.socrata.com/docs/app-tokens.html for more information.
        /// </remarks>
        public readonly string AppToken;

        /// <summary>
        /// The user account that this client will use for Authentication during each request.
        /// </summary>
        /// <remarks>
        /// Authentication is only necessary when accessing datasets that have been marked as private or when making write requests (PUT, POST, and DELETE).
        /// See http://dev.socrata.com/docs/authentication.html for more information.
        /// </remarks>
        public readonly string Username;
        
        //not publicly readable, can only be set in a constructor
        private readonly string password;

        #endregion

        #region implementation

        /// <summary>
        /// Helper method for creating an HttpWebRequest object.
        /// </summary>
        /// <param name="uri">The Uri to send the request to.</param>
        /// <param name="method">The HTTP method to use for the request.</param>
        /// <param name="dataFormat">The data format used for the request.</param>
        /// <param name="appToken">The Socrata App Token to send with the request.</param>
        /// <param name="username">The Socrata user account to use for the request.</param>
        /// <param name="password">The password for the specified Socrata <paramref name="username"/>.</param>
        /// <param name="payload">The body of the request.</param>
        internal static HttpWebRequest createRequest(Uri uri, string method, string appToken, string username, string password, SodaDataFormat dataFormat = SodaDataFormat.JSON, string payload = null)
        {
            var request = WebRequest.Create(uri) as HttpWebRequest;
            request.Method = method;
            request.ProtocolVersion = new System.Version("1.1");
            request.PreAuthenticate = true;

            request.Headers.Add("X-App-Token", appToken);

            if (!String.IsNullOrEmpty(username) && !String.IsNullOrEmpty(password))
            {
                string authKVP = String.Format("{0}:{1}", username, password);
                byte[] authBytes = Encoding.UTF8.GetBytes(authKVP);
                request.Headers.Add("Authorization", String.Format("Basic {0}", Convert.ToBase64String(authBytes)));
            }

            switch (dataFormat)
            {
                case SodaDataFormat.JSON:
                    request.Accept = "application/json";
                    if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                        request.ContentType = "application/json";
                    break;
                case SodaDataFormat.CSV:
                    switch(method)
                    {
                        case "GET":
                            request.Accept = "text/csv";
                            break;
                        case "POST":
                        case "PUT":
                            request.ContentType = "text/csv";
                            break;
                    }
                    break;
                case SodaDataFormat.XML:
                    request.Accept = "application/rdf+xml";
                    break;
            }

            if (!String.IsNullOrEmpty(payload))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(payload);

                using (var stream = request.GetRequestStream())
                {
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }
                
            return request;
        }

        /// <summary>
        /// Helper method for sending web requests.
        /// </summary>
        /// <typeparam name="TResult">The target type during response deserialization.</typeparam>
        /// <param name="webRequest">The HttpWebRequest to send.</param>
        internal static TResult sendRequest<TResult>(HttpWebRequest webRequest, SodaDataFormat dataFormat = SodaDataFormat.JSON) where TResult : class
        {
            TResult result = default(TResult);

            using (var responseStream = webRequest.GetResponse().GetResponseStream())
            {
                string response = new StreamReader(responseStream).ReadToEnd();

                switch (dataFormat)
                {
                    case SodaDataFormat.JSON:
                        try
                        {
                            result = JsonConvert.DeserializeObject<TResult>(response);
                        }
                        catch(Newtonsoft.Json.JsonReaderException)
                        {
                            result = response as TResult;
                        }
                        break;
                    case SodaDataFormat.CSV:
                        result = response as TResult;
                        break;
                    case SodaDataFormat.XML:
                        var ttype = typeof(TResult);
                        if (ttype == typeof(string))
                        {
                            result = response as TResult;
                        }
                        else
                        {
                            try
                            {
                                var reader = XmlReader.Create(new StringReader(response));
                                var serializer = new XmlSerializer(ttype);
                                result = serializer.Deserialize(reader) as TResult;
                            }
                            catch
                            {
                                result = response as TResult;
                            }
                        }
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Helper method for getting the response string a WebException.
        /// </summary>
        /// <param name="webException">The WebException whose response string will be read.</param>
        /// <returns>The response string if it exists, otherwise the Message property of the WebException.</returns>
        internal static string unwrapExceptionMessage(WebException webException)
        {
            string message = String.Empty;

            if (webException != null)
            {
                message = webException.Message;

                if (webException.Response != null)
                {
                    using (var streamReader = new StreamReader(webException.Response.GetResponseStream()))
                    {
                        message = streamReader.ReadToEnd();
                    }
                }
            }

            return message;
        }

        /// <summary>
        /// Send an HTTP GET request to the specified URI, and include an appropriate Accept header for the specified data format.
        /// </summary>
        /// <typeparam name="T">The .NET class to use for response deserialization.</typeparam>
        /// <param name="uri">A uniform resource identifier that is the target of this GET request.</param>
        /// <param name="dataFormat">One of the data-interchange formats that Socrata supports. The default is JSON.</param>
        /// <returns>The HTTP response, deserialized into an object of type <typeparamref name="T"/>.</returns>
        internal T get<T>(Uri uri, SodaDataFormat dataFormat = SodaDataFormat.JSON) where T : class
        {
            var request = createRequest(uri, "GET", AppToken, Username, password, dataFormat);

            return sendRequest<T>(request);
        }

        #endregion

        #region ctor

        /// <summary>
        /// Initialize a new SodaClient for the specified Socrata host, using the specified application token and the specified Authentication credentials.
        /// </summary>
        /// <param name="host">The Socrata Open Data Portal that this client will target.</param>
        /// <param name="appToken">The Socrata application token that this client will use for all requests.</param>
        /// <param name="username">The user account that this client will use for Authentication during each request.</param>
        /// <param name="password">The password for the specified <paramref name="username"/> that this client will use for Authentication during each request.</param>
        public SodaClient(string host, string appToken, string username, string password)
        {
            if (String.IsNullOrEmpty(host))
                throw new ArgumentException("host", "A host is required");

            if (String.IsNullOrEmpty(appToken))
                throw new ArgumentException("appToken", "An app token is required");
            
            Host = SodaUri.enforceHttps(host);
            AppToken = appToken;
            Username = username;
            this.password = password;
        }
        
        /// <summary>
        /// Initialize a new (anonymous) SodaClient for the specified Socrata host, using the specified application token.
        /// </summary>
        /// <param name="host">The Socrata Open Data Portal that this client will target.</param>
        /// <param name="appToken">The Socrata application token that this client will use for all requests.</param>
        public SodaClient(string host, string appToken)
            : this(host, appToken, null, null)
        {
        }
                
        #endregion

        #region public API
        
        /// <summary>
        /// Get a ResourceMetadata object using the specified resource identifier.
        /// </summary>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>
        /// A ResourceMetadata object for the specified resource identifier.
        /// </returns>
        public ResourceMetadata GetMetadata(string resourceId)
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            var uri = SodaUri.ForMetadata(Host, resourceId);

            return get<ResourceMetadata>(uri);
        }

        /// <summary>
        /// Get a collection of ResourceMetadata objects on the specified page.
        /// </summary>
        /// <param name="page">The 1-indexed page of the Metadata Catalog on this client's Socrata host.</param>
        /// <returns>A collection of ResourceMetadata objects from the specified page of this client's Socrata host.</returns>
        public IEnumerable<ResourceMetadata> GetMetadataPage(int page)
        {
            if (page <= 0)
                throw new ArgumentOutOfRangeException("page", "Resouce metadata catalogs begin on page 1.");
            
            var catalogUri = SodaUri.ForMetadataList(Host, page);

            IEnumerable<dynamic> rawDataList = get<IEnumerable<dynamic>>(catalogUri).ToArray();

            foreach (var rawData in rawDataList)
            {
                var metadata = GetMetadata((string)rawData.id);

                yield return metadata;
            }
        }

        /// <summary>
        /// Get a Resource object using the specified resource identifier.
        /// </summary>
        /// <typeparam name="TRow">The .NET class that represents the type of the underlying row in the Resource.</typeparam>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A Resource object with an underlying row set of type <typeparamref name="TRow"/>.</returns>
        public Resource<TRow> GetResource<TRow>(string resourceId) where TRow : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            var metadata = GetMetadata(resourceId);

            return new Resource<TRow>(metadata, this);
        }

        /// <summary>
        /// Update/Insert the specified payload string using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A string of serialized data.</param>
        /// <param name="dataFormat">The data format used for serialization.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult"/> indicating success or failure.</returns>
        public SodaResult Upsert(string payload, SodaDataFormat dataFormat, string resourceId)
        {
            if (dataFormat == SodaDataFormat.XML)
                throw new ArgumentOutOfRangeException("dataFormat", "SodaDataFormat.XML is not a valid format for write operations.");

            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            var uri = SodaUri.ForResourceAPI(Host, resourceId);

            var request = createRequest(uri, "POST", AppToken, Username, password, dataFormat, payload);
            SodaResult result;

            try
            {
                result = sendRequest<SodaResult>(request);
            }
            catch (WebException webEx)
            {
                string message = unwrapExceptionMessage(webEx);
                result = new SodaResult() { Message = webEx.Message, IsError = true, ErrorCode = message, Data = payload };
            }
            catch (Exception ex)
            {
                result = new SodaResult() { Message = ex.Message, IsError = true, ErrorCode = ex.Message, Data = payload };
            }

            return result;
        }
        
        /// <summary>
        /// Update/Insert the specified collection of entities using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult"/> indicating success or failure.</returns>
        public SodaResult Upsert<T>(IEnumerable<T> payload, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            string json = JsonConvert.SerializeObject(payload);

            return Upsert(json, SodaDataFormat.JSON, resourceId);
        }
        
        /// <summary>
        /// Update/Insert the specified collection of entities in batches of the specified size, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="batchSize">The maximum number of entities to process in a single batch.</param>
        /// <param name="breakFunction">A function which, when evaluated true, causes a batch to be sent (possibly before it reaches <paramref name="batchSize"/>).</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A collection of <see cref="SodaResult"/>, one for each batched Upsert.</returns>
        public IEnumerable<SodaResult> BatchUpsert<T>(IEnumerable<T> payload, int batchSize, Func<IEnumerable<T>, T, bool> breakFunction, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            Queue<T> queue = new Queue<T>(payload);

            while (queue.Any())
            {
                var batch = new List<T>();

                for (var index = 0; index < batchSize && queue.Count > 0; index++)
                {
                    if (breakFunction != null && breakFunction(batch, queue.Peek()))
                        break;

                    batch.Add(queue.Dequeue());
                }

                SodaResult result;

                try
                {
                    result = Upsert<T>(batch, resourceId);
                }
                catch (WebException webEx)
                {
                    string message = unwrapExceptionMessage(webEx);
                    result = new SodaResult() { Message = webEx.Message, IsError = true, ErrorCode = message, Data = payload };
                }
                catch (Exception ex)
                {
                    result = new SodaResult() { Message = ex.Message, IsError = true, ErrorCode = ex.Message, Data = payload };
                }

                yield return result;
            }
        }
        
        /// <summary>
        /// Update/Insert the specified collection of entities in batches of the specified size, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="batchSize">The maximum number of entities to process in a single batch.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A collection of <see cref="SodaResult"/>, one for each batch processed.</returns>
        public IEnumerable<SodaResult> BatchUpsert<T>(IEnumerable<T> payload, int batchSize, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            Func<IEnumerable<T>, T, bool> neverBreak = (a, b) => false;

            return BatchUpsert<T>(payload, batchSize, neverBreak, resourceId);
        }

        /// <summary>
        /// Replace any existing rows with the payload data, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A string of serialized data.</param>
        /// <param name="dataFormat">The data format used for serialization.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult"/> indicating success or failure.</returns>
        public SodaResult Replace(string payload, SodaDataFormat dataFormat, string resourceId)
        {
            if (dataFormat == SodaDataFormat.XML)
                throw new ArgumentOutOfRangeException("dataFormat", "SodaDataFormat.XML is not a valid format for write operations.");

            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            var uri = SodaUri.ForResourceAPI(Host, resourceId);

            var request = createRequest(uri, "PUT", AppToken, Username, password, dataFormat, payload);

            return sendRequest<SodaResult>(request);
        }

        /// <summary>
        /// Replace any existing rows with a collection of entities, using the specified resource identifier.
        /// </summary>
        /// <param name="payload">A collection of entities, where each represents a single row in the target resource.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult"/> indicating success or failure.</returns>
        public SodaResult Replace<T>(IEnumerable<T> payload, string resourceId) where T : class
        {
            if (FourByFour.IsNotValid(resourceId))
                throw new ArgumentOutOfRangeException("resourceId", "The provided resourceId is not a valid Socrata (4x4) resource identifier.");

            string json = JsonConvert.SerializeObject(payload);

            return Replace(json, SodaDataFormat.JSON, resourceId);
        }
               
        /// <summary>
        /// Delete a single row using the specified row identifier and the specified resource identifier.
        /// </summary>
        /// <param name="rowId">The identifier of the row to be deleted.</param>
        /// <param name="resourceId">The identifier (4x4) for a resource on the Socrata host to target.</param>
        /// <returns>A <see cref="SodaResult"/> indicating success or failure.</returns>
        public SodaResult DeleteRow(string rowId, string resourceId)
        {
            var uri = SodaUri.ForResourceAPI(Host, resourceId, rowId);

            var request = createRequest(uri, "DELETE", AppToken, Username, password);

            return sendRequest<SodaResult>(request);
        }
 
        #endregion
    }
}
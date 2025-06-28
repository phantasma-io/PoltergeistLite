using System;
using System.Collections;
using UnityEngine.Networking;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Phantasma.SDK
{
    public static class WebClient
    {
        public static int NoTimeout = 0;
        public static int DefaultTimeout = 30;
        private static long requestNumber = 0;
        private static object requestNumberLock = new object();
        private static long GetNextRequestNumber()
        {
            lock (requestNumberLock)
            {
                if (requestNumber == Int64.MaxValue)
                    requestNumber = 0;
                else
                    requestNumber++;
            }

            return requestNumber;
        }

        public class JsonRpcRequest
        {
            public string jsonrpc = "2.0";
            public string method;
            public string id = "1";
            public object[] @params;

            public JsonRpcRequest(string method, object[] parameters)
            {
                this.method = method;
                this.@params = parameters;
            }
        }
        public class JsonRpcError
        {
            public int code;
            public string message;
        }
        public class JsonRpcResponse<T>
        {
            public string jsonrpc;
            public string id;
            public T result;
            public JsonRpcError error;
        }
        public class JsonRpcResponse
        {
            public string jsonrpc;
            public string id;
            public object result;
            public JsonRpcError error;
        }

        public static IEnumerator RPCRequest<T>(string url, string method, int timeout, int retriesOnNetworkError, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback,
                                            Action<T> callback, params object[] parameters)
        {
            var json = JsonConvert.SerializeObject(new JsonRpcRequest(method, parameters));

            var requestNumber = GetNextRequestNumber();
            Log.Write($"RPC request [{requestNumber}]\nurl: {url}\njson: {json}", Log.Level.Networking);

            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            DateTime startTime = DateTime.Now;

            UnityWebRequest request;
            for (; ; )
            {
                request = new UnityWebRequest(url, "POST");
                request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (timeout > 0)
                    request.timeout = timeout;

                yield return request.SendWebRequest();

                if (request.error == null || retriesOnNetworkError == 0)
                {
                    // success
                    break;
                }

                Log.Write($"RPC network error [{requestNumber}], {retriesOnNetworkError} retries left.", Log.Level.Networking);
                Thread.Sleep(1000);
                retriesOnNetworkError--;
            }

            TimeSpan responseTime = DateTime.Now - startTime;

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                // Try extracting error details
                int? errorCode = null;
                string? errorMessage = null;
                try
                {
                    var stringResponse = request.downloadHandler.text;
                    var rpcResponse = JsonConvert.DeserializeObject<JsonRpcResponse>(stringResponse);
                    errorCode = rpcResponse?.error?.code;
                    errorMessage = rpcResponse?.error?.message;
                }
                catch
                {
                    // No parsable response body is available.
                }

                var error = request.error + $"\nURL: {url}\nIs connection error: {request.result == UnityWebRequest.Result.ConnectionError}\nIs protocol error: {request.result == UnityWebRequest.Result.ProtocolError}\nIs data processing error: {request.result == UnityWebRequest.Result.DataProcessingError}\nResponse code: {request.responseCode}";
                if(errorCode != null)
                {
                    error += "\nError code: " + errorCode.ToString();
                }
                if(errorMessage != null)
                {
                    error += "\nError message: " + errorMessage.ToString();
                }

                Log.Write($"RPC error [{requestNumber}]\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n" + error, Log.Level.Networking);
                if (errorHandlingCallback != null) errorHandlingCallback(EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR, error);
            }
            else
            {
                Log.Write($"RPC response [{requestNumber}]\nurl: {url}\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n{request.downloadHandler.text}", Log.Level.Networking);
                JsonRpcResponse<T> rpcResponse = null;

                try
                {
                    var stringResponse = request.downloadHandler.text;

                    try
                    {
                        rpcResponse = JsonConvert.DeserializeObject<JsonRpcResponse<T>>(stringResponse);
                    }
                    catch
                    {
                        if (method.ToUpper() == "GETNFT" && parameters.Length > 0 && ((string)parameters[0]).ToUpper() == "GAME")
                        {
                            Log.Write($"RPC response [{requestNumber}]\nurl: {url}\nFailed to parse GAME NFT, trying workaround. JSON: " + stringResponse, Log.Level.Logic);
                            // TODO remove later: Temporary HACK for binary data inside JSON
                            var cutFrom = stringResponse.IndexOf(",{\"Key\":\"OriginalMetadata\"", StringComparison.InvariantCultureIgnoreCase);
                            if (cutFrom > 0)
                            {
                                stringResponse = stringResponse.Substring(0, cutFrom) + "]}";
                                rpcResponse = JsonConvert.DeserializeObject<JsonRpcResponse<T>>(stringResponse);
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                    if (rpcResponse != null && rpcResponse.result != null && rpcResponse.error == null)
                    {
                        callback?.Invoke(rpcResponse.result);
                    }
                    else
                    {
                        if (rpcResponse?.error != null)
                        {
                            Log.Write($"RPC response [{requestNumber}]\nurl: {url}\nError node found: {rpcResponse.error.message}", Log.Level.Networking);
                            if (errorHandlingCallback != null) errorHandlingCallback(EPHANTASMA_SDK_ERROR_TYPE.API_ERROR, rpcResponse.error.message);
                        }
                        else
                        {
                            errorHandlingCallback?.Invoke(EPHANTASMA_SDK_ERROR_TYPE.FAILED_PARSING_JSON, "Invalid or null response");
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Write($"RPC response [{requestNumber}]\nurl: {url}\nFailed to parse JSON: " + e.ToString(), Log.Level.Networking);
                    if (errorHandlingCallback != null) errorHandlingCallback(EPHANTASMA_SDK_ERROR_TYPE.FAILED_PARSING_JSON, "Failed to parse RPC response: \"" + e.Message + "\"");
                    yield break;
                }
            }

            yield break;
        }

        public static IEnumerator RESTRequestT<T>(string url, int timeout, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback, Action<T> callback)
        {
            UnityWebRequest request;

            var requestNumber = GetNextRequestNumber();
            Log.Write($"REST request [{requestNumber}]\nurl: {url}", Log.Level.Networking);

            request = new UnityWebRequest(url, "GET");
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

            DateTime startTime = DateTime.Now;

            if (timeout > 0)
                request.timeout = timeout;
            
            yield return request.SendWebRequest();
            
            TimeSpan responseTime = DateTime.Now - startTime;

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                // Try extracting error details
                int? errorCode = null;
                string? errorMessage = null;
                try
                {
                    var stringResponse = request.downloadHandler.text;
                    var rpcResponse = JsonConvert.DeserializeObject<JsonRpcResponse>(stringResponse);
                    errorCode = rpcResponse?.error?.code;
                    errorMessage = rpcResponse?.error?.message;
                }
                catch
                {
                    // No parsable response body is available.
                }

                var error = request.error + $"\nURL: {url}\nIs connection error: {request.result == UnityWebRequest.Result.ConnectionError}\nIs protocol error: {request.result == UnityWebRequest.Result.ProtocolError}\nIs data processing error: {request.result == UnityWebRequest.Result.DataProcessingError}\nResponse code: {request.responseCode}";
                if(errorCode != null)
                {
                    error += "\nError code: " + errorCode.ToString();
                }
                if(errorMessage != null)
                {
                    error += "\nError message: " + errorMessage.ToString();
                }

                Log.Write($"REST error [{requestNumber}]\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n" + error, Log.Level.Networking);
                if (errorHandlingCallback != null) errorHandlingCallback(EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR, error);
            }
            else
            {
                T response = default;
                try
                {
                    Log.Write($"REST response [{requestNumber}]\nurl: {url}\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n{request.downloadHandler.text}", Log.Level.Networking);
                    response = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Log.Write(e.Message);
                }
                callback(response);
            }

            yield break;
        }

        public static IEnumerator RESTGet<T>(string url, int timeout, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback, Action<T> callback)
        {
            UnityWebRequest request;

            var requestNumber = GetNextRequestNumber();
            Log.Write($"REST request [{requestNumber}]\nurl: {url}", Log.Level.Networking);

            request = new UnityWebRequest(url, "GET");
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

            DateTime startTime = DateTime.Now;

            if (timeout > 0)
                request.timeout = timeout;
            
            yield return request.SendWebRequest();
            
            TimeSpan responseTime = DateTime.Now - startTime;

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                Log.Write($"REST error [{requestNumber}]\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec", Log.Level.Networking);
                if (errorHandlingCallback != null) errorHandlingCallback(EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR, null);
            }
            else
            {
                T response = default;
                try
                {
                    Log.Write($"REST response [{requestNumber}]\nurl: {url}\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n{request.downloadHandler.text}", Log.Level.Networking);
                    response = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                }
                catch(Exception e)
                {
                    Log.Write(e.Message);
                }
                callback(response);
            }

            yield break;
        }

        public static IEnumerator RESTPost<T>(string url, string serializedJson, bool deserializeResponse, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback, Action<T> callback)
        {
            UnityWebRequest request;

            var requestNumber = GetNextRequestNumber();
            Log.Write($"REST request (POST) [{requestNumber}]\nurl: {url}", Log.Level.Networking);

            Log.Write($"REST request (POST) [{requestNumber}]\nserializedJson: {serializedJson}", Log.Level.Debug1);

            request = new UnityWebRequest(url, "POST");

            byte[] data = Encoding.UTF8.GetBytes(serializedJson);
            request.uploadHandler = (UploadHandler)new UploadHandlerRaw(data);
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            DateTime startTime = DateTime.Now;
            yield return request.SendWebRequest();
            TimeSpan responseTime = DateTime.Now - startTime;

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                // Try extracting error details
                int? errorCode = null;
                string? errorMessage = null;
                try
                {
                    var stringResponse = request.downloadHandler.text;
                    var rpcResponse = JsonConvert.DeserializeObject<JsonRpcResponse>(stringResponse);
                    errorCode = rpcResponse?.error?.code;
                    errorMessage = rpcResponse?.error?.message;
                }
                catch
                {
                    // No parsable response body is available.
                }

                var error = request.error + $"\nURL: {url}\nIs connection error: {request.result == UnityWebRequest.Result.ConnectionError}\nIs protocol error: {request.result == UnityWebRequest.Result.ProtocolError}\nIs data processing error: {request.result == UnityWebRequest.Result.DataProcessingError}\nResponse code: {request.responseCode}";
                if(errorCode != null)
                {
                    error += "\nError code: " + errorCode.ToString();
                }
                if(errorMessage != null)
                {
                    error += "\nError message: " + errorMessage.ToString();
                }

                Log.Write($"REST error [{requestNumber}]\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n" + error, Log.Level.Networking);
                if (errorHandlingCallback != null) errorHandlingCallback(EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR, error);
            }
            else
            {
                Log.Write($"REST response [{requestNumber}]\nurl: {url}\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n{request.downloadHandler.text}", Log.Level.Networking);

                T response = default;
                try
                {
                    if(deserializeResponse)
                    {
                        response = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                    }
                    else
                    {
                        response = (T)(object)request.downloadHandler.text;
                    }
                }
                catch(Exception e)
                {
                    Log.Write(e.Message);
                }
                callback(response);
            }

            yield break;
        }

        public static IEnumerator Ping(string url, Action<EPHANTASMA_SDK_ERROR_TYPE, string> errorHandlingCallback, Action<TimeSpan> callback)
        {
            UnityWebRequest request;

            Log.Write($"Ping url: {url}", Log.Level.Networking);

            request = new UnityWebRequest(url, "GET");
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();

            DateTime startTime = DateTime.Now;
            yield return request.SendWebRequest();
            TimeSpan responseTime = DateTime.Now - startTime;

            // TODO return proper check later when PHA RPC would return something instead of 405 error code.
            // if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError || request.result == UnityWebRequest.Result.DataProcessingError)
            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                // Try extracting error details
                int? errorCode = null;
                string? errorMessage = null;
                try
                {
                    var stringResponse = request.downloadHandler.text;
                    var rpcResponse = JsonConvert.DeserializeObject<JsonRpcResponse>(stringResponse);
                    errorCode = rpcResponse?.error?.code;
                    errorMessage = rpcResponse?.error?.message;
                }
                catch
                {
                    // No parsable response body is available.
                }

                var error = request.error + $"\nURL: {url}\nIs connection error: {request.result == UnityWebRequest.Result.ConnectionError}\nIs protocol error: {request.result == UnityWebRequest.Result.ProtocolError}\nIs data processing error: {request.result == UnityWebRequest.Result.DataProcessingError}\nResponse code: {request.responseCode}";
                if(errorCode != null)
                {
                    error += "\nError code: " + errorCode.ToString();
                }
                if(errorMessage != null)
                {
                    error += "\nError message: " + errorMessage.ToString();
                }

                Log.Write($"Ping error error [{requestNumber}]\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n" + error, Log.Level.Networking);
                if (errorHandlingCallback != null) errorHandlingCallback(EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR, error);
            }
            else
            {
                Log.Write($"Ping response\nurl: {url}\nResponse time: {responseTime.Seconds}.{responseTime.Milliseconds} sec\n{request.downloadHandler.text}", Log.Level.Networking);
                callback(responseTime);
            }

            yield break;
        }
    }
}
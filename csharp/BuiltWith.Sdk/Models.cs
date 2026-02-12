using System.Text.Json.Serialization;

namespace BuiltWith.Sdk
{
    public class SdkResult
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("data")]
        public object Data { get; set; }

        [JsonPropertyName("raw")]
        public object Raw { get; set; }

        [JsonPropertyName("error")]
        public SdkError Error { get; set; }

        [JsonPropertyName("meta")]
        public SdkMeta Meta { get; set; }
    }

    public class SdkError
    {
        [JsonPropertyName("error_code")]
        public string ErrorCode { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("http_status")]
        public int HttpStatus { get; set; }

        [JsonPropertyName("details")]
        public object Details { get; set; }

        [JsonPropertyName("suggested_fix")]
        public string SuggestedFix { get; set; }
    }

    public class SdkMeta
    {
        [JsonPropertyName("request_id")]
        public string RequestId { get; set; }

        [JsonPropertyName("tool")]
        public string Tool { get; set; }

        [JsonPropertyName("cached")]
        public bool? Cached { get; set; }
    }

    public class BuiltWithException : System.Exception
    {
        public string ErrorCode { get; }
        public int HttpStatus { get; }
        public string SuggestedFix { get; }

        public BuiltWithException(string errorCode, string message, int httpStatus, string suggestedFix = null)
            : base(message)
        {
            ErrorCode = errorCode;
            HttpStatus = httpStatus;
            SuggestedFix = suggestedFix;
        }

        public SdkError ToSdkError()
        {
            return new SdkError
            {
                ErrorCode = ErrorCode,
                Message = Message,
                HttpStatus = HttpStatus,
                SuggestedFix = SuggestedFix
            };
        }
    }

    internal class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string Jsonrpc { get; set; } = "2.0";

        [JsonPropertyName("method")]
        public string Method { get; set; } = "tools/call";

        [JsonPropertyName("params")]
        public JsonRpcParams Params { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    internal class JsonRpcParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("arguments")]
        public object Arguments { get; set; }
    }

    internal class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string Jsonrpc { get; set; }

        [JsonPropertyName("result")]
        public object Result { get; set; }

        [JsonPropertyName("error")]
        public JsonRpcError Error { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    internal class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
